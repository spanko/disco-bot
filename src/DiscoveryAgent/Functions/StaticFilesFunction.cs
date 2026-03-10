using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace DiscoveryAgent.Functions;

/// <summary>
/// Serves static web files (HTML, CSS, JS) from the web directory.
/// </summary>
public class StaticFilesFunction
{
    private readonly ILogger<StaticFilesFunction> _logger;
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        { ".html", "text/html; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "application/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" }
    };

    public StaticFilesFunction(ILogger<StaticFilesFunction> logger)
    {
        _logger = logger;
    }

    [Function("ServeRootFile")]
    public async Task<HttpResponseData> ServeRootFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{fileName}")] HttpRequestData req,
        string fileName)
    {
        try
        {
            var filePath = fileName;

            // Only allow specific file extensions
            var allowedExtensions = new[] { ".html", ".css", ".js", ".json", ".png", ".jpg", ".jpeg", ".svg", ".ico" };
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("File type not allowed");
                return badRequest;
            }

            // Prevent directory traversal attacks
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Invalid file name");
                return badRequest;
            }

            // Serve from web directory
            filePath = $"web/{fileName}";

            // Get the base directory (where the function app is running)
            var baseDirectory = AppContext.BaseDirectory;
            var fullPath = Path.Combine(baseDirectory, filePath);

            _logger.LogInformation("Attempting to serve file: {FilePath} from {FullPath}", filePath, fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File not found: {FullPath}", fullPath);
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync("File not found");
                return notFound;
            }

            var contentType = ContentTypes.GetValueOrDefault(extension, "application/octet-stream");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", contentType);

            // Add caching headers for static assets (except HTML which might change)
            if (extension != ".html")
            {
                response.Headers.Add("Cache-Control", "public, max-age=3600");
            }

            var fileContent = await File.ReadAllBytesAsync(fullPath);
            await response.Body.WriteAsync(fileContent);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving static file: {FileName}", fileName);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("Error serving file");
            return errorResponse;
        }
    }
}
