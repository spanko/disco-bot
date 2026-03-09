using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Models;

namespace DiscoveryAgent.Services;

/// <summary>
/// Service for managing discovery contexts in Cosmos DB.
/// Provides CRUD operations for admin portal.
/// </summary>
public class ContextManagementService
{
    private readonly Database _cosmosDb;
    private readonly ILogger<ContextManagementService> _logger;

    public ContextManagementService(Database cosmosDb, ILogger<ContextManagementService> logger)
    {
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// Get all discovery contexts
    /// </summary>
    public async Task<List<DiscoveryContext>> GetAllContextsAsync()
    {
        var container = _cosmosDb.GetContainer("discovery-sessions");

        // Query for all DiscoveryContext documents
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.type = @type OR NOT IS_DEFINED(c.type)")
            .WithParameter("@type", "DiscoveryContext");

        var iterator = container.GetItemQueryIterator<DiscoveryContext>(query);
        var contexts = new List<DiscoveryContext>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            contexts.AddRange(response.Where(c => !string.IsNullOrEmpty(c.ContextId)));
        }

        _logger.LogInformation("Retrieved {Count} discovery contexts", contexts.Count);
        return contexts;
    }

    /// <summary>
    /// Get a single context by ID
    /// </summary>
    public async Task<DiscoveryContext?> GetContextAsync(string contextId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("discovery-sessions");
            var response = await container.ReadItemAsync<DiscoveryContext>(
                contextId,
                new PartitionKey(contextId));

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Context {ContextId} not found", contextId);
            return null;
        }
    }

    /// <summary>
    /// Create or update a discovery context
    /// </summary>
    public async Task<DiscoveryContext> UpsertContextAsync(DiscoveryContext context)
    {
        var container = _cosmosDb.GetContainer("discovery-sessions");

        // Ensure ID and ContextId are set using record 'with' syntax
        var contextToSave = context;

        if (string.IsNullOrEmpty(context.ContextId))
        {
            var newContextId = Guid.NewGuid().ToString();
            contextToSave = contextToSave with { ContextId = newContextId };
        }

        if (string.IsNullOrEmpty(contextToSave.Id) || contextToSave.Id != contextToSave.ContextId)
        {
            contextToSave = contextToSave with { Id = contextToSave.ContextId };
        }

        // Set timestamp if not already set
        if (contextToSave.CreatedAt == default)
        {
            contextToSave = contextToSave with { CreatedAt = DateTime.UtcNow };
        }

        var response = await container.UpsertItemAsync(
            contextToSave,
            new PartitionKey(contextToSave.ContextId));

        _logger.LogInformation("Upserted context {ContextId}: {Name}", contextToSave.ContextId, contextToSave.Name);
        return response.Resource;
    }

    /// <summary>
    /// Delete a discovery context
    /// </summary>
    public async Task<bool> DeleteContextAsync(string contextId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("discovery-sessions");
            await container.DeleteItemAsync<DiscoveryContext>(
                contextId,
                new PartitionKey(contextId));

            _logger.LogInformation("Deleted context {ContextId}", contextId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Cannot delete context {ContextId}: not found", contextId);
            return false;
        }
    }
}
