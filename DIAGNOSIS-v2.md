# Disco-Bot: Updated Root Cause Analysis (v2)

After reviewing the Microsoft Foundry documentation, SDK reference, and community
reports, here is my updated assessment.

---

## CONFIRMED: This is a known Foundry Agent Service behavior

Multiple Microsoft Q&A posts describe the exact same symptom: runs complete with
status "Completed" but no assistant message is appended to the thread. Key findings:

1. **Microsoft Q&A confirms this as a known pattern** — runs completing without
   generating messages, even with zero tools. The Foundry Agent Service runtime
   has implicit behaviors around token limits, content filtering, and response
   validation that can cause this silently.

2. **The `max_completion_tokens` parameter matters** — the `CreateRunAsync` SDK
   docs show this is an optional parameter. When NOT set, the Foundry runtime may
   apply a default that's too low or the orchestration layer may cap it. One Q&A
   thread specifically notes that agents complete with `incomplete` status when
   token limits are exceeded, but your code isn't checking for `incomplete_details`.

3. **Content filtering can silently suppress responses** — Foundry's content safety
   layer can block the model's output without surfacing an error. The run still
   shows "Completed" but no message is created. This has been reported by multiple
   users.

---

## Three things to try RIGHT NOW

### 1. Set `maxCompletionTokens` explicitly on `CreateRunAsync`

Your current code:
```csharp
var run = await agentsClient.Runs.CreateRunAsync(
    threadId,
    _agentManager.AgentId
);
```

Change to:
```csharp
var run = await agentsClient.Runs.CreateRunAsync(
    threadId,
    _agentManager.AgentId,
    maxCompletionTokens: 4096
);
```

The full overload signature is:
```csharp
CreateRunAsync(
    string threadId,
    string assistantId,
    string overrideModelName = default,
    string overrideInstructions = default,
    string additionalInstructions = default,
    IEnumerable<ThreadMessageOptions> additionalMessages = default,
    IEnumerable<ToolDefinition> overrideTools = default,
    bool? stream = default,
    float? temperature = default,
    float? topP = default,
    int? maxPromptTokens = default,
    int? maxCompletionTokens = default,   // <-- THIS ONE
    ...
)
```

Use named parameters to set just what you need:
```csharp
var run = await agentsClient.Runs.CreateRunAsync(
    threadId: threadId,
    assistantId: _agentManager.AgentId,
    maxCompletionTokens: 4096,
    maxPromptTokens: 8192
);
```

### 2. Check `run.Value.IncompleteDetails` after completion

The run might actually be completing with status `Completed` but having
`IncompleteDetails` set (which is different from `Failed`). Add this logging:

```csharp
if (status == RunStatus.Completed)
{
    _logger.LogInformation(
        "Run {RunId} completed. Agent={AgentId}, Model={Model}, " +
        "Usage: Prompt={PromptTokens}, Completion={CompletionTokens}, " +
        "IncompleteDetails={IncompleteDetails}",
        run.Value.Id, run.Value.AssistantId, run.Value.Model,
        run.Value.Usage?.PromptTokens ?? 0,
        run.Value.Usage?.CompletionTokens ?? 0,
        run.Value.IncompleteDetails ?? "none");
    break;
}
```

Also check for `RunStatus.Incomplete` (not just Failed/Cancelled/Expired):
```csharp
if (status == RunStatus.Incomplete)
{
    _logger.LogWarning("Run {RunId} incomplete: {Details}",
        run.Value.Id, run.Value.IncompleteDetails);
    // Still try to get messages — there might be a partial response
    break;
}
```

### 3. Fix the endpoint double-path issue

As identified earlier, if `FOUNDRY_ENDPOINT` already contains `/api/projects/`,
Program.cs doubles it. Fix:

```csharp
// Smart endpoint construction - handles both formats
var projectEndpoint = settings.FoundryEndpoint.Contains("/api/projects/")
    ? settings.FoundryEndpoint.TrimEnd('/')
    : $"{settings.FoundryEndpoint.TrimEnd('/')}/api/projects/{settings.FoundryProjectName}";
```

---

## The test script approach

The `TestAgent.csx` script I provided earlier is still the fastest way to isolate
whether this is a Foundry API issue or an app configuration issue. Run it locally
with `az login` and the correct endpoint/model. If it also produces no messages,
you know it's a Foundry-side issue and the fix is the `maxCompletionTokens` parameter
or a support ticket.

---

## Summary of code changes needed in ConversationHandler.cs

In the run creation block (~line 107), change from:
```csharp
var run = await agentsClient.Runs.CreateRunAsync(
    threadId,
    _agentManager.AgentId
);
```

To:
```csharp
var run = await agentsClient.Runs.CreateRunAsync(
    threadId: threadId,
    assistantId: _agentManager.AgentId,
    maxCompletionTokens: 4096,
    maxPromptTokens: 16384
);
```

And in the polling loop, add handling for `RunStatus.Incomplete` alongside the
existing `Completed`/`Failed`/`Cancelled`/`Expired` checks.
