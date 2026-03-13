using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Azure.AI.Agents.Persistent;
using Azure.Cosmos;
using Microsoft.Extensions.Options;
using DiscoveryAgent.Configuration;

namespace DiscoveryAgent.Functions;

public class ListThreadsFunction
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<ListThreadsFunction> _logger;
    private readonly DiscoveryBotConfig _config;

    public ListThreadsFunction(
        CosmosClient cosmosClient,
        IOptions<DiscoveryBotConfig> config,
        ILogger<ListThreadsFunction> logger)
    {
        _cosmosClient = cosmosClient;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/manage/threads
    /// Returns all conversation threads with metadata for admin portal
    /// </summary>
    [Function("ListThreads")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manage/threads")] HttpRequestData req)
    {
        try
        {
            // Parse query parameters for filtering
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var userId = query["userId"];
            var limit = int.TryParse(query["limit"], out var l) ? l : 100;
            var offset = int.TryParse(query["offset"], out var o) ? o : 0;

            _logger.LogInformation("Retrieving threads list (limit: {Limit}, offset: {Offset})", limit, offset);

            // Get the Cosmos container for threads
            var database = _cosmosClient.GetDatabase("agents-foundry-cosmos");
            var container = database.GetContainer("persistent-threads");

            // Build query for threads
            var queryText = "SELECT * FROM c WHERE c.type = 'thread' ORDER BY c._ts DESC";
            if (!string.IsNullOrEmpty(userId))
            {
                queryText = $"SELECT * FROM c WHERE c.type = 'thread' AND c.metadata.userId = '{userId}' ORDER BY c._ts DESC";
            }
            queryText += $" OFFSET {offset} LIMIT {limit}";

            var queryDefinition = new QueryDefinition(queryText);
            var queryIterator = container.GetItemQueryIterator<dynamic>(queryDefinition);

            var threads = new List<object>();
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    // Extract thread information
                    threads.Add(new
                    {
                        threadId = item.id?.ToString(),
                        userId = item.metadata?.userId?.ToString(),
                        createdAt = item._ts != null ? DateTimeOffset.FromUnixTimeSeconds((long)item._ts).DateTime : DateTime.MinValue,
                        lastActivity = item.lastActivity?.ToString(),
                        messageCount = item.messageCount ?? 0,
                        status = item.status?.ToString() ?? "active",
                        contextId = item.metadata?.contextId?.ToString(),
                        // Add any other relevant metadata
                    });
                }
            }

            _logger.LogInformation("Retrieved {Count} threads", threads.Count);

            var responseObj = req.CreateResponse(HttpStatusCode.OK);
            await responseObj.WriteAsJsonAsync(new
            {
                threads = threads,
                total = threads.Count,
                offset = offset,
                limit = limit
            });
            return responseObj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve threads list");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to retrieve threads",
                details = ex.Message
            });
            return errorResponse;
        }
    }
}