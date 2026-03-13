using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Cosmos;
using Microsoft.Extensions.Options;
using DiscoveryAgent.Configuration;
using System.Globalization;

namespace DiscoveryAgent.Functions;

public class ExportDataFunction
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<ExportDataFunction> _logger;
    private readonly DiscoveryBotConfig _config;

    public ExportDataFunction(
        CosmosClient cosmosClient,
        IOptions<DiscoveryBotConfig> config,
        ILogger<ExportDataFunction> logger)
    {
        _cosmosClient = cosmosClient;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/manage/export
    /// Exports discovery data in various formats (json, csv, markdown)
    /// Query params: format, startDate, endDate, contextId, userId
    /// </summary>
    [Function("ExportData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manage/export")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var format = query["format"] ?? "json";
            var contextId = query["contextId"];
            var userId = query["userId"];
            var startDate = query["startDate"];
            var endDate = query["endDate"];

            _logger.LogInformation("Exporting data in format: {Format}", format);

            // Gather data from multiple sources
            var exportData = new ExportData();

            // Get knowledge items
            var database = _cosmosClient.GetDatabase(_config.CosmosDatabase);
            var knowledgeContainer = database.GetContainer("knowledge-items");

            var knowledgeQuery = BuildKnowledgeQuery(contextId, userId, startDate, endDate);
            var knowledgeIterator = knowledgeContainer.GetItemQueryIterator<dynamic>(
                new QueryDefinition(knowledgeQuery));

            while (knowledgeIterator.HasMoreResults)
            {
                var response = await knowledgeIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    exportData.KnowledgeItems.Add(item);
                }
            }

            // Get conversation threads
            var threadsContainer = database.GetContainer("persistent-threads");
            var threadsQuery = BuildThreadsQuery(userId, startDate, endDate);
            var threadsIterator = threadsContainer.GetItemQueryIterator<dynamic>(
                new QueryDefinition(threadsQuery));

            while (threadsIterator.HasMoreResults)
            {
                var response = await threadsIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    exportData.Conversations.Add(item);
                }
            }

            // Get discovery contexts if available
            try
            {
                var contextsContainer = database.GetContainer("discovery-contexts");
                var contextsIterator = contextsContainer.GetItemQueryIterator<dynamic>(
                    new QueryDefinition("SELECT * FROM c WHERE c.type = 'context'"));

                while (contextsIterator.HasMoreResults)
                {
                    var response = await contextsIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        exportData.Contexts.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not retrieve contexts: {Message}", ex.Message);
            }

            // Generate response based on format
            HttpResponseData httpResponse;
            switch (format.ToLower())
            {
                case "csv":
                    httpResponse = await GenerateCsvResponse(req, exportData);
                    break;
                case "markdown":
                    httpResponse = await GenerateMarkdownResponse(req, exportData);
                    break;
                default:
                    httpResponse = await GenerateJsonResponse(req, exportData);
                    break;
            }

            return httpResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export data");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = "Failed to export data",
                details = ex.Message
            });
            return errorResponse;
        }
    }

    private string BuildKnowledgeQuery(string? contextId, string? userId, string? startDate, string? endDate)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(contextId))
            conditions.Add($"c.relatedContextId = '{contextId}'");
        if (!string.IsNullOrEmpty(userId))
            conditions.Add($"c.sourceUserId = '{userId}'");
        if (!string.IsNullOrEmpty(startDate))
            conditions.Add($"c.extractionTimestamp >= '{startDate}'");
        if (!string.IsNullOrEmpty(endDate))
            conditions.Add($"c.extractionTimestamp <= '{endDate}'");

        var whereClause = conditions.Any()
            ? $"WHERE {string.Join(" AND ", conditions)}"
            : "";

        return $"SELECT * FROM c {whereClause} ORDER BY c.extractionTimestamp DESC";
    }

    private string BuildThreadsQuery(string? userId, string? startDate, string? endDate)
    {
        var conditions = new List<string> { "c.type = 'thread'" };

        if (!string.IsNullOrEmpty(userId))
            conditions.Add($"c.metadata.userId = '{userId}'");
        if (!string.IsNullOrEmpty(startDate))
            conditions.Add($"c._ts >= {new DateTimeOffset(DateTime.Parse(startDate)).ToUnixTimeSeconds()}");
        if (!string.IsNullOrEmpty(endDate))
            conditions.Add($"c._ts <= {new DateTimeOffset(DateTime.Parse(endDate)).ToUnixTimeSeconds()}");

        return $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c._ts DESC";
    }

    private async Task<HttpResponseData> GenerateJsonResponse(HttpRequestData req, ExportData data)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Content-Disposition", $"attachment; filename=discovery-export-{DateTime.UtcNow:yyyyMMdd}.json");

        var jsonData = new
        {
            exportDate = DateTime.UtcNow,
            summary = new
            {
                knowledgeItemCount = data.KnowledgeItems.Count,
                conversationCount = data.Conversations.Count,
                contextCount = data.Contexts.Count,
                uniqueUsers = data.Conversations.Select(c => c.metadata?.userId?.ToString()).Distinct().Count()
            },
            data
        };

        await response.WriteAsJsonAsync(jsonData, new JsonSerializerOptions { WriteIndented = true });
        return response;
    }

    private async Task<HttpResponseData> GenerateCsvResponse(HttpRequestData req, ExportData data)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/csv");
        response.Headers.Add("Content-Disposition", $"attachment; filename=discovery-knowledge-{DateTime.UtcNow:yyyyMMdd}.csv");

        var csv = new StringBuilder();
        csv.AppendLine("ID,Content,Category,Confidence,Source User,Source Role,Thread ID,Context ID,Extraction Date,Verified");

        foreach (dynamic item in data.KnowledgeItems)
        {
            csv.AppendLine($"\"{item.id}\",\"{EscapeCsv(item.content?.ToString())}\",\"{item.category}\",{item.confidence}," +
                          $"\"{item.sourceUserId}\",\"{item.sourceUserRole}\",\"{item.sourceThreadId}\"," +
                          $"\"{item.relatedContextId}\",\"{item.extractionTimestamp}\",{item.verified ?? false}");
        }

        await response.WriteStringAsync(csv.ToString());
        return response;
    }

    private async Task<HttpResponseData> GenerateMarkdownResponse(HttpRequestData req, ExportData data)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/markdown");
        response.Headers.Add("Content-Disposition", $"attachment; filename=discovery-report-{DateTime.UtcNow:yyyyMMdd}.md");

        var markdown = new StringBuilder();
        markdown.AppendLine($"# Discovery Report");
        markdown.AppendLine($"*Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*\n");

        // Summary
        markdown.AppendLine("## Summary");
        markdown.AppendLine($"- **Knowledge Items**: {data.KnowledgeItems.Count}");
        markdown.AppendLine($"- **Conversations**: {data.Conversations.Count}");
        markdown.AppendLine($"- **Discovery Contexts**: {data.Contexts.Count}");
        markdown.AppendLine($"- **Unique Participants**: {data.Conversations.Select(c => c.metadata?.userId?.ToString()).Distinct().Count()}\n");

        // Knowledge by Category
        markdown.AppendLine("## Knowledge by Category");
        var categories = data.KnowledgeItems
            .GroupBy(k => k.category?.ToString() ?? "Uncategorized")
            .OrderByDescending(g => g.Count());

        foreach (var category in categories)
        {
            markdown.AppendLine($"\n### {category.Key} ({category.Count()} items)\n");
            foreach (var item in category.Take(10)) // Limit to top 10 per category
            {
                var content = item.content?.ToString() ?? "";
                if (content.Length > 200)
                    content = content.Substring(0, 197) + "...";

                markdown.AppendLine($"- {content}");
                markdown.AppendLine($"  - *Source: {item.sourceUserRole ?? item.sourceUserId} | Confidence: {item.confidence:P0}*");
            }
        }

        // Top Contributors
        markdown.AppendLine("\n## Top Contributors");
        var contributors = data.KnowledgeItems
            .GroupBy(k => k.sourceUserId?.ToString() ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(10);

        foreach (var contributor in contributors)
        {
            var role = contributor.First().sourceUserRole?.ToString() ?? "Unknown Role";
            markdown.AppendLine($"- **{contributor.Key}** ({role}): {contributor.Count()} contributions");
        }

        // Recent Activity
        markdown.AppendLine("\n## Recent Activity");
        var recentItems = data.KnowledgeItems
            .Where(k => k.extractionTimestamp != null)
            .OrderByDescending(k => k.extractionTimestamp)
            .Take(10);

        foreach (var item in recentItems)
        {
            var content = item.content?.ToString() ?? "";
            if (content.Length > 150)
                content = content.Substring(0, 147) + "...";

            markdown.AppendLine($"- {DateTime.Parse(item.extractionTimestamp.ToString()):yyyy-MM-dd}: {content}");
        }

        await response.WriteStringAsync(markdown.ToString());
        return response;
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
    }

    private class ExportData
    {
        public List<dynamic> KnowledgeItems { get; set; } = new();
        public List<dynamic> Conversations { get; set; } = new();
        public List<dynamic> Contexts { get; set; } = new();
    }
}