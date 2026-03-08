using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using DiscoveryAgent.Services;
using System.Net;

namespace DiscoveryAgent.Functions;

public class KnowledgeFunction
{
    private readonly KnowledgeStore _store;

    public KnowledgeFunction(KnowledgeStore store)
    {
        _store = store;
    }

    [Function("Knowledge")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "knowledge/{contextId}")] HttpRequestData req,
        string contextId)
    {
        var knowledge = await _store.GetByContextAsync(contextId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(knowledge);
        return response;
    }
}
