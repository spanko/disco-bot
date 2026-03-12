using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Configuration;
using DiscoveryAgent.Services;
using DiscoveryAgent.Handlers;
using DiscoveryAgent.Tools;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // Configuration - bind from IConfiguration (supports appsettings.json and environment variables)
        var config = context.Configuration;
        var settings = config.GetSection("DiscoveryBot").Get<DiscoveryBotSettings>() ?? new DiscoveryBotSettings();

        // Also check for azd-style environment variables as fallback
        if (string.IsNullOrEmpty(settings.FoundryEndpoint))
        {
            settings = DiscoveryBotSettings.FromEnvironment();
        }

        services.AddSingleton(settings);

        // Azure Credential
        var credential = new DefaultAzureCredential();
        services.AddSingleton(credential);

        // Foundry Persistent Agents Client
        // Expected format: https://<aiservices-id>.services.ai.azure.com/api/projects/<project-name>
        // IMPORTANT: The Bicep output FOUNDRY_ENDPOINT may already include /api/projects/<name>,
        // so we detect and avoid doubling the path.
        var baseEndpoint = settings.FoundryEndpoint.TrimEnd('/');
        var projectEndpoint = baseEndpoint.Contains("/api/projects/")
            ? baseEndpoint
            : $"{baseEndpoint}/api/projects/{settings.FoundryProjectName}";

        // Log the constructed endpoint for debugging (will show in startup logs)
        Console.WriteLine($"[Startup] Constructed project endpoint: {projectEndpoint}");
        Console.WriteLine($"[Startup] Model deployment: {settings.PrimaryModelDeployment}");
        Console.WriteLine($"[Startup] Agent name: {settings.AgentName}");

        services.AddSingleton(_ =>
            new PersistentAgentsClient(projectEndpoint, credential));

        // AI Search Client
        services.AddSingleton(_ =>
            new SearchClient(
                new Uri(settings.AiSearchEndpoint),
                "knowledge-items",
                credential));

        // Cosmos DB
        services.AddSingleton(_ => new CosmosClient(settings.CosmosEndpoint, credential, new()));
        services.AddSingleton(sp =>
            sp.GetRequiredService<CosmosClient>().GetDatabase(settings.CosmosDatabase));

        // Blob Storage
        services.AddSingleton(_ =>
            new BlobServiceClient(new Uri(settings.StorageEndpoint), credential));

        // Application Services
        services.AddSingleton<AgentManager>();
        services.AddSingleton<KnowledgeStore>();
        services.AddSingleton<QuestionnaireProcessor>();
        services.AddSingleton<UserProfileService>();
        services.AddSingleton<ContextManagementService>();

        services.AddScoped<ConversationHandler>();
        services.AddScoped<FileUploadHandler>();

        services.AddSingleton<FileSearchTool>();
        services.AddSingleton<DataExtractionTool>();
        services.AddSingleton<ConversationStoreTool>();

        // App Insights - Functions worker has built-in support, no explicit registration needed
    })
    .Build();

// =====================================================================
// Initialize agent BEFORE accepting requests.
// This replaces the old fire-and-forget Task.Run that silently swallowed
// errors and raced with incoming HTTP requests.
// =====================================================================
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

try
{
    logger.LogInformation("Starting agent initialization...");
    var agentManager = host.Services.GetRequiredService<AgentManager>();
    await agentManager.EnsureAgentExistsAsync();
    logger.LogInformation("Agent initialization complete. Starting host.");
}
catch (Exception ex)
{
    // Log the FULL exception — don't swallow it like before.
    // The app will still start (so health checks work), but the agent
    // readiness signal will be in a faulted state, and ConversationHandler
    // will report a clear error to callers.
    logger.LogCritical(ex,
        "Agent initialization FAILED. The bot will return errors until this is resolved. " +
        "Check: (1) FOUNDRY_ENDPOINT is correct, (2) model deployment '{Model}' exists, " +
        "(3) the managed identity has Cognitive Services User role.",
        host.Services.GetRequiredService<DiscoveryBotSettings>().PrimaryModelDeployment);
}

host.Run();

// Request/Response Models
public record ConversationRequest(
    string UserId,
    string Message,
    string? ThreadId = null,
    string? ContextId = null
);

public record ConversationResponse(
    string ThreadId,
    string Response,
    string AgentId,
    List<string>? ExtractedKnowledgeIds = null
);

public record DiscoveryContextConfig(
    string ContextId,
    string Name,
    string Description,
    string DiscoveryMode,
    List<string> DiscoveryAreas,
    List<string> KeyQuestions,
    List<string> SensitiveAreas,
    List<string> SuccessCriteria,
    string AgentInstructions
);
