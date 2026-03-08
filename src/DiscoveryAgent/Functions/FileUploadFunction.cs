using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Handlers;
using System.Net;

namespace DiscoveryAgent.Functions;

public class FileUploadFunction
{
    private readonly FileUploadHandler _handler;
    private readonly ILogger<FileUploadFunction> _logger;

    public FileUploadFunction(FileUploadHandler handler, ILogger<FileUploadFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("FileUpload")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "upload")] HttpRequestData req)
    {
        // TODO: Implement multipart form data parsing for Azure Functions
        // Azure Functions Worker doesn't have built-in multipart support yet
        // Consider using Microsoft.AspNetCore.WebUtilities or a custom parser

        _logger.LogWarning("File upload endpoint not yet implemented for Functions runtime");

        var response = req.CreateResponse(HttpStatusCode.NotImplemented);
        await response.WriteAsJsonAsync(new
        {
            error = "File upload not yet implemented for Azure Functions runtime",
            message = "Multipart form data parsing needs to be added"
        });
        return response;
    }
}
