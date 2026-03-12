using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using DiscoveryAgent.Configuration;
using DiscoveryAgent.Models;
using DiscoveryAgent.Services;
using DiscoveryAgent.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DiscoveryAgent.Handlers;

/// <summary>
/// Handles the full lifecycle of a conversation turn:
/// 1. Get or create thread
/// 2. Add user message
/// 3. Run agent
/// 4. Process tool calls (knowledge extraction, profile updates, etc.)
/// 5. Return response
/// </summary>
public class ConversationHandler
{
    private readonly PersistentAgentsClient _agentsClient;
    private readonly AgentManager _agentManager;
    private readonly KnowledgeStore _knowledgeStore;
    private readonly UserProfileService _userProfiles;
    private readonly DataExtractionTool _extractionTool;
    private readonly DiscoveryBotSettings _settings;
    private readonly ILogger<ConversationHandler> _logger;

    public ConversationHandler(
        PersistentAgentsClient agentsClient,
        AgentManager agentManager,
        KnowledgeStore knowledgeStore,
        UserProfileService userProfiles,
        DataExtractionTool extractionTool,
        DiscoveryBotSettings settings,
        ILogger<ConversationHandler> logger)
    {
        _agentsClient = agentsClient;
        _agentManager = agentManager;
        _knowledgeStore = knowledgeStore;
        _userProfiles = userProfiles;
        _extractionTool = extractionTool;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ConversationResponse> HandleAsync(ConversationRequest request)
    {
        var agentsClient = _agentsClient;

        // -----------------------------------------------------------------
        // Step 1: Get or create thread
        // -----------------------------------------------------------------
        string threadId;
        if (!string.IsNullOrEmpty(request.ThreadId))
        {
            threadId = request.ThreadId;
        }
        else
        {
            var thread = await agentsClient.Threads.CreateThreadAsync();
            threadId = thread.Value.Id;

            // If a context is specified, inject it into the thread
            if (!string.IsNullOrEmpty(request.ContextId))
            {
                var context = await _agentManager.GetContextAsync(request.ContextId);
                if (context is not null)
                {
                    var contextMessage = BuildContextMessage(context);
                    await agentsClient.Messages.CreateMessageAsync(
                        threadId,
                        MessageRole.User,
                        contextMessage
                    );
                }
            }
        }

        // -----------------------------------------------------------------
        // Step 2: Add user message
        // -----------------------------------------------------------------
        await agentsClient.Messages.CreateMessageAsync(
            threadId,
            MessageRole.User,
            request.Message
        );

        // -----------------------------------------------------------------
        // Step 3: Run agent
        // -----------------------------------------------------------------
        var run = await agentsClient.Runs.CreateRunAsync(
            threadId,
            _agentManager.AgentId
        );

        var extractedKnowledgeIds = new List<string>();

        // -----------------------------------------------------------------
        // Step 4: Poll for completion, handle tool calls
        // -----------------------------------------------------------------
        while (true)
        {
            await Task.Delay(500);

            run = await agentsClient.Runs.GetRunAsync(threadId, run.Value.Id);
            var status = run.Value.Status;

            if (status == RunStatus.Completed)
                break;

            if (status == RunStatus.Failed || status == RunStatus.Cancelled ||
                status == RunStatus.Expired)
            {
                _logger.LogError("Run failed with status: {Status}", status);
                throw new InvalidOperationException($"Agent run failed: {status}");
            }

            if (status == RunStatus.RequiresAction)
            {
                var toolOutputs = await ProcessToolCallsAsync(
                    run.Value, request.UserId, threadId, request.ContextId);

                extractedKnowledgeIds.AddRange(
                    toolOutputs.Where(t => t.Tag == "knowledge")
                               .Select(t => t.Id));

                await agentsClient.Runs.SubmitToolOutputsToRunAsync(
                    run.Value,
                    toolOutputs.Select(t => t.Output).ToList(),
                    cancellationToken: default
                );
            }
        }

        // -----------------------------------------------------------------
        // Step 5: Get the assistant's response
        // -----------------------------------------------------------------
        var messagesPageable = agentsClient.Messages.GetMessagesAsync(threadId);
        var messagesList = new List<PersistentThreadMessage>();
        await foreach (var msg in messagesPageable)
        {
            messagesList.Add(msg);
        }

        _logger.LogInformation("Retrieved {Count} messages from thread {ThreadId}", messagesList.Count, threadId);
        foreach (var msg in messagesList.OrderByDescending(m => m.CreatedAt).Take(5))
        {
            _logger.LogInformation("Message: Role={Role}, ContentItemCount={Count}, CreatedAt={CreatedAt}",
                msg.Role, msg.ContentItems.Count, msg.CreatedAt);
        }

        var lastAssistantMessage = messagesList
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault(m => m.Role == MessageRole.Agent);

        if (lastAssistantMessage == null)
        {
            _logger.LogWarning("No agent-role message found in thread {ThreadId}. Run status was {Status}",
                threadId, run.Value.Status);
        }

        var responseText = lastAssistantMessage?.ContentItems
            .OfType<MessageTextContent>()
            .Select(c => c.Text)
            .FirstOrDefault() ?? "I'm sorry, I wasn't able to generate a response.";

        return new ConversationResponse(
            ThreadId: threadId,
            Response: responseText,
            AgentId: _agentManager.AgentId,
            ExtractedKnowledgeIds: extractedKnowledgeIds.Any() ? extractedKnowledgeIds : null
        );
    }

    /// <summary>
    /// Processes tool calls from the agent run. Each tool call maps to a
    /// custom C# function that performs the actual work.
    /// </summary>
    private async Task<List<ToolCallResult>> ProcessToolCallsAsync(
        ThreadRun run, string userId, string threadId, string? contextId)
    {
        var results = new List<ToolCallResult>();

        if (run.RequiredAction is not SubmitToolOutputsAction submitToolOutputsAction)
            return results;

        foreach (var toolCall in submitToolOutputsAction.ToolCalls)
        {
            if (toolCall is not RequiredFunctionToolCall functionToolCall)
                continue;

            var functionName = functionToolCall.Name;
            var arguments = functionToolCall.Arguments;

            _logger.LogInformation("Processing tool call: {FunctionName}", functionName);

            try
            {
                var (output, tag, id) = functionName switch
                {
                    "extract_knowledge" => await HandleKnowledgeExtraction(
                        arguments, userId, threadId, contextId ?? "default"),

                    "store_user_profile" => await HandleProfileUpdate(
                        arguments, userId),

                    "complete_questionnaire_section" => await HandleSectionComplete(
                        arguments, threadId),

                    _ => (JsonSerializer.Serialize(new { error = $"Unknown function: {functionName}" }),
                          "unknown", "")
                };

                results.Add(new ToolCallResult(
                    new ToolOutput(toolCall.Id, output),
                    tag, id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool call failed: {Function}", functionName);
                results.Add(new ToolCallResult(
                    new ToolOutput(toolCall.Id, JsonSerializer.Serialize(new { error = ex.Message })),
                    "error", ""));
            }
        }

        return results;
    }

    private async Task<(string output, string tag, string id)> HandleKnowledgeExtraction(
        string arguments, string userId, string threadId, string contextId)
    {
        var args = JsonSerializer.Deserialize<KnowledgeExtractionArgs>(arguments);
        if (args?.Items is null) return ("{}", "knowledge", "");

        var ids = new List<string>();
        foreach (var item in args.Items)
        {
            var ki = new KnowledgeItem
            {
                Content = item.Content,
                Category = Enum.Parse<KnowledgeCategory>(item.Category, true),
                Confidence = item.Confidence,
                SourceUserId = userId,
                SourceThreadId = threadId,
                RelatedContextId = contextId,
                Tags = item.Tags ?? [],
            };

            var storedId = await _knowledgeStore.StoreAsync(ki);
            ids.Add(storedId);
        }

        var result = JsonSerializer.Serialize(new
        {
            stored = ids.Count,
            ids
        });
        return (result, "knowledge", string.Join(",", ids));
    }

    private async Task<(string output, string tag, string id)> HandleProfileUpdate(
        string arguments, string userId)
    {
        var args = JsonSerializer.Deserialize<ProfileUpdateArgs>(arguments);
        if (args is null) return ("{}", "profile", "");

        var profile = new UserProfile
        {
            UserId = userId,
            RoleName = args.RoleName,
            Tone = Enum.TryParse<CommunicationTone>(args.Tone, true, out var t) ? t : CommunicationTone.Conversational,
            DetailLevel = Enum.TryParse<DetailLevel>(args.DetailLevel, true, out var d) ? d : DetailLevel.Detailed,
            PriorityTopics = args.PriorityTopics ?? [],
            QuestionComplexity = Enum.TryParse<QuestionComplexity>(args.QuestionComplexity?.Replace("-", ""), true, out var q) ? q : QuestionComplexity.Detailed,
        };

        await _userProfiles.UpsertAsync(profile);

        return (JsonSerializer.Serialize(new { status = "profile_updated", role = args.RoleName }),
                "profile", userId);
    }

    private Task<(string output, string tag, string id)> HandleSectionComplete(
        string arguments, string threadId)
    {
        // Acknowledged — session tracking is persisted via the knowledge store
        var result = JsonSerializer.Serialize(new { status = "section_completed" });
        return Task.FromResult((result, "section", ""));
    }

    private static string BuildContextMessage(DiscoveryContext context) =>
        $"""
        [SYSTEM CONTEXT — Discovery Session Configuration]
        Project: {context.Name}
        Description: {context.Description}
        Mode: {context.DiscoveryMode}
        Focus Areas: {string.Join(", ", context.DiscoveryAreas)}
        Key Questions: {string.Join("; ", context.KeyQuestions)}
        Please begin the discovery session following your instructions.
        """;

    // Deserialization helpers
    private record KnowledgeExtractionArgs(List<KnowledgeItemArg>? Items);
    private record KnowledgeItemArg(string Content, string Category, double Confidence, List<string>? Tags);
    private record ProfileUpdateArgs(string RoleName, string? Tone, string? DetailLevel, List<string>? PriorityTopics, string? QuestionComplexity);
    private record ToolCallResult(ToolOutput Output, string Tag, string Id);
}
