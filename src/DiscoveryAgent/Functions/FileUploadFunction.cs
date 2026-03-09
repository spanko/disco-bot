using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
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
        try
        {
            // Extract boundary from Content-Type header
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault();
            if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data"))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Request must be multipart/form-data" });
                return badRequest;
            }

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid multipart boundary" });
                return badRequest;
            }

            var reader = new MultipartReader(boundary, req.Body);

            string? userId = null;
            string? threadId = null;
            string? fileName = null;
            string? fileContentType = null;
            Stream? fileStream = null;
            long fileSize = 0;

            // Read multipart sections
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                var hasContentDisposition = ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var contentDisposition);

                if (hasContentDisposition && contentDisposition != null)
                {
                    var name = contentDisposition.Name.Value?.Trim('"');

                    // Handle form fields
                    if (contentDisposition.DispositionType.Equals("form-data") &&
                        !contentDisposition.FileName.HasValue)
                    {
                        var value = await new StreamReader(section.Body).ReadToEndAsync();
                        if (name == "userId") userId = value;
                        else if (name == "threadId") threadId = value;
                    }
                    // Handle file
                    else if (contentDisposition.FileName.HasValue)
                    {
                        fileName = contentDisposition.FileName.Value?.Trim('"');
                        fileContentType = section.ContentType ?? "application/octet-stream";

                        // Copy to memory stream for processing
                        var memoryStream = new MemoryStream();
                        await section.Body.CopyToAsync(memoryStream);
                        fileSize = memoryStream.Length;
                        memoryStream.Position = 0;
                        fileStream = memoryStream;
                    }
                }
            }

            // Validate required fields
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(threadId) ||
                fileStream == null || string.IsNullOrEmpty(fileName))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    error = "Missing required fields",
                    details = "userId, threadId, and file are required"
                });
                return badRequest;
            }

            _logger.LogInformation("Processing file upload: {FileName} for user {UserId} in thread {ThreadId}",
                fileName, userId, threadId);

            // Process upload
            var result = await _handler.HandleUploadAsync(
                fileStream, fileName, fileContentType!, fileSize, threadId, userId);

            fileStream.Dispose();

            if (!result.Success)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { error = result.Error });
                return errorResponse;
            }

            // Success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                documentId = result.DocumentId,
                fileName = result.FileName,
                blobUrl = result.BlobUrl,
                isQuestionnaire = result.IsQuestionnaire,
                questionnaireId = result.QuestionnaireId
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file upload");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to process upload", details = ex.Message });
            return errorResponse;
        }
    }

    private static string? GetBoundary(string contentType)
    {
        var elements = contentType.Split(' ');
        var element = elements.FirstOrDefault(e => e.StartsWith("boundary="));
        if (element == null) return null;

        var boundary = element.Substring("boundary=".Length).Trim('"');
        return boundary;
    }
}
