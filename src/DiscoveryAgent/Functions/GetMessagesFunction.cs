using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Azure.AI.Agents.Persistent;

namespace DiscoveryAgent.Functions;

public class GetMessagesFunction
{
    private readonly PersistentAgentsClient _agentsClient;
    private readonly ILogger<GetMessagesFunction> _logger;

    public GetMessagesFunction(PersistentAgentsClient agentsClient, ILogger<GetMessagesFunction> logger)
    {
        _agentsClient = agentsClient;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/conversation/{threadId}/messages
    /// Returns the message history for a given thread so the web chat frontend
    /// can restore a session after page reload / navigation.
    /// </summary>
    [Function("GetMessages")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "conversation/{threadId}/messages")] HttpRequestData req,
        string threadId)
    {
        try
        {
            // Get userId from query params (optional, for logging)
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var userId = query["userId"];

            _logger.LogInformation("Retrieving messages for thread {ThreadId}, user {UserId}", threadId, userId);

            // Retrieve all messages in the thread (Foundry persists these in Cosmos)
            var messagesPageable = _agentsClient.Messages.GetMessagesAsync(threadId);
            var messagesList = new List<PersistentThreadMessage>();

            await foreach (var message in messagesPageable)
            {
                messagesList.Add(message);
            }

            if (messagesList.Count == 0)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { error = "Thread not found or empty" });
                return notFoundResponse;
            }

            // Map Foundry messages to a simple DTO the frontend can render
            var messages = messagesList
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    role = m.Role == MessageRole.Agent ? "assistant" : "user",
                    content = string.Join("\n", m.ContentItems
                        .OfType<MessageTextContent>()
                        .Select(c => c.Text)),
                    created = m.CreatedAt
                })
                .Where(m => !string.IsNullOrWhiteSpace(m.content))
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { threadId, messages });
            return response;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Thread {ThreadId} not found", threadId);
            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteAsJsonAsync(new { error = "Thread not found or expired" });
            return notFoundResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve messages for thread {ThreadId}", threadId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Could not retrieve conversation history",
                details = ex.Message
            });
            return errorResponse;
        }
    }
}