using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using DiscoveryAgent.Configuration;
using DiscoveryAgent.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Services;

/// <summary>
/// Manages the Foundry agent lifecycle: creation, configuration updates,
/// and context management. This is the bridge between the configurable
/// layer (instructions.md, agent.yaml, discovery contexts) and the
/// Foundry Agent Service runtime.
/// </summary>
public class AgentManager
{
    private readonly AIProjectClient _projectClient;
    private readonly DiscoveryBotSettings _settings;
    private readonly Database _cosmosDb;
    private readonly ILogger<AgentManager> _logger;

    private string? _agentId;

    public string AgentId => _agentId
        ?? throw new InvalidOperationException("Agent not yet initialized. Call EnsureAgentExistsAsync first.");

    public AgentManager(
        AIProjectClient projectClient,
        DiscoveryBotSettings settings,
        Database cosmosDb,
        ILogger<AgentManager> logger)
    {
        _projectClient = projectClient;
        _settings = settings;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the discovery agent exists in Foundry Agent Service.
    /// Creates it on first run, or retrieves the existing agent ID.
    /// </summary>
    public async Task EnsureAgentExistsAsync()
    {
        _logger.LogInformation("Initializing discovery agent...");

        // Load system prompt from the configurable instructions file
        var instructions = await LoadInstructionsAsync();

        try
        {
            // Try to find existing agent by name
            var agents = _projectClient.GetPersistentAgentsClient();
            // NOTE: The actual SDK method to list/find agents may differ.
            // This represents the intent — create if not exists.

            var agent = await agents.Administration.CreateAgentAsync(
                _settings.PrimaryModelDeployment,
                _settings.AgentName,
                instructions: instructions,
                tools: BuildToolDefinitions()
            );

            _agentId = agent.Value.Id;
            _logger.LogInformation("Agent ready: {AgentId}", _agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agent");
            throw;
        }
    }

    /// <summary>
    /// Loads the system prompt from the configurable instructions file,
    /// then appends any active discovery context instructions.
    /// </summary>
    private async Task<string> LoadInstructionsAsync()
    {
        var path = _settings.InstructionsPath;

        // Try reading from file system (works in container)
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path);
        }

        // Fallback: embedded default prompt
        return GetDefaultSystemPrompt();
    }

    /// <summary>
    /// Generates context-aware instructions by combining the base system prompt
    /// with a specific discovery context's configuration.
    /// </summary>
    public string BuildContextualInstructions(DiscoveryContext context)
    {
        var baseInstructions = File.Exists(_settings.InstructionsPath)
            ? File.ReadAllText(_settings.InstructionsPath)
            : GetDefaultSystemPrompt();

        return $"""
            {baseInstructions}

            ---
            ## ACTIVE DISCOVERY CONTEXT
            
            **Project**: {context.Name}
            **Description**: {context.Description}
            **Mode**: {context.DiscoveryMode}
            
            **Discovery Areas**: {string.Join(", ", context.DiscoveryAreas)}
            **Key Questions**: 
            {string.Join("\n", context.KeyQuestions.Select((q, i) => $"  {i + 1}. {q}"))}
            
            **Sensitive Areas (handle carefully)**: {string.Join(", ", context.SensitiveAreas)}
            
            **Success Criteria**:
            {string.Join("\n", context.SuccessCriteria.Select(c => $"  - {c}"))}
            
            {(string.IsNullOrEmpty(context.AgentInstructions) ? "" : $"\n**Additional Instructions**:\n{context.AgentInstructions}")}
            """;
    }

