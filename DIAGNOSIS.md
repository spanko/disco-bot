# Disco-Bot: Root Cause Analysis

## Finding 1: Potential double-pathed endpoint (depends on deployment method)

**The `FOUNDRY_ENDPOINT` Bicep output already includes the full project path:**

```
// foundry-account.bicep line 108:
output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'

// main.bicep line 196:
output FOUNDRY_ENDPOINT string = foundry.outputs.projectEndpoint
```

**But `Program.cs` appends `/api/projects/{name}` AGAIN:**

```csharp
var projectEndpoint = $"{settings.FoundryEndpoint.TrimEnd('/')}/api/projects/{settings.FoundryProjectName}";
```

**If azd sets `FOUNDRY_ENDPOINT` from the Bicep output, you get:**
```
https://discdev-foundry-abc123.services.ai.azure.com/api/projects/discdev-project/api/projects/discdev-project
```

However — if the `DiscoveryBot:FoundryEndpoint` section in appsettings.json is empty (which it is), and the `FOUNDRY_ENDPOINT` env var IS set by azd, then `FromEnvironment()` is called and `settings.FoundryEndpoint` gets the full project URL. The doubled path would cause the `PersistentAgentsClient` to hit a wrong URL.

**BUT** the agent creation succeeded with `asst_C2npepVvWgG3uLBCQ3jhgGjI` — so either the SDK is tolerant of the extra path, or the env var is set to just the base URL. Check the startup log line `[Startup] Constructed project endpoint:` to confirm.

---

## Finding 2: Missing Function App environment variables (HIGH PROBABILITY)

**The Bicep `function-app.bicep` does NOT set any of the discovery bot env vars.**

Look at lines 84-117 of `function-app.bicep` — the `appSettings` array only contains:
- `AzureWebJobsStorage`
- `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`
- `WEBSITE_CONTENTSHARE`
- `FUNCTIONS_EXTENSION_VERSION`
- `FUNCTIONS_WORKER_RUNTIME`
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
- `WEBSITE_RUN_FROM_PACKAGE`

**None of these are set:**
- `FOUNDRY_ENDPOINT`
- `FOUNDRY_PROJECT_NAME`
- `PRIMARY_MODEL_DEPLOYMENT`
- `COSMOS_ENDPOINT`
- `AI_SEARCH_ENDPOINT`
- `STORAGE_ENDPOINT`
- etc.

**This means the app relies entirely on `azd` or the deploy script to inject these.**
If `azd up` doesn't wire Bicep outputs → Function App app settings, the app starts
with empty settings and falls back to defaults from `DiscoveryBotSettings.cs`:

```csharp
public string PrimaryModelDeployment { get; set; } = "gpt-5.2";  // Not "gpt-5.2-chat"!
```

While the actual Azure deployment name (from dev.bicepparam) is `gpt-5.2-chat`.

---

## Finding 3: The `CreateMessageAsync` content parameter

Looking at the SDK docs, `CreateMessageAsync` accepts:
```csharp
CreateMessageAsync(string threadId, MessageRole role, string content)
```

But newer SDK versions may require `BinaryData` or a `MessageContent` list rather
than a plain string. If the string overload silently succeeds but the message body
is empty/malformed from the API's perspective, the agent would have nothing to
respond to and would complete immediately with zero completion tokens.

---

## Recommended Next Steps (in priority order)

### Step 1: Check the actual startup logs

Look at App Insights or function logs for these lines after the latest deployment:
```
[Startup] Constructed project endpoint: ???
[Startup] Model deployment: ???
[Startup] Agent name: ???
```

And from AgentManager:
```
Agent created successfully: asst_C2npepVvWgG3uLBCQ3jhgGjI (Name=???, Model=???)
Run XXX completed. Agent=???, Model=???, Usage: Prompt=???, Completion=???
```

**The `Completion=0` vs `Completion=N` tells us everything.** If completion tokens are 0,
the model literally didn't generate anything. If completion tokens are >0 but no messages
appear, it's a message retrieval issue.

### Step 2: Fix the endpoint construction (regardless of current behavior)

Replace the endpoint construction in `Program.cs` with:
```csharp
// If FOUNDRY_ENDPOINT already contains /api/projects/, use it directly
// Otherwise, construct the full project endpoint
var projectEndpoint = settings.FoundryEndpoint.Contains("/api/projects/")
    ? settings.FoundryEndpoint.TrimEnd('/')
    : $"{settings.FoundryEndpoint.TrimEnd('/')}/api/projects/{settings.FoundryProjectName}";
```

### Step 3: Wire env vars in Bicep function-app.bicep

Add parameters for all discovery bot settings and wire them into `appSettings`.
Don't rely on azd alone.

### Step 4: Build a standalone test script

Create a minimal C# console app or .csx script that:
1. Creates a PersistentAgentsClient with the known-good endpoint
2. Creates an agent with the correct model deployment name
3. Creates a thread
4. Adds a message: "Hello, please respond with a single word."
5. Creates a run
6. Polls to completion
7. Dumps ALL messages and the run's Usage property

This isolates whether the issue is in your app code or in the Foundry API itself.
