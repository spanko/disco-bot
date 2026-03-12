#!/usr/bin/env dotnet-script
// =============================================================================
// Standalone Foundry Persistent Agents test script
// Run with: dotnet script TestAgent.csx
// Or paste into a .NET 9 console app's Program.cs (remove the #r lines)
//
// Prerequisites:
//   dotnet tool install -g dotnet-script
//   az login
// =============================================================================

#r "nuget: Azure.AI.Agents.Persistent, 1.2.0-beta.9"
#r "nuget: Azure.Identity, 1.14.1"

using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;

// ---- CONFIGURE THESE ----
var projectEndpoint = Environment.GetEnvironmentVariable("PROJECT_ENDPOINT")
    ?? "https://discdev-foundry-3xr5ve.services.ai.azure.com/api/projects/discdev-project";
var modelDeployment = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME")
    ?? "gpt-4o-mini";
// -------------------------

Console.WriteLine($"Endpoint: {projectEndpoint}");
Console.WriteLine($"Model:    {modelDeployment}");
Console.WriteLine();

var credential = new DefaultAzureCredential();
var client = new PersistentAgentsClient(projectEndpoint, credential);

// Step 1: Create agent
Console.WriteLine("Creating agent...");
var agent = await client.Administration.CreateAgentAsync(
    model: modelDeployment,
    name: "test-agent-diagnostic",
    instructions: "You are a helpful assistant. Always respond with a brief answer."
);
Console.WriteLine($"  Agent: {agent.Value.Id} (Name={agent.Value.Name}, Model={agent.Value.Model})");

try
{
    // Step 2: Create thread
    Console.WriteLine("Creating thread...");
    var thread = await client.Threads.CreateThreadAsync();
    Console.WriteLine($"  Thread: {thread.Value.Id}");

    // Step 3: Add message
    Console.WriteLine("Adding message...");
    var msg = await client.Messages.CreateMessageAsync(
        thread.Value.Id,
        MessageRole.User,
        "Hello! Please respond with the word 'working' and nothing else."
    );
    Console.WriteLine($"  Message: {msg.Value.Id} (Role={msg.Value.Role})");

    // Step 4: Create run
    Console.WriteLine("Creating run...");
    var run = await client.Runs.CreateRunAsync(thread.Value.Id, agent.Value.Id);
    Console.WriteLine($"  Run: {run.Value.Id} (Status={run.Value.Status})");

    // Step 5: Poll
    Console.WriteLine("Polling...");
    while (true)
    {
        await Task.Delay(500);
        run = await client.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
        Console.Write($"  Status={run.Value.Status}");

        if (run.Value.Status == RunStatus.Completed)
        {
            Console.WriteLine();
            Console.WriteLine($"  Model:            {run.Value.Model}");
            Console.WriteLine($"  Prompt tokens:    {run.Value.Usage?.PromptTokens ?? -1}");
            Console.WriteLine($"  Completion tokens: {run.Value.Usage?.CompletionTokens ?? -1}");
            Console.WriteLine($"  Total tokens:     {run.Value.Usage?.TotalTokens ?? -1}");
            break;
        }

        if (run.Value.Status == RunStatus.Failed || 
            run.Value.Status == RunStatus.Cancelled ||
            run.Value.Status == RunStatus.Expired)
        {
            Console.WriteLine();
            Console.WriteLine($"  FAILED! LastError: {run.Value.LastError?.Message ?? "(none)"}");
            Console.WriteLine($"  LastError Code:    {run.Value.LastError?.Code ?? "(none)"}");
            break;
        }

        if (run.Value.Status == RunStatus.RequiresAction)
        {
            Console.WriteLine(" (unexpected tool call with no tools!)");
            break;
        }

        Console.Write(".");
    }

    // Step 6: Dump messages
    Console.WriteLine("\nAll messages in thread:");
    Console.WriteLine("---");
    await foreach (var threadMsg in client.Messages.GetMessagesAsync(thread.Value.Id))
    {
        Console.WriteLine($"  [{threadMsg.Role}] CreatedAt={threadMsg.CreatedAt}");
        foreach (var content in threadMsg.ContentItems)
        {
            if (content is MessageTextContent textContent)
                Console.WriteLine($"    Text: \"{textContent.Text}\"");
            else
                Console.WriteLine($"    Content type: {content.GetType().Name}");
        }
    }
    Console.WriteLine("---");

    // Cleanup thread
    await client.Threads.DeleteThreadAsync(thread.Value.Id);
}
finally
{
    // Cleanup agent
    Console.WriteLine("\nCleaning up agent...");
    await client.Administration.DeleteAgentAsync(agent.Value.Id);
    Console.WriteLine("Done.");
}
