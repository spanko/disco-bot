using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using DiscoveryAgent.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Services;

/// <summary>
/// Manages knowledge extraction, storage, and retrieval with full attribution.
/// Dual-writes to Cosmos DB (persistence) and Azure AI Search (retrieval).
/// </summary>
public class KnowledgeStore
{
    private readonly Database _cosmosDb;
    private readonly SearchClient _searchClient;
    private readonly ILogger<KnowledgeStore> _logger;

    public KnowledgeStore(
        Database cosmosDb,
        SearchClient searchClient,
        ILogger<KnowledgeStore> logger)
    {
        _cosmosDb = cosmosDb;
        _searchClient = searchClient;
        _logger = logger;
    }

    /// <summary>
    /// Stores a knowledge item in both Cosmos DB and Azure AI Search.
    /// Returns the item ID.
    /// </summary>
    public async Task<string> StoreAsync(KnowledgeItem item)
    {
        // Persist to Cosmos DB
        var container = _cosmosDb.GetContainer("knowledge-items");
        await container.UpsertItemAsync(item, new PartitionKey(item.RelatedContextId));

        // Index in Azure AI Search for semantic retrieval
        try
        {
            var searchDoc = new SearchDocument(new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["content"] = item.Content,
                ["category"] = item.Category.ToString(),
                ["confidence"] = item.Confidence,
                ["sourceUserId"] = item.SourceUserId,
                ["sourceUserRole"] = item.SourceUserRole,
                ["sourceThreadId"] = item.SourceThreadId,
                ["relatedContextId"] = item.RelatedContextId,
                ["tags"] = item.Tags,
                ["extractionTimestamp"] = item.ExtractionTimestamp.ToString("O"),
                ["verified"] = item.Verified,
            });

            await _searchClient.MergeOrUploadDocumentsAsync([searchDoc]);
        }
        catch (Exception ex)
        {
            // Non-fatal: Cosmos is the source of truth; search indexing can retry
            _logger.LogWarning(ex, "Failed to index knowledge item {Id} in search", item.Id);
        }

        _logger.LogInformation("Knowledge stored: {Id} [{Category}] confidence={Confidence:F2}",
            item.Id, item.Category, item.Confidence);

        return item.Id;
    }

    /// <summary>
    /// Retrieves all knowledge items for a given discovery context.
    /// </summary>
    public async Task<List<KnowledgeItem>> GetByContextAsync(string contextId)
    {
        var container = _cosmosDb.GetContainer("knowledge-items");
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.relatedContextId = @contextId ORDER BY c.extractionTimestamp DESC")
            .WithParameter("@contextId", contextId);

        var items = new List<KnowledgeItem>();
        using var iterator = container.GetItemQueryIterator<KnowledgeItem>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            items.AddRange(response);
        }

        return items;
    }

    /// <summary>
    /// Searches knowledge items semantically using Azure AI Search.
    /// Returns items with full attribution chain.
    /// </summary>
    public async Task<List<KnowledgeItem>> SearchAsync(
        string query, string? contextId = null, int maxResults = 10)
    {
        var options = new SearchOptions
        {
            Size = maxResults,
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default"
            }
        };

        if (!string.IsNullOrEmpty(contextId))
        {
            options.Filter = $"relatedContextId eq '{contextId}'";
        }

        var results = await _searchClient.SearchAsync<KnowledgeItem>(query, options);
        var items = new List<KnowledgeItem>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            if (result.Document is not null)
                items.Add(result.Document);
        }

        return items;
    }

    /// <summary>
    /// Gets the full provenance chain for a knowledge item.
    /// </summary>
    public async Task<KnowledgeProvenance?> TraceOriginAsync(string itemId, string contextId)
    {
        var container = _cosmosDb.GetContainer("knowledge-items");

        try
        {
            var response = await container.ReadItemAsync<KnowledgeItem>(
                itemId, new PartitionKey(contextId));
            var item = response.Resource;

            return new KnowledgeProvenance(
                Item: item,
                SourceUserId: item.SourceUserId,
                SourceUserRole: item.SourceUserRole,
                ThreadId: item.SourceThreadId,
                ExtractionTimestamp: item.ExtractionTimestamp,
                Verified: item.Verified
            );
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

public record KnowledgeProvenance(
    KnowledgeItem Item,
    string SourceUserId,
    string SourceUserRole,
    string ThreadId,
    DateTime ExtractionTimestamp,
    bool Verified
);
