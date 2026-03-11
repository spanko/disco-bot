using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Services;
using DiscoveryAgent.Models;
using System.Net;

namespace DiscoveryAgent.Functions;

public class AdminContextFunction
{
    private readonly ContextManagementService _contextService;
    private readonly ILogger<AdminContextFunction> _logger;

    public AdminContextFunction(
        ContextManagementService contextService,
        ILogger<AdminContextFunction> logger)
    {
        _contextService = contextService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/contexts - List all contexts
    /// </summary>
    [Function("AdminContextsList")]
    public async Task<HttpResponseData> GetContexts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/contexts")] HttpRequestData req)
    {
        try
        {
            var contexts = await _contextService.GetAllContextsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(contexts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving contexts");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve contexts" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/admin/context/{contextId} - Get single context
    /// </summary>
    [Function("AdminContextGet")]
    public async Task<HttpResponseData> GetContext(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/context/{contextId}")] HttpRequestData req,
        string contextId)
    {
        try
        {
            var context = await _contextService.GetContextAsync(contextId);

            if (context == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Context not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(context);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving context {ContextId}", contextId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve context" });
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /api/admin/context - Create or update context
    /// </summary>
    [Function("AdminContextUpsert")]
    public async Task<HttpResponseData> UpsertContext(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/context")] HttpRequestData req)
    {
        try
        {
            var config = await req.ReadFromJsonAsync<DiscoveryContextConfig>();
            if (config is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid request body" });
                return bad;
            }

            // Convert config to DiscoveryContext model
            var context = new DiscoveryContext
            {
                Id = config.ContextId,
                ContextId = config.ContextId,
                Name = config.Name,
                Description = config.Description,
                DiscoveryMode = Enum.TryParse<DiscoveryMode>(config.DiscoveryMode, true, out var mode)
                    ? mode
                    : DiscoveryMode.Hybrid,
                DiscoveryAreas = config.DiscoveryAreas ?? new List<string>(),
                KeyQuestions = config.KeyQuestions ?? new List<string>(),
                SensitiveAreas = config.SensitiveAreas ?? new List<string>(),
                SuccessCriteria = config.SuccessCriteria ?? new List<string>(),
                AgentInstructions = config.AgentInstructions
            };

            var saved = await _contextService.UpsertContextAsync(context);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(saved);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting context");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to save context", details = ex.Message });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /api/admin/context/{contextId} - Delete context
    /// </summary>
    [Function("AdminContextDelete")]
    public async Task<HttpResponseData> DeleteContext(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "admin/context/{contextId}")] HttpRequestData req,
        string contextId)
    {
        try
        {
            var deleted = await _contextService.DeleteContextAsync(contextId);

            if (!deleted)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Context not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, contextId });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting context {ContextId}", contextId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to delete context" });
            return errorResponse;
        }
    }
}
