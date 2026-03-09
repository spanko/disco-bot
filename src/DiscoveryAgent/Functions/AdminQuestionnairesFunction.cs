using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using DiscoveryAgent.Models;
using System.Net;

namespace DiscoveryAgent.Functions;

public class AdminQuestionnairesFunction
{
    private readonly Database _cosmosDb;
    private readonly ILogger<AdminQuestionnairesFunction> _logger;

    public AdminQuestionnairesFunction(
        Database cosmosDb,
        ILogger<AdminQuestionnairesFunction> logger)
    {
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/questionnaires - List all questionnaires
    /// </summary>
    [Function("AdminQuestionnairesList")]
    public async Task<HttpResponseData> GetQuestionnaires(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/questionnaires")] HttpRequestData req)
    {
        try
        {
            var container = _cosmosDb.GetContainer("questionnaires");

            var query = new QueryDefinition("SELECT * FROM c ORDER BY c.uploadedAt DESC");
            var iterator = container.GetItemQueryIterator<ParsedQuestionnaire>(query);

            var questionnaires = new List<ParsedQuestionnaire>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                questionnaires.AddRange(response);
            }

            _logger.LogInformation("Retrieved {Count} questionnaires", questionnaires.Count);

            var httpResponse = req.CreateResponse(HttpStatusCode.OK);
            await httpResponse.WriteAsJsonAsync(questionnaires);
            return httpResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving questionnaires");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve questionnaires" });
            return errorResponse;
        }
    }

    /// <summary>
    /// GET /api/admin/questionnaire/{id} - Get single questionnaire
    /// </summary>
    [Function("AdminQuestionnaireGet")]
    public async Task<HttpResponseData> GetQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/questionnaire/{questionnaireId}")] HttpRequestData req,
        string questionnaireId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("questionnaires");
            var response = await container.ReadItemAsync<ParsedQuestionnaire>(
                questionnaireId,
                new PartitionKey(questionnaireId));

            var httpResponse = req.CreateResponse(HttpStatusCode.OK);
            await httpResponse.WriteAsJsonAsync(response.Resource);
            return httpResponse;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Questionnaire not found" });
            return notFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving questionnaire {QuestionnaireId}", questionnaireId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to retrieve questionnaire" });
            return errorResponse;
        }
    }

    /// <summary>
    /// DELETE /api/admin/questionnaire/{id} - Delete questionnaire
    /// </summary>
    [Function("AdminQuestionnaireDelete")]
    public async Task<HttpResponseData> DeleteQuestionnaire(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "admin/questionnaire/{questionnaireId}")] HttpRequestData req,
        string questionnaireId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("questionnaires");
            await container.DeleteItemAsync<ParsedQuestionnaire>(
                questionnaireId,
                new PartitionKey(questionnaireId));

            _logger.LogInformation("Deleted questionnaire {QuestionnaireId}", questionnaireId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, questionnaireId });
            return response;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Questionnaire not found" });
            return notFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting questionnaire {QuestionnaireId}", questionnaireId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to delete questionnaire" });
            return errorResponse;
        }
    }
}
