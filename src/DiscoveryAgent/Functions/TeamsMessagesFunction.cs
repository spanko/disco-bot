using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Handlers;
using System.Net;
using Newtonsoft.Json;

namespace DiscoveryAgent.Functions;

public class TeamsMessagesFunction
{
    private readonly ConversationHandler _handler;
    private readonly ILogger<TeamsMessagesFunction> _logger;

    public TeamsMessagesFunction(ConversationHandler handler, ILogger<TeamsMessagesFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Messages")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "messages")] HttpRequestData req)
    {
        try
        {
            // Read the Bot Framework Activity
            var body = await req.ReadAsStringAsync();
            var activity = JsonConvert.DeserializeObject<Activity>(body);

            if (activity == null)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid activity");
                return badResponse;
            }

            _logger.LogInformation("Received {ActivityType} from Teams user {UserId} in conversation {ConversationId}",
                activity.Type, activity.From?.Id, activity.Conversation?.Id);

            // Only process message activities
            if (activity.Type != ActivityTypes.Message || string.IsNullOrWhiteSpace(activity.Text))
            {
                var okResponse = req.CreateResponse(HttpStatusCode.OK);
                return okResponse;
            }

            // Extract user ID and conversation ID from Teams activity
            var userId = activity.From?.Id ?? "unknown";
            var conversationId = activity.Conversation?.Id;

            // Map Teams conversation to our thread ID format
            var threadId = string.IsNullOrEmpty(conversationId) ? null : $"teams-{conversationId}";

            // Create our conversation request
            var request = new ConversationRequest(
                UserId: userId,
                Message: activity.Text,
                ThreadId: threadId,
                ContextId: null
            );

            // Process with our agent
            var result = await _handler.HandleAsync(request);

            // Create Bot Framework response activity
            var replyActivity = new Activity
            {
                Type = ActivityTypes.Message,
                Text = result.Response,
                Conversation = activity.Conversation,
                Recipient = activity.From,
                From = activity.Recipient,
                ReplyToId = activity.Id
            };

            _logger.LogInformation("Sending response to Teams conversation {ConversationId}", conversationId);

            // Return the activity as JSON
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonConvert.SerializeObject(replyActivity));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Teams message");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }
}
