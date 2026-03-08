using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Tools;

/// <summary>
/// Handles the extract_knowledge function tool calls from the agent.
/// The actual extraction logic is driven by the LLM; this tool receives
/// the structured output and persists it via the KnowledgeStore.
/// See ConversationHandler.HandleKnowledgeExtraction for the wiring.
/// </summary>
public class DataExtractionTool
{
    private readonly ILogger<DataExtractionTool> _logger;

    public DataExtractionTool(ILogger<DataExtractionTool> logger)
    {
        _logger = logger;
    }
}

/// <summary>
/// Wraps Foundry's built-in file_search tool configuration.
/// File search is handled natively by the Foundry Agent Service —
/// this class manages the vector store and index configuration.
/// </summary>
public class FileSearchTool
{
    private readonly ILogger<FileSearchTool> _logger;

    public FileSearchTool(ILogger<FileSearchTool> logger)
    {
        _logger = logger;
    }
}

/// <summary>
/// Manages conversation thread persistence and session tracking.
/// Conversation history is stored natively by Foundry Agent Service threads;
/// this tool handles extended metadata and session state in Cosmos DB.
/// </summary>
public class ConversationStoreTool
{
    private readonly ILogger<ConversationStoreTool> _logger;

    public ConversationStoreTool(ILogger<ConversationStoreTool> logger)
    {
        _logger = logger;
    }
}
