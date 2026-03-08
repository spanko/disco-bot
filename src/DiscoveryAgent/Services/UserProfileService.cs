using DiscoveryAgent.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace DiscoveryAgent.Services;

/// <summary>
/// Manages user profiles that the agent uses for adaptive behavior.
/// Profiles are created/updated when the agent calls the store_user_profile tool.
/// </summary>
public class UserProfileService
{
    private readonly Database _cosmosDb;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(Database cosmosDb, ILogger<UserProfileService> logger)
    {
        _cosmosDb = cosmosDb;
        _logger = logger;
    }

    public async Task UpsertAsync(UserProfile profile)
    {
        var container = _cosmosDb.GetContainer("user-profiles");
        await container.UpsertItemAsync(profile, new PartitionKey(profile.UserId));
        _logger.LogInformation("User profile upserted: {UserId} as {Role}", profile.UserId, profile.RoleName);
    }

    public async Task<UserProfile?> GetAsync(string userId)
    {
        try
        {
            var container = _cosmosDb.GetContainer("user-profiles");
            var response = await container.ReadItemAsync<UserProfile>(
                userId, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
