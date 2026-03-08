using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using DiscoveryAgent.Configuration;
using DiscoveryAgent.Models;
using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Services;

/// <summary>
/// Parses uploaded documents to detect and extract structured questionnaires.
/// Uses the Foundry project's model to intelligently parse document structure.
/// </summary>
public class QuestionnaireProcessor
{
    private readonly AIProjectClient _projectClient;
    private readonly DiscoveryBotSettings _settings;
    private readonly ILogger<QuestionnaireProcessor> _logger;

    // Indicators that a document is a questionnaire
    private static readonly string[] QuestionnaireIndicators =
    [
        "please answer", "select", "rate", "describe", "explain",
        "questionnaire", "survey", "assessment", "evaluation",
        "strongly agree", "strongly disagree", "not applicable",
        "yes/no", "check all", "choose one"
    ];

    public QuestionnaireProcessor(
        AIProjectClient projectClient,
        DiscoveryBotSettings settings,
        ILogger<QuestionnaireProcessor> logger)
    {
        _projectClient = projectClient;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Detects whether an uploaded document is a questionnaire.
    /// </summary>
    public async Task<bool> DetectQuestionnaireAsync(string blobUrl, string contentType)
    {
        // Only process document types (not images)
        if (contentType.StartsWith("image/")) return false;

        // Use the LLM to analyze the document structure
        try
        {
            var agents = _projectClient.GetPersistentAgentsClient();
            var thread = await agents.Threads.CreateThreadAsync();

            // Upload the file reference and ask for analysis
            await agents.Messages.CreateMessageAsync(
                thread.Value.Id,
                MessageRole.User,
                @"Analyze this document and determine if it is a questionnaire, survey,
                or assessment form. Look for: numbered questions, multiple choice options,
                rating scales, yes/no fields, open-ended prompts, section headers.

                Respond with ONLY a JSON object: {""isQuestionnaire"": true/false, ""confidence"": 0.0-1.0, ""reason"": ""...""}

                Document URL: " + blobUrl
            );

            // For detection, use the cheaper fallback model
            var run = await agents.Runs.CreateRunAsync(thread.Value.Id, _settings.FallbackModelDeployment);

            // Poll for completion
            while (true)
            {
                await Task.Delay(300);
                run = await agents.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
                if (run.Value.Status == RunStatus.Completed) break;
                if (run.Value.Status == RunStatus.Failed || run.Value.Status == RunStatus.Cancelled) return false;
            }

            var messagesPageable = agents.Messages.GetMessagesAsync(thread.Value.Id);
            var messagesList = new List<PersistentThreadMessage>();
            await foreach (var msg in messagesPageable)
            {
                messagesList.Add(msg);
            }
            var response = messagesList
                .Where(m => m.Role == MessageRole.Agent)
                .SelectMany(m => m.ContentItems)
                .OfType<MessageTextContent>()
                .Select(c => c.Text)
                .FirstOrDefault();

            if (response is not null && response.Contains("\"isQuestionnaire\": true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Questionnaire detection failed, falling back to heuristic");
        }

        return false;
    }

    /// <summary>
    /// Parses a questionnaire document into structured sections and questions.
    /// </summary>
    public async Task<ParsedQuestionnaire> ParseAsync(string blobUrl, string contentType)
    {
        _logger.LogInformation("Parsing questionnaire from {Url}", blobUrl);

        var agents = _projectClient.GetPersistentAgentsClient();
        var thread = await agents.Threads.CreateThreadAsync();

        await agents.Messages.CreateMessageAsync(
            thread.Value.Id,
            MessageRole.User,
            @"Parse this questionnaire document into a structured format.

            Extract:
            1. Title and description
            2. Sections (with hierarchy - parent/child)
            3. Questions within each section, including:
               - Question text
               - Question type (open, multiple_choice, scale, yes_no)
               - Options (if multiple choice)
               - Conditional logic (if answer X, skip to question Y)
               - Whether required
               - Order within section

            Respond with ONLY valid JSON matching this schema:
            {
              ""title"": ""string"",
              ""description"": ""string"",
              ""sections"": [
                {
                  ""sectionId"": ""s1"",
                  ""title"": ""string"",
                  ""description"": ""string"",
                  ""parentSectionId"": null,
                  ""order"": 1
                }
              ],
              ""questions"": [
                {
                  ""questionId"": ""q1"",
                  ""sectionId"": ""s1"",
                  ""text"": ""string"",
                  ""questionType"": ""open|multiple_choice|scale|yes_no"",
                  ""options"": [],
                  ""followUpLogic"": {},
                  ""required"": true,
                  ""order"": 1
                }
              ]
            }

            Document URL: " + blobUrl
        );

        var run = await agents.Runs.CreateRunAsync(thread.Value.Id, _settings.PrimaryModelDeployment);

        while (true)
        {
            await Task.Delay(500);
            run = await agents.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
            if (run.Value.Status == RunStatus.Completed) break;
            if (run.Value.Status == RunStatus.Failed || run.Value.Status == RunStatus.Cancelled)
                throw new InvalidOperationException("Questionnaire parsing failed");
        }

        var messagesPageable = agents.Messages.GetMessagesAsync(thread.Value.Id);
        var messagesList = new List<PersistentThreadMessage>();
        await foreach (var msg in messagesPageable)
        {
            messagesList.Add(msg);
        }
        var responseText = messagesList
            .Where(m => m.Role == MessageRole.Agent)
            .SelectMany(m => m.ContentItems)
            .OfType<MessageTextContent>()
            .Select(c => c.Text)
            .FirstOrDefault() ?? "{}";

        // Parse the LLM's structured output
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<ParsedQuestionnaire>(
                responseText,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            return parsed ?? new ParsedQuestionnaire { QuestionnaireId = Guid.NewGuid().ToString() };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM questionnaire output");
            return new ParsedQuestionnaire
            {
                QuestionnaireId = Guid.NewGuid().ToString(),
                Title = "Unparsed Questionnaire",
                Description = "The document was detected as a questionnaire but could not be fully parsed."
            };
        }
    }
}
