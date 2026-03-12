namespace DiscoveryAgent.Configuration;

/// <summary>
/// All configuration for the Discovery Bot. Populated from appsettings.json,
/// environment variables, or Key Vault. Environment variables use the same
/// names as the Bicep outputs (FOUNDRY_ENDPOINT, etc.).
/// </summary>
public class DiscoveryBotSettings
{
    // Microsoft Foundry
    public string FoundryEndpoint { get; set; } = "";
    public string FoundryAccountName { get; set; } = "";
    public string FoundryProjectName { get; set; } = "";
    public string PrimaryModelDeployment { get; set; } = "gpt-5.2-chat";
    public string FallbackModelDeployment { get; set; } = "gpt-4o";

    // Azure AI Search
    public string AiSearchEndpoint { get; set; } = "";
    public string AiSearchName { get; set; } = "";
    public string KnowledgeIndexName { get; set; } = "knowledge-items";

    // Cosmos DB
    public string CosmosEndpoint { get; set; } = "";
    public string CosmosDatabase { get; set; } = "discovery";

    // Storage
    public string StorageAccountName { get; set; } = "";
    public string StorageEndpoint { get; set; } = "";
    public string UploadContainer { get; set; } = "uploads";
    public string QuestionnaireContainer { get; set; } = "questionnaires";

    // Key Vault
    public string KeyVaultUri { get; set; } = "";

    // App Insights
    public string AppInsightsConnection { get; set; } = "";

    // Agent Config
    public string AgentName { get; set; } = "discovery-bot";
    public string InstructionsPath { get; set; } = "config/instructions.md";

    /// <summary>
    /// Builds settings from environment variables (azd populates these from Bicep outputs).
    /// </summary>
    public static DiscoveryBotSettings FromEnvironment() => new()
    {
        FoundryEndpoint = Env("FOUNDRY_ENDPOINT"),
        FoundryAccountName = Env("FOUNDRY_ACCOUNT_NAME"),
        FoundryProjectName = Env("FOUNDRY_PROJECT_NAME"),
        PrimaryModelDeployment = Env("PRIMARY_MODEL_DEPLOYMENT", "gpt-5.2-chat"),
        FallbackModelDeployment = Env("FALLBACK_MODEL_DEPLOYMENT", "gpt-4o"),
        AiSearchEndpoint = Env("AI_SEARCH_ENDPOINT"),
        AiSearchName = Env("AI_SEARCH_NAME"),
        CosmosEndpoint = Env("COSMOS_ENDPOINT"),
        CosmosDatabase = Env("COSMOS_DATABASE", "discovery"),
        StorageAccountName = Env("STORAGE_ACCOUNT_NAME"),
        StorageEndpoint = Env("STORAGE_ENDPOINT"),
        KeyVaultUri = Env("KEY_VAULT_URI"),
        AppInsightsConnection = Env("APP_INSIGHTS_CONNECTION"),
    };

    private static string Env(string name, string fallback = "")
        => Environment.GetEnvironmentVariable(name) ?? fallback;
}
