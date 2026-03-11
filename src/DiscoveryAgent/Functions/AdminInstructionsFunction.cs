using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace DiscoveryAgent.Functions;

public class AdminInstructionsFunction
{
    private readonly ILogger<AdminInstructionsFunction> _logger;
    private const string InstructionsPath = "config/instructions.md";

    public AdminInstructionsFunction(ILogger<AdminInstructionsFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/instructions - Get instructions.md content
    /// </summary>
    [Function("AdminInstructionsGet")]
    public async Task<HttpResponseData> GetInstructions(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/instructions")] HttpRequestData req)
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDirectory, InstructionsPath);

            _logger.LogInformation("Attempting to read instructions from: {FullPath}", fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Instructions file not found at: {FullPath}", fullPath);
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync($"Instructions file not found at {fullPath}");
                return notFound;
            }

            var content = await File.ReadAllTextAsync(fullPath);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync(content);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading instructions file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error reading instructions: {ex.Message}");
            return errorResponse;
        }
    }

    /// <summary>
    /// POST /api/admin/instructions - Save instructions.md content
    /// </summary>
    [Function("AdminInstructionsSave")]
    public async Task<HttpResponseData> SaveInstructions(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/instructions")] HttpRequestData req)
    {
        try
        {
            var content = await req.ReadAsStringAsync();

            if (string.IsNullOrEmpty(content))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Content cannot be empty" });
                return badRequest;
            }

            var baseDirectory = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDirectory, InstructionsPath);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create backup of current file
            if (File.Exists(fullPath))
            {
                var backupPath = $"{fullPath}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                File.Copy(fullPath, backupPath);
                _logger.LogInformation("Created backup: {BackupPath}", backupPath);
            }

            // Write new content
            await File.WriteAllTextAsync(fullPath, content);

            _logger.LogInformation("Instructions file updated successfully");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Instructions saved successfully",
                timestamp = DateTime.UtcNow
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving instructions file");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to save instructions",
                details = ex.Message
            });
            return errorResponse;
        }
    }
}
