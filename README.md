# Discovery Chatbot

An enterprise-grade conversational discovery bot built on **Microsoft Foundry Agent Service** with C#/.NET. Deploys as both a **web chat** and a **Microsoft Teams bot** from a single codebase.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    CONFIGURABLE LAYER                    │
│  config/instructions.md    config/agent.yaml             │
│  config/knowledge/         infra/params/*.bicepparam     │
└──────────────┬──────────────────────────┬───────────────┘
               │                          │
┌──────────────▼──────────────┐ ┌────────▼────────────────┐
│     AGENT RUNTIME (C#)      │ │   AZURE INFRASTRUCTURE  │
│  src/DiscoveryAgent/        │ │   infra/ (Bicep IaC)    │
│  • ConversationHandler      │ │   • Foundry Account     │
│  • FileUploadHandler        │ │   • AI Search           │
│  • KnowledgeStore           │ │   • Cosmos DB           │
│  • QuestionnaireProcessor   │ │   • Blob Storage        │
│  • AgentManager             │ │   • Bot Service         │
│  • UserProfileService       │ │   • Key Vault           │
└──────────────┬──────────────┘ │   • App Insights        │
               │                └─────────────────────────┘
┌──────────────▼──────────────────────────────────────────┐
│                   DELIVERY CHANNELS                      │
│   Web Chat (index.html)  ←→  Responses API               │
│   Microsoft Teams        ←→  Activity Protocol (Bot Svc) │
└─────────────────────────────────────────────────────────┘
```

## Quick Start

### Prerequisites

- Azure CLI (`az`) + logged in
- Azure Developer CLI (`azd`) with AI agent extension
- Docker
- .NET 9 SDK
- An Azure subscription with permissions to create Foundry resources

### Deploy

```bash
# Option A: PowerShell
./scripts/deploy.ps1 -Environment dev -ResourceGroup discovery-dev -PublishTeams

# Option B: Bash
./scripts/deploy.sh --env dev --rg discovery-dev --teams
```

This single command:
1. Provisions all Azure resources via Bicep
2. Builds the C# agent container
3. Deploys to Foundry Agent Service
4. Publishes the web chat to static hosting
5. (Optional) Sets up the Teams channel

### Configure for a New Use Case

To repurpose this solution for a completely different discovery scenario, you only need to modify files in the `config/` folder:

| File | Purpose |
|------|---------|
| `config/instructions.md` | The LLM system prompt — defines agent personality, discovery flow, and behavior |
| `config/agent.yaml` | Agent manifest — model selection, tools, knowledge sources |
| `config/knowledge/` | Seed documents to pre-index for RAG |

Everything in `infra/` and `src/` stays the same.

### Configure for a New Environment

Create a new `.bicepparam` file in `infra/params/`:

| Parameter | Description |
|-----------|-------------|
| `prefix` | Resource name prefix |
| `suffix` | Environment tag (dev/prod) |
| `primaryModelCapacity` | GPT-5.2 throughput (thousands TPM) |
| `deployerObjectId` | Your Azure AD object ID |
| `enablePublicAccess` | `true` for dev, `false` for prod |

## Project Structure

```
discovery-chatbot/
├── azure.yaml                  # azd project definition
├── infra/                      # REUSABLE — Bicep IaC
│   ├── main.bicep              #   Orchestrator
│   ├── modules/                #   Resource modules
│   │   ├── foundry-account.bicep
│   │   ├── ai-search.bicep
│   │   ├── cosmos-db.bicep
│   │   ├── storage.bicep
│   │   ├── key-vault.bicep
│   │   ├── app-insights.bicep
│   │   ├── bot-service.bicep
│   │   └── role-assignments.bicep
│   └── params/                 #   CONFIGURABLE — per-environment
│       ├── dev.bicepparam
│       └── prod.bicepparam
├── src/
│   ├── DiscoveryAgent/         # REUSABLE — C# hosted agent
│   │   ├── Program.cs          #   Entry point + DI + API endpoints
│   │   ├── Configuration/      #   Settings from env vars / appsettings
│   │   ├── Handlers/           #   Conversation + file upload handling
│   │   ├── Services/           #   AgentManager, KnowledgeStore, etc.
│   │   ├── Models/             #   Domain models
│   │   ├── Tools/              #   Custom tool definitions
│   │   └── Dockerfile
│   └── WebChat/                # REUSABLE — browser chat UI
│       └── index.html
├── config/                     # CONFIGURABLE — swap per use case
│   ├── agent.yaml
│   ├── instructions.md
│   └── knowledge/
├── scripts/
│   ├── deploy.ps1
│   ├── deploy.sh
│   ├── publish-teams.ps1
│   └── teardown.ps1
└── tests/
```

## Capabilities

### File Upload + RAG
Users upload documents via web chat or Teams. Files are stored in Azure Blob, optionally indexed in Azure AI Search, and available for grounded retrieval during conversations.

### Conversation Persistence
All conversations persist as Foundry threads backed by Cosmos DB. Users can resume sessions, and the agent references prior conversations via built-in memory.

### Structured Data Extraction
The agent calls custom function tools to extract and categorize knowledge from every substantive user message. Each item is attributed to the source user, thread, and timestamp.

### Adaptive Behavior
The agent profiles each user's role at the start of conversation and adapts tone, question depth, and focus areas accordingly.

### Questionnaire Processing
Uploaded questionnaire documents are automatically detected, parsed into structured sections/questions, and conducted as interactive conversational sessions.

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/conversation` | Send a message, get agent response |
| POST | `/api/upload` | Upload a file to the current thread |
| POST | `/api/admin/context` | Create/update a discovery context |
| GET | `/api/knowledge/{contextId}` | Retrieve extracted knowledge |
| GET | `/health` | Health check |

## Teardown

```powershell
./scripts/teardown.ps1 -ResourceGroup discovery-dev
```
