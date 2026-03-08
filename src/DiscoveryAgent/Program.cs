using Azure.Identity;
using Azure.AI.Projects;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        // Foundry Client
        services.AddSingleton(_ =>
            new AIProjectClient(new Uri(settings.FoundryEndpoint), credential));

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

        services.AddScoped<ConversationHandler>();
        services.AddScoped<FileUploadHandler>();

        services.AddSingleton<FileSearchTool>();
        services.AddSingleton<DataExtractionTool>();
        services.AddSingleton<ConversationStoreTool>();

        // App Insights - Functions worker has built-in support, no explicit registration needed
    })
    .Build();

// Initialize agent on startup (run in background, don't block host startup)
_ = Task.Run(async () =>
{
    try
    {
        using var scope = host.Services.CreateScope();
        var agentManager = scope.ServiceProvider.GetRequiredService<AgentManager>();
        await agentManager.EnsureAgentExistsAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Failed to initialize agent on startup: {ex.Message}");
    }
});

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
