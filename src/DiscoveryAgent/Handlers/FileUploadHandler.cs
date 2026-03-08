using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DiscoveryAgent.Configuration;
using DiscoveryAgent.Models;
using DiscoveryAgent.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Handlers;

/// <summary>
/// Handles file uploads from both the web chat and Teams channels.
/// Stores files in Azure Blob Storage, detects questionnaires,
/// and triggers appropriate processing pipelines.
/// </summary>
public class FileUploadHandler
{
    private readonly BlobServiceClient _blobClient;
    private readonly QuestionnaireProcessor _questionnaireProcessor;
    private readonly Database _cosmosDb;
    private readonly DiscoveryBotSettings _settings;
    private readonly ILogger<FileUploadHandler> _logger;

    private static readonly HashSet<string> SupportedTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv",
        "image/png",
        "image/jpeg"
    ];

    public FileUploadHandler(
        BlobServiceClient blobClient,
        QuestionnaireProcessor questionnaireProcessor,
        Database cosmosDb,
        DiscoveryBotSettings settings,
        ILogger<FileUploadHandler> logger)
    {
        _blobClient = blobClient;
        _questionnaireProcessor = questionnaireProcessor;
        _cosmosDb = cosmosDb;
        _settings = settings;
        _logger = logger;
    }

    public async Task<UploadResult> HandleUploadAsync(
        Stream fileStream, string fileName, string contentType, long fileSize,
        string threadId, string userId)
    {
        // Validate
        if (!SupportedTypes.Contains(contentType))
        {
            return new UploadResult(
                Success: false,
                Error: $"Unsupported file type: {contentType}");
        }

        _logger.LogInformation("Processing upload: {FileName} ({ContentType}, {Size} bytes)",
            fileName, contentType, fileSize);

        // -----------------------------------------------------------------
        // Store in Blob
        // -----------------------------------------------------------------
        var containerClient = _blobClient.GetBlobContainerClient(_settings.UploadContainer);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = $"{threadId}/{Guid.NewGuid()}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(fileStream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = new Dictionary<string, string>
            {
                ["threadId"] = threadId,
                ["userId"] = userId,
                ["originalName"] = fileName,
                ["uploadedAt"] = DateTime.UtcNow.ToString("O")
            }
        });

        var blobUrl = blobClient.Uri.ToString();

        // -----------------------------------------------------------------
        // Detect if questionnaire
        // -----------------------------------------------------------------
        var isQuestionnaire = await _questionnaireProcessor.DetectQuestionnaireAsync(
            blobUrl, contentType);

        string? questionnaireId = null;
        if (isQuestionnaire)
        {
            var parsed = await _questionnaireProcessor.ParseAsync(blobUrl, contentType);
            questionnaireId = parsed.QuestionnaireId;

            // Store parsed questionnaire in Cosmos
            var qContainer = _cosmosDb.GetContainer("questionnaires");
            await qContainer.UpsertItemAsync(parsed, new PartitionKey(parsed.QuestionnaireId));

            _logger.LogInformation("Questionnaire detected and parsed: {Id} with {Sections} sections, {Questions} questions",
                questionnaireId, parsed.Sections.Count, parsed.Questions.Count);
        }

        // -----------------------------------------------------------------
        // Store document metadata in Cosmos
        // -----------------------------------------------------------------
        var docMetadata = new UploadedDocument
        {
            OriginalName = fileName,
            BlobUrl = blobUrl,
            ContentType = contentType,
            SizeBytes = fileSize,
            UploadedByUserId = userId,
            ThreadId = threadId,
            IsQuestionnaire = isQuestionnaire,
            QuestionnaireId = questionnaireId,
        };

        var sessionsContainer = _cosmosDb.GetContainer("discovery-sessions");
        await sessionsContainer.UpsertItemAsync(docMetadata, new PartitionKey(threadId));

        return new UploadResult(
            Success: true,
            DocumentId: docMetadata.Id,
            BlobUrl: blobUrl,
            IsQuestionnaire: isQuestionnaire,
            QuestionnaireId: questionnaireId,
            FileName: fileName
        );
    }
}

public record UploadResult(
    bool Success,
    string? DocumentId = null,
    string? BlobUrl = null,
    bool IsQuestionnaire = false,
    string? QuestionnaireId = null,
    string? FileName = null,
    string? Error = null
);