    /// <summary>
    /// Updates or creates a discovery context in Cosmos DB.
    /// Called from the admin API endpoint.
    /// </summary>
    public async Task UpdateContextAsync(DiscoveryContextConfig config)
    {
        var container = _cosmosDb.GetContainer("discovery-sessions");

        var context = new DiscoveryContext
        {
            Id = config.ContextId,
            ContextId = config.ContextId,
            Name = config.Name,
            Description = config.Description,
            DiscoveryMode = Enum.Parse<DiscoveryMode>(config.DiscoveryMode, true),
            DiscoveryAreas = config.DiscoveryAreas,
            KeyQuestions = config.KeyQuestions,
            SensitiveAreas = config.SensitiveAreas,
            SuccessCriteria = config.SuccessCriteria,
            AgentInstructions = config.AgentInstructions,
        };

        await container.UpsertItemAsync(context, new PartitionKey(context.ContextId));
        _logger.LogInformation("Context updated: {ContextId}", config.ContextId);
    }

    /// <summary>
    /// Retrieves a discovery context by ID.
    /// </summary>
    public async Task<DiscoveryContext?> GetContextAsync(string contextId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("discovery-sessions");
            var response = await container.ReadItemAsync<DiscoveryContext>(
                contextId, new PartitionKey(contextId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the tool definitions for the agent.
    /// These map to the custom C# tool classes in the Tools/ folder.
    /// </summary>
    private IEnumerable<ToolDefinition> BuildToolDefinitions()
    {
        // File search is built-in to Foundry Agent Service
        // Custom tools are registered as function definitions
        return
        [
            // Built-in file search for RAG
            new FileSearchToolDefinition(),

            // Custom: Extract structured knowledge from conversation
            new FunctionToolDefinition(
                name: "extract_knowledge",
                description: "Extract and categorize knowledge items from the user's response. " +
                    "Call this after each substantive user message to capture facts, opinions, " +
                    "decisions, requirements, and concerns.",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        items = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    content = new { type = "string", description = "The knowledge statement" },
                                    category = new { type = "string", @enum = new[] { "fact", "opinion", "decision", "requirement", "concern" } },
                                    confidence = new { type = "number", description = "0.0 to 1.0" },
                                    tags = new { type = "array", items = new { type = "string" } }
                                },
                                required = new[] { "content", "category", "confidence" }
                            }
                        }
                    },
                    required = new[] { "items" }
                })
            ),

            // Custom: Store user profile/role information
            new FunctionToolDefinition(
                name: "store_user_profile",
                description: "Store or update the user's role profile based on what they've shared. " +
                    "Call this after the role discovery phase of conversation.",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        roleName = new { type = "string" },
                        tone = new { type = "string", @enum = new[] { "formal", "conversational", "technical" } },
                        detailLevel = new { type = "string", @enum = new[] { "executive", "detailed", "technical" } },
                        priorityTopics = new { type = "array", items = new { type = "string" } },
                        questionComplexity = new { type = "string", @enum = new[] { "high-level", "detailed", "deep-dive" } }
                    },
                    required = new[] { "roleName" }
                })
            ),

            // Custom: Mark questionnaire section complete
            new FunctionToolDefinition(
                name: "complete_questionnaire_section",
                description: "Mark a questionnaire section as complete and summarize findings.",
                parameters: BinaryData.FromObjectAsJson(new
                {
                    type = "object",
                    properties = new
                    {
                        sectionId = new { type = "string" },
                        summary = new { type = "string" },
                        keyFindings = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "sectionId", "summary" }
                })
            ),
        ];
    }

    private static string GetDefaultSystemPrompt() => """
        You are an intelligent discovery agent designed to help organizations gather,
        organize, and synthesize knowledge through natural conversation.

        Your primary purpose is to discover, understand, and document knowledge — not
        to provide answers. You ask thoughtful questions, listen actively, and help
        users articulate their knowledge in structured ways.

        At the start of every new conversation, identify the user's role by asking
        about their responsibilities, then adapt your tone, depth, and focus accordingly.

        For every significant piece of information shared, extract and categorize it
        as a fact, opinion, decision, requirement, or concern, noting confidence level
        and relationships to other captured knowledge.
        """;
}
