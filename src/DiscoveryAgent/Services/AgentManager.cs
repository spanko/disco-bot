using Azure;
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
///
/// Key design decisions:
/// - Uses a SemaphoreSlim to ensure only one initialization runs at a time
/// - Persists agent ID to Cosmos so restarts reuse the same agent
/// - Falls back to creating a new agent only if the persisted one is gone
/// - Exposes a TaskCompletionSource so callers can await readiness
/// </summary>
public class AgentManager
{
    private readonly PersistentAgentsClient _agentsClient;
    private readonly DiscoveryBotSettings _settings;
    private readonly Database _cosmosDb;
    private readonly ILogger<AgentManager> _logger;

    private string? _agentId;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly TaskCompletionSource _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Cosmos container/key for persisting the agent ID across restarts
    private const string AgentMetaContainer = "agent-meta";
    private const string AgentMetaId = "current-agent";

    /// <summary>
    /// Returns the current agent ID, or throws if not yet initialized.
    /// Prefer <see cref="WaitForReadyAsync"/> in request handlers.
    /// </summary>
    public string AgentId => _agentId
        ?? throw new InvalidOperationException("Agent not yet initialized. Call EnsureAgentExistsAsync first.");

    /// <summary>
    /// Await this in request handlers to block until the agent is ready.
    /// Throws if initialization failed.
    /// </summary>
    public Task WaitForReadyAsync(CancellationToken ct = default)
    {
        return _readySignal.Task.WaitAsync(ct);
    }

    public AgentManager(
        PersistentAgentsClient agentsClient,
        DiscoveryBotSettings settings,
        Database cosmosDb,
        ILogger<AgentManager> logger)
    {
        _agentsClient = agentsClient;
        _settings = settings;
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the discovery agent exists in Foundry Agent Service.
    /// 1. Checks Cosmos for a previously persisted agent ID
    /// 2. If found, validates the agent still exists in Foundry
    /// 3. If not found or stale, creates a new agent and persists the ID
    ///
    /// This is safe to call concurrently — the SemaphoreSlim serializes.
    /// </summary>
    public async Task EnsureAgentExistsAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            // Already initialized in this process?
            if (_agentId is not null)
            {
                _logger.LogInformation("Agent already initialized: {AgentId}", _agentId);
                return;
            }

            _logger.LogInformation("Initializing discovery agent...");
            _logger.LogInformation("  Endpoint base: {Endpoint}", _settings.FoundryEndpoint);
            _logger.LogInformation("  Project: {Project}", _settings.FoundryProjectName);
            _logger.LogInformation("  Model deployment: {Model}", _settings.PrimaryModelDeployment);

            // Step 1: Try to recover a previously persisted agent ID from Cosmos
            var persistedId = await LoadPersistedAgentIdAsync();

            if (persistedId is not null)
            {
                _logger.LogInformation("Found persisted agent ID: {AgentId}. Validating...", persistedId);

                if (await TryValidateAgentAsync(persistedId))
                {
                    _agentId = persistedId;
                    _logger.LogInformation("Persisted agent is valid and ready: {AgentId}", _agentId);
                    _readySignal.TrySetResult();
                    return;
                }

                _logger.LogWarning("Persisted agent {AgentId} no longer exists in Foundry. Will create a new one.", persistedId);
            }
            else
            {
                _logger.LogInformation("No persisted agent ID found. Creating new agent...");
            }

            // Step 2: Create a fresh agent
            await CreateNewAgentAsync();

            _readySignal.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL: Failed to initialize agent. The bot will not function.");
            _readySignal.TrySetException(ex);
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Creates a new agent in Foundry and persists its ID to Cosmos.
    /// </summary>
    private async Task CreateNewAgentAsync()
    {
        var instructions = await LoadInstructionsAsync();
        var tools = BuildToolDefinitions().ToList();

        _logger.LogInformation("Creating agent with {ToolCount} tools, {InstructionLength} char instructions",
            tools.Count, instructions.Length);

        try
        {
            var agent = await _agentsClient.Administration.CreateAgentAsync(
                _settings.PrimaryModelDeployment,
                _settings.AgentName,
                instructions: instructions,
                tools: tools
            );

            _agentId = agent.Value.Id;
            _logger.LogInformation("Agent created successfully: {AgentId} (Name={Name}, Model={Model})",
                _agentId, agent.Value.Name, agent.Value.Model);

            // Persist the agent ID so we can reuse it after restarts
            await PersistAgentIdAsync(_agentId);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Foundry API error creating agent: Status={Status}, Code={Code}, Message={Message}",
                ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Validates that an agent ID still exists in Foundry by calling GetAgent.
    /// Returns false if the agent is gone (404) or the call fails.
    /// </summary>
    private async Task<bool> TryValidateAgentAsync(string agentId)
    {
        try
        {
            var agent = await _agentsClient.Administration.GetAgentAsync(agentId);
            _logger.LogInformation("Validated agent: {AgentId} (Name={Name}, Model={Model})",
                agent.Value.Id, agent.Value.Name, agent.Value.Model);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Agent {AgentId} not found (404)", agentId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate agent {AgentId}. Will recreate.", agentId);
            return false;
        }
    }

    /// <summary>
    /// Loads a previously persisted agent ID from Cosmos DB.
    /// Returns null if no record exists or the container isn't set up yet.
    /// </summary>
    private async Task<string?> LoadPersistedAgentIdAsync()
    {
        try
        {
            var container = _cosmosDb.GetContainer(AgentMetaContainer);
            var response = await container.ReadItemAsync<AgentMetaRecord>(
                AgentMetaId, new PartitionKey(AgentMetaId));
            return response.Resource.AgentId;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read persisted agent ID from Cosmos (container may not exist yet)");
            return null;
        }
    }

    /// <summary>
    /// Persists the agent ID to Cosmos DB so it survives app restarts.
    /// </summary>
    private async Task PersistAgentIdAsync(string agentId)
    {
        try
        {
            var container = _cosmosDb.GetContainer(AgentMetaContainer);
            var record = new AgentMetaRecord
            {
                Id = AgentMetaId,
                AgentId = agentId,
                CreatedAt = DateTimeOffset.UtcNow,
                ModelDeployment = _settings.PrimaryModelDeployment,
                AgentName = _settings.AgentName,
            };
            await container.UpsertItemAsync(record, new PartitionKey(AgentMetaId));
            _logger.LogInformation("Persisted agent ID {AgentId} to Cosmos", agentId);
        }
        catch (Exception ex)
        {
            // Non-fatal: we can still function, just won't reuse across restarts
            _logger.LogWarning(ex, "Failed to persist agent ID to Cosmos. Agent will be recreated on next restart.");
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
            var instructions = await File.ReadAllTextAsync(path);
            _logger.LogInformation("Loaded instructions from file: {Path} ({Length} characters)", path, instructions.Length);
            return instructions;
        }

        // Fallback: embedded default prompt
        _logger.LogWarning("Instructions file not found at {Path}, using default prompt", path);
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
        // NOTE: File search tool removed due to known issue with empty vector stores
        // causing runs to complete without generating messages
        return
        [
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

    /// <summary>
    /// Internal record for persisting agent metadata in Cosmos DB.
    /// </summary>
    private record AgentMetaRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; init; } = AgentMetaId;

        [System.Text.Json.Serialization.JsonPropertyName("agentId")]
        public string AgentId { get; init; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("modelDeployment")]
        public string ModelDeployment { get; init; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("agentName")]
        public string AgentName { get; init; } = "";
    }
}