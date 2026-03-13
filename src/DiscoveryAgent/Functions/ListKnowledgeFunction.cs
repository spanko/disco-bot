using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Azure.Cosmos;
using Microsoft.Extensions.Options;
using DiscoveryAgent.Configuration;

namespace DiscoveryAgent.Functions;

public class ListKnowledgeFunction
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<ListKnowledgeFunction> _logger;
    private readonly DiscoveryBotConfig _config;

    public ListKnowledgeFunction(
        CosmosClient cosmosClient,
        IOptions<DiscoveryBotConfig> config,
        ILogger<ListKnowledgeFunction> logger)
    {
        _cosmosClient = cosmosClient;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/manage/knowledge
    /// Returns all knowledge items with full metadata for admin portal
    /// </summary>
    [Function("ListKnowledge")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manage/knowledge")] HttpRequestData req)
    {
        try
        {
            // Parse query parameters for filtering
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var contextId = query["contextId"];
            var threadId = query["threadId"];
            var limit = int.TryParse(query["limit"], out var l) ? l : 100;
            var offset = int.TryParse(query["offset"], out var o) ? o : 0;

            _logger.LogInformation("Retrieving knowledge items (limit: {Limit}, offset: {Offset})", limit, offset);

            // Get the Cosmos container for knowledge items
            var database = _cosmosClient.GetDatabase(_config.CosmosDatabase);
            var container = database.GetContainer("knowledge-items");

            // Build query for knowledge items
            var queryText = "SELECT * FROM c ORDER BY c.extractionTimestamp DESC";

            // Add filtering if provided
            var filters = new List<string>();
            if (!string.IsNullOrEmpty(contextId))
            {
                filters.Add($"c.relatedContextId = '{contextId}'");
            }
            if (!string.IsNullOrEmpty(threadId))
            {
                filters.Add($"c.sourceThreadId = '{threadId}'");
            }

            if (filters.Any())
            {
                queryText = $"SELECT * FROM c WHERE {string.Join(" AND ", filters)} ORDER BY c.extractionTimestamp DESC";
            }

            queryText += $" OFFSET {offset} LIMIT {limit}";

            var queryDefinition = new QueryDefinition(queryText);
            var queryIterator = container.GetItemQueryIterator<dynamic>(queryDefinition);

            var knowledgeItems = new List<object>();
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    knowledgeItems.Add(new
                    {
                        id = item.id?.ToString(),
                        content = item.content?.ToString(),
                        category = item.category?.ToString() ?? "Uncategorized",
                        confidence = item.confidence ?? 0.0,
                        sourceUserId = item.sourceUserId?.ToString(),
                        sourceUserRole = item.sourceUserRole?.ToString(),
                        sourceThreadId = item.sourceThreadId?.ToString(),
                        sourceMessageId = item.sourceMessageId?.ToString(),
                        extractionTimestamp = item.extractionTimestamp?.ToString(),
                        relatedContextId = item.relatedContextId?.ToString(),
                        tags = item.tags ?? new List<string>(),
                        verified = item.verified ?? false
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} knowledge items", knowledgeItems.Count);

            var responseObj = req.CreateResponse(HttpStatusCode.OK);
            await responseObj.WriteAsJsonAsync(new
            {
                items = knowledgeItems,
                total = knowledgeItems.Count,
                offset = offset,
                limit = limit
            });
            return responseObj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve knowledge items");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to retrieve knowledge items",
                details = ex.Message
            });
            return errorResponse;
        }
    }
}