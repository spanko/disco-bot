using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Handlers;
using System.Net;

namespace DiscoveryAgent.Functions;

public class ConversationFunction
{
    private readonly ConversationHandler _handler;
    private readonly ILogger<ConversationFunction> _logger;

    public ConversationFunction(ConversationHandler handler, ILogger<ConversationFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Conversation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "conversation")] HttpRequestData req)
    {
        var request = await req.ReadFromJsonAsync<ConversationRequest>();
        if (request is null)
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid request body");
            return badResponse;
        }

        var result = await _handler.HandleAsync(request);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}
