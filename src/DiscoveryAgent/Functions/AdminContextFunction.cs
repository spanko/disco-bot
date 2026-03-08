using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using DiscoveryAgent.Services;
using System.Net;

namespace DiscoveryAgent.Functions;

public class AdminContextFunction
{
    private readonly AgentManager _manager;

    public AdminContextFunction(AgentManager manager)
    {
        _manager = manager;
    }

    [Function("AdminContext")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/context")] HttpRequestData req)
    {
        var context = await req.ReadFromJsonAsync<DiscoveryContextConfig>();
        if (context is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        await _manager.UpdateContextAsync(context);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "updated" });
        return response;
    }
}
