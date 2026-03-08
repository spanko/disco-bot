# GPT-5 Codex Prompt: Azure Foundry Conversational Discovery Bot

## Project Overview

Build an enterprise-grade conversational discovery bot using **Microsoft Foundry Agent Service** with the **Microsoft Agent Framework SDK**. The bot will be deployed to **Microsoft Teams** and feature dynamic context setting, adaptive questioning based on user roles, intelligent questionnaire processing, persistent memory with knowledge attribution, and document upload handling.

---

## System Architecture

### Core Components
- **Runtime**: Microsoft Foundry Agent Service (managed agent runtime)
- **Framework**: Microsoft Agent Framework SDK (Python or C#/.NET)
- **Memory**: Foundry Agent Service built-in memory (persistent long-term storage)
- **Knowledge Retrieval**: Foundry IQ with Azure AI Search
- **Deployment Channel**: Microsoft Teams via Azure Bot Service
- **State Management**: Foundry threads with Cosmos DB for extended persistence
- **File Processing**: Azure Blob Storage + Document Intelligence

### Required Azure Resources
```
- Microsoft Foundry Project (with Agent Service enabled)
- Azure AI Search (for Foundry IQ knowledge grounding)
- Azure Cosmos DB (for extended state/attribution tracking)
- Azure Blob Storage (for document uploads)
- Azure Bot Service (Teams channel)
- Azure Document Intelligence (for questionnaire/document parsing)
- Model deployment: GPT-5 or Claude Sonnet (via Foundry Models)
```

---

## Functional Requirements

### 1. Context Setting Engine

Create a system that allows administrators to define discovery contexts before conversations begin.

```python
from dataclasses import dataclass, field
from typing import List, Dict, Optional
from enum import Enum

class DiscoveryMode(Enum):
    EXPLORATORY = "exploratory"      # Open-ended discovery
    STRUCTURED = "structured"         # Questionnaire-driven
    HYBRID = "hybrid"                 # Combines both approaches

@dataclass
class DiscoveryContext:
    """Defines the scope and focus areas for discovery sessions."""
    context_id: str
    name: str
    description: str
    discovery_mode: DiscoveryMode
    
    # Areas requiring deep investigation
    discovery_areas: List[str] = field(default_factory=list)
    
    # Specific questions or topics to explore
    key_questions: List[str] = field(default_factory=list)
    
    # Topics to avoid or handle carefully
    sensitive_areas: List[str] = field(default_factory=list)
    
    # Expected outcomes from discovery
    success_criteria: List[str] = field(default_factory=list)
    
    # Custom instructions for the agent
    agent_instructions: str = ""
    
    # Linked questionnaires (if any)
    questionnaire_ids: List[str] = field(default_factory=list)

@dataclass
class RoleConfiguration:
    """Adapts bot behavior based on user's organizational role."""
    role_id: str
    role_name: str
    
    # Communication style preferences
    tone: str  # "formal", "conversational", "technical"
    detail_level: str  # "executive", "detailed", "technical"
    
    # Focus areas for this role
    priority_topics: List[str] = field(default_factory=list)
    
    # Question depth and complexity
    question_complexity: str  # "high-level", "detailed", "deep-dive"
    
    # Formatting preferences
    response_format: str  # "bullets", "narrative", "structured"
```

### 2. Adaptive User Profiling

Implement a conversation flow that identifies user roles and adapts accordingly:

```python
from agent_framework import ChatAgent
from agent_framework.azure import AzureAIAgentClient

ROLE_DISCOVERY_PROMPT = """
At the start of each new conversation, naturally determine the user's role and responsibilities 
to tailor the discovery experience. Ask questions like:

1. "Before we dive in, could you tell me a bit about your role? What are your primary 
   areas of responsibility?"

2. Based on their response, ask follow-up questions to understand:
   - Their decision-making authority
   - Key stakeholders they work with
   - Their most pressing challenges
   - How they prefer to receive information

3. Once you understand their role, adapt your:
   - Question depth and technical complexity
   - Communication tone and formality
   - Topics to prioritize
   - Response formatting style

Store this role context in memory for the entire session and future sessions with this user.
"""

class AdaptiveDiscoveryAgent:
    def __init__(self, foundry_client: AzureAIAgentClient, context: DiscoveryContext):
        self.client = foundry_client
        self.context = context
        self.user_profile: Optional[RoleConfiguration] = None
        
    async def initialize_session(self, thread):
        """Start session with role discovery."""
        await self.agent.run(
            "Let's begin our discovery session. To make this as valuable as possible "
            "for you, I'd like to understand your perspective first. "
            "Could you tell me about your role and primary responsibilities?",
            thread=thread
        )
        
    async def adapt_to_role(self, role_info: dict, thread):
        """Dynamically adjust agent behavior based on discovered role."""
        self.user_profile = self._map_to_role_config(role_info)
        
        # Update agent instructions dynamically
        adapted_instructions = self._generate_adapted_instructions()
        # Apply through Foundry Agent Service configuration update
```

### 3. Questionnaire Processing Engine

Build a system that imports, parses, and intelligently processes questionnaires:

```python
from typing import List, Dict, Any
from dataclasses import dataclass
from azure.ai.documentintelligence import DocumentIntelligenceClient

@dataclass
class QuestionnaireSection:
    section_id: str
    title: str
    description: str
    parent_section_id: Optional[str]  # For nested sections
    order: int
    
@dataclass  
class Question:
    question_id: str
    section_id: str
    text: str
    question_type: str  # "open", "multiple_choice", "scale", "yes_no"
    options: List[str] = field(default_factory=list)
    follow_up_logic: Dict[str, str] = field(default_factory=dict)  # answer -> next_question_id
    required: bool = True
    order: int = 0

@dataclass
class ParsedQuestionnaire:
    questionnaire_id: str
    title: str
    description: str
    sections: List[QuestionnaireSection]
    questions: List[Question]
    metadata: Dict[str, Any]

class QuestionnaireProcessor:
    """
    Processes uploaded questionnaire documents and converts them into 
    structured, interactive discovery sessions.
    """
    
    def __init__(self, doc_intelligence_client: DocumentIntelligenceClient):
        self.doc_client = doc_intelligence_client
        
    async def parse_questionnaire(self, document_url: str) -> ParsedQuestionnaire:
        """
        Parse a questionnaire document using Document Intelligence.
        Detects: sections, sub-sections, question types, numbering patterns.
        """
        # Use Document Intelligence to extract structure
        poller = self.doc_client.begin_analyze_document(
            "prebuilt-document",
            document_url
        )
        result = poller.result()
        
        # Extract hierarchical structure
        sections = self._extract_sections(result)
        questions = self._extract_questions(result, sections)
        
        return ParsedQuestionnaire(
            questionnaire_id=generate_id(),
            title=self._extract_title(result),
            description=self._extract_description(result),
            sections=sections,
            questions=questions,
            metadata=self._extract_metadata(result)
        )
    
    def _extract_sections(self, doc_result) -> List[QuestionnaireSection]:
        """
        Identify sections from document structure:
        - Heading levels (H1, H2, H3)
        - Numbered sections (1.0, 1.1, 1.1.1)
        - Bold/formatted section titles
        - Horizontal rules or page breaks
        """
        sections = []
        # Implementation: Walk through paragraphs, detect section markers
        # Build parent-child relationships for nested sections
        return sections
    
    def _extract_questions(self, doc_result, sections) -> List[Question]:
        """
        Extract questions and their types:
        - Look for question marks
        - Identify numbered/bulleted question lists
        - Detect multiple choice options (a, b, c or checkboxes)
        - Find rating scales (1-5, Strongly Agree to Strongly Disagree)
        - Parse conditional logic (If yes, proceed to...)
        """
        questions = []
        # Implementation: Pattern matching for question types
        return questions

class InteractiveQuestionnaireAgent:
    """
    Conducts questionnaire sessions conversationally, following 
    document structure while allowing natural flow.
    """
    
    QUESTIONNAIRE_SYSTEM_PROMPT = """
    You are conducting a discovery session based on a structured questionnaire.
    
    CRITICAL INSTRUCTIONS:
    1. Follow the questionnaire's logical structure (sections → subsections → questions)
    2. Present questions conversationally, not as a form
    3. Allow the user to elaborate beyond the literal question
    4. Capture both the direct answer AND any valuable context they provide
    5. Use follow-up questions naturally when answers are incomplete
    6. Respect conditional logic (skip irrelevant sections based on answers)
    7. Summarize section insights before moving to the next section
    8. Track completion status and allow users to revisit earlier sections
    
    RESPONSE STRUCTURE:
    - Brief acknowledgment of their previous answer
    - Insight or connection to broader context (when relevant)
    - Natural transition to next question
    - Clear indication of progress through the questionnaire
    """
    
    async def conduct_questionnaire(
        self, 
        questionnaire: ParsedQuestionnaire,
        thread,
        start_section: Optional[str] = None
    ):
        """Run an interactive questionnaire session."""
        current_section = start_section or questionnaire.sections[0].section_id
        
        # Initialize session with questionnaire context
        intro = self._generate_session_intro(questionnaire)
        await self.agent.run(intro, thread=thread)
        
        # Process sections and questions
        for section in self._get_ordered_sections(questionnaire, current_section):
            await self._process_section(section, questionnaire, thread)
```

### 4. Knowledge Storage with Attribution

Implement persistent storage that traces all knowledge back to specific interactions:

```python
from datetime import datetime
from typing import List, Dict, Optional
from dataclasses import dataclass
import uuid

@dataclass
class KnowledgeItem:
    """A discrete piece of knowledge captured from interactions."""
    item_id: str
    content: str
    category: str  # "fact", "opinion", "decision", "requirement", "concern"
    confidence: float  # 0.0 to 1.0
    
    # Attribution
    source_user_id: str
    source_user_role: str
    source_thread_id: str
    source_message_id: str
    extraction_timestamp: datetime
    
    # Context
    related_context_id: str  # Links to DiscoveryContext
    section_id: Optional[str]  # If from questionnaire
    question_id: Optional[str]
    
    # Relationships
    related_items: List[str] = field(default_factory=list)
    supersedes: Optional[str] = None  # If this updates prior knowledge
    
    # Metadata
    tags: List[str] = field(default_factory=list)
    verified: bool = False

@dataclass
class AggregatedKnowledge:
    """Synthesized knowledge from multiple sources."""
    topic: str
    summary: str
    supporting_items: List[str]  # KnowledgeItem IDs
    consensus_level: str  # "unanimous", "majority", "mixed", "conflicting"
    source_count: int
    last_updated: datetime

class KnowledgeStore:
    """
    Manages knowledge extraction, storage, and retrieval with full attribution.
    Uses Cosmos DB for persistence and Azure AI Search for retrieval.
    """
    
    def __init__(self, cosmos_client, search_client, foundry_memory):
        self.cosmos = cosmos_client
        self.search = search_client
        self.foundry_memory = foundry_memory  # Foundry Agent Service memory
        
    async def extract_and_store(
        self, 
        message_content: str,
        user_id: str,
        thread_id: str,
        context: DiscoveryContext
    ) -> List[KnowledgeItem]:
        """
        Extract knowledge items from a user message and store with attribution.
        """
        # Use LLM to extract structured knowledge
        extraction_prompt = f"""
        Extract key knowledge items from this user response.
        
        Context: {context.description}
        Discovery Areas: {context.discovery_areas}
        
        User Message: {message_content}
        
        For each piece of knowledge, identify:
        1. The specific fact, opinion, decision, requirement, or concern
        2. Confidence level (how clearly/definitely was this stated)
        3. Category of knowledge
        4. Related topics or themes
        
        Return as structured JSON.
        """
        
        extracted = await self._call_extraction_model(extraction_prompt)
        
        # Create attributed knowledge items
        items = []
        for item_data in extracted:
            item = KnowledgeItem(
                item_id=str(uuid.uuid4()),
                content=item_data["content"],
                category=item_data["category"],
                confidence=item_data["confidence"],
                source_user_id=user_id,
                source_user_role=await self._get_user_role(user_id),
                source_thread_id=thread_id,
                source_message_id=item_data.get("message_id"),
                extraction_timestamp=datetime.utcnow(),
                related_context_id=context.context_id,
                tags=item_data.get("tags", [])
            )
            items.append(item)
            
            # Store in Cosmos DB
            await self._store_in_cosmos(item)
            
            # Index in Azure AI Search for retrieval
            await self._index_for_search(item)
            
            # Also store summary in Foundry memory for agent access
            await self._update_foundry_memory(item)
            
        return items
    
    async def get_attributed_knowledge(
        self, 
        topic: str,
        context_id: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Retrieve knowledge on a topic with full attribution chain.
        """
        # Search for relevant knowledge items
        results = await self.search.search(
            search_text=topic,
            filter=f"related_context_id eq '{context_id}'" if context_id else None,
            select=["item_id", "content", "source_user_id", "source_user_role", 
                    "source_thread_id", "confidence", "extraction_timestamp"]
        )
        
        # Group by source and build attribution
        attributed = {
            "topic": topic,
            "knowledge_items": [],
            "sources": {},
            "aggregated_view": None
        }
        
        for result in results:
            attributed["knowledge_items"].append(result)
            user_id = result["source_user_id"]
            if user_id not in attributed["sources"]:
                attributed["sources"][user_id] = {
                    "role": result["source_user_role"],
                    "contributions": []
                }
            attributed["sources"][user_id]["contributions"].append(result["item_id"])
        
        # Generate aggregated view
        if len(attributed["knowledge_items"]) > 1:
            attributed["aggregated_view"] = await self._aggregate_knowledge(
                attributed["knowledge_items"]
            )
            
        return attributed
    
    async def trace_knowledge_origin(self, item_id: str) -> Dict[str, Any]:
        """
        Get full provenance chain for a knowledge item.
        Returns: user, conversation, timestamp, exact quote, context.
        """
        item = await self._get_from_cosmos(item_id)
        thread = await self._get_thread_details(item.source_thread_id)
        message = await self._get_message(item.source_message_id)
        
        return {
            "item": item,
            "original_message": message,
            "conversation_context": thread,
            "user_profile": await self._get_user_profile(item.source_user_id),
            "extraction_method": "llm_extraction",
            "verification_status": item.verified
        }
```

### 5. Teams Integration with Document Upload

Configure the bot for Microsoft Teams with file handling:

```python
from azure.ai.projects import AIProjectClient
from azure.identity import DefaultAzureCredential
from azure.storage.blob import BlobServiceClient

class TeamsDocumentHandler:
    """
    Handles document uploads from Microsoft Teams conversations.
    Supports: PDF, DOCX, XLSX, images, and common text formats.
    """
    
    SUPPORTED_FORMATS = [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv",
        "image/png",
        "image/jpeg"
    ]
    
    def __init__(self, blob_client: BlobServiceClient, doc_intelligence_client):
        self.blob = blob_client
        self.doc_intelligence = doc_intelligence_client
        
    async def process_teams_attachment(
        self, 
        attachment: dict,
        thread_id: str,
        user_id: str
    ) -> dict:
        """
        Process a file attachment from Teams.
        
        Teams attachments come in different forms:
        - Direct file uploads (contentUrl)
        - OneDrive/SharePoint links
        - Inline images
        """
        content_type = attachment.get("contentType", "")
        
        if content_type not in self.SUPPORTED_FORMATS:
            return {"error": f"Unsupported format: {content_type}"}
        
        # Download file content
        if "contentUrl" in attachment:
            content = await self._download_from_url(attachment["contentUrl"])
        elif "content" in attachment:  # Inline content
            content = attachment["content"]
        else:
            return {"error": "No accessible content in attachment"}
        
        # Store in blob with metadata
        blob_url = await self._store_document(
            content=content,
            filename=attachment.get("name", "unnamed"),
            thread_id=thread_id,
            user_id=user_id,
            content_type=content_type
        )
        
        # Extract content for agent context
        extracted = await self._extract_document_content(blob_url, content_type)
        
        return {
            "blob_url": blob_url,
            "extracted_text": extracted["text"],
            "document_structure": extracted.get("structure"),
            "is_questionnaire": self._detect_questionnaire(extracted),
            "metadata": {
                "original_name": attachment.get("name"),
                "size": len(content),
                "uploaded_by": user_id,
                "upload_thread": thread_id
            }
        }
    
    def _detect_questionnaire(self, extracted: dict) -> bool:
        """Detect if uploaded document is a questionnaire."""
        indicators = [
            "?" in extracted["text"],  # Questions present
            any(word in extracted["text"].lower() for word in 
                ["please answer", "select", "rate", "describe", "explain"]),
            extracted.get("structure", {}).get("has_numbered_items", False)
        ]
        return sum(indicators) >= 2

# Bot configuration for Teams deployment
TEAMS_BOT_CONFIG = {
    "manifest": {
        "version": "1.0.0",
        "id": "${BOT_ID}",
        "name": "Discovery Bot",
        "description": "Conversational discovery and questionnaire bot",
        "icons": {
            "outline": "outline.png",
            "color": "color.png"
        },
        "bots": [{
            "botId": "${BOT_ID}",
            "scopes": ["personal", "team", "groupchat"],
            "supportsFiles": True,  # Enable file uploads
            "supportsCalling": False,
            "supportsVideo": False
        }],
        "permissions": [
            "identity",
            "messageTeamMembers"
        ],
        "validDomains": [
            "token.botframework.com",
            "${YOUR_DOMAIN}"
        ]
    }
}
```

### 6. Agent System Prompt

The core system prompt that ties everything together:

```python
DISCOVERY_BOT_SYSTEM_PROMPT = """
You are an intelligent discovery agent designed to help organizations gather, 
organize, and synthesize knowledge through natural conversation.

## CORE IDENTITY
You are fundamentally inquisitive. Your primary purpose is to discover, understand, 
and document knowledge - not to provide answers. You ask thoughtful questions, 
listen actively, and help users articulate their knowledge in structured ways.

## CONVERSATION INITIALIZATION
At the start of EVERY new conversation:

1. **Identify the User's Role**
   - Ask: "To make our conversation as relevant as possible, could you share a bit 
     about your role? What are your main areas of responsibility?"
   - Listen for: job function, seniority, decision-making scope, key stakeholders
   - Adapt your tone, depth, and focus based on their response

2. **Establish Context**
   - Confirm the discovery context/project you're working on
   - Review what's already been learned (check your memory)
   - Identify gaps that this user might help fill

3. **Set Expectations**
   - Explain how the session will work
   - Note that their insights will be captured and attributed
   - Ask if they have any constraints (time, topics to avoid)

## DISCOVERY APPROACH

### For Open Discovery (no questionnaire):
- Start broad, then drill down based on interesting threads
- Use the "5 Whys" technique to get to root insights
- Connect their input to knowledge from other users
- Regularly summarize and validate your understanding
- Look for patterns, contradictions, and gaps

### For Questionnaire-Guided Discovery:
- Follow the document structure but maintain conversational flow
- Explain the purpose of each section before diving in
- Allow elaboration beyond the literal questions
- Track completion and allow navigation between sections
- Capture both structured answers AND contextual insights

## ADAPTIVE BEHAVIOR

Based on user role, adjust:
- **Executive/Leadership**: High-level, strategic focus. Concise questions. 
  Focus on decisions, priorities, concerns.
- **Manager/Director**: Balance of strategy and operations. Focus on processes, 
  team dynamics, challenges.
- **Individual Contributor**: Detailed, technical depth. Focus on specifics, 
  workflows, pain points.
- **External Stakeholder**: Relationship-focused. Focus on expectations, 
  experiences, feedback.

## KNOWLEDGE CAPTURE

For every significant piece of information:
1. Confirm your understanding with the user
2. Categorize it (fact, opinion, decision, requirement, concern)
3. Note the confidence level (clearly stated vs. implied)
4. Identify relationships to other captured knowledge
5. Flag contradictions with previously captured information

## DOCUMENT HANDLING

When users upload documents:
1. Acknowledge receipt and explain what you can do with it
2. If it's a questionnaire: Offer to conduct an interactive session based on it
3. If it's reference material: Extract relevant content for context
4. Ask clarifying questions about how they want to use the document

## RESPONSE STYLE

- Be warm but professional
- Ask one main question at a time (with optional follow-ups)
- Provide brief acknowledgments of what you've learned
- Use their terminology back to them
- Never be judgmental about their answers
- Show genuine curiosity

## MEMORY & CONTINUITY

- Reference relevant prior conversations naturally
- Build on previously captured knowledge
- Note when new information updates or contradicts prior knowledge
- Remember user preferences and adapt accordingly

## EXAMPLE FLOWS

**Starting a new session:**
"Hello! I'm here to help with our [Project Name] discovery session. Before we 
begin, I'd love to understand your perspective better. What's your role, and 
what are your main responsibilities?"

**Transitioning to questionnaire:**
"I see you've uploaded [Document Name]. This looks like a questionnaire with 
[X sections] covering [topics]. Would you like me to guide you through this 
conversationally? We can take it section by section, and you're welcome to 
elaborate on anything that comes to mind."

**Probing deeper:**
"That's interesting - you mentioned [X]. Can you help me understand more about 
why that's particularly important? What would happen if [scenario]?"

**Connecting insights:**
"What you're describing reminds me of something [Role] mentioned about [related 
topic]. Do you see a connection there, or are these separate concerns?"
"""
```

---

## Implementation Structure

```
discovery-bot/
├── src/
│   ├── agents/
│   │   ├── discovery_agent.py      # Main agent orchestration
│   │   ├── questionnaire_agent.py  # Questionnaire-specific logic
│   │   └── prompts/
│   │       ├── system_prompt.py
│   │       └── role_prompts.py
│   ├── knowledge/
│   │   ├── store.py                # Cosmos DB knowledge storage
│   │   ├── extraction.py           # LLM-based knowledge extraction
│   │   ├── attribution.py          # Source tracking
│   │   └── aggregation.py          # Cross-source synthesis
│   ├── questionnaires/
│   │   ├── parser.py               # Document Intelligence parsing
│   │   ├── models.py               # Data structures
│   │   └── session.py              # Interactive session management
│   ├── channels/
│   │   ├── teams/
│   │   │   ├── bot.py              # Teams bot adapter
│   │   │   ├── attachments.py      # File handling
│   │   │   └── manifest/           # Teams app manifest
│   │   └── api/
│   │       └── endpoints.py        # REST API for admin
│   ├── context/
│   │   ├── models.py               # DiscoveryContext, RoleConfiguration
│   │   ├── manager.py              # Context lifecycle management
│   │   └── adapters.py             # Role-based adaptation logic
│   └── config/
│       ├── settings.py
│       └── azure.py                # Azure resource configuration
├── infrastructure/
│   ├── bicep/                      # Azure resource templates
│   │   ├── main.bicep
│   │   ├── foundry.bicep
│   │   ├── cosmos.bicep
│   │   └── search.bicep
│   └── scripts/
│       └── deploy.sh
├── tests/
├── requirements.txt
└── README.md
```

---

## Key Azure Foundry SDK Patterns

### Agent Creation with Memory
```python
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    AgentConfiguration,
    MemoryConfiguration,
    FileSearchTool,
    AzureAISearchSource
)
from azure.identity import DefaultAzureCredential

async def create_discovery_agent():
    client = AIProjectClient(
        endpoint="https://<project>.api.azureml.ms",
        credential=DefaultAzureCredential()
    )
    
    # Create agent with memory enabled
    agent = await client.agents.create(
        name="discovery-bot",
        model="gpt-5",
        instructions=DISCOVERY_BOT_SYSTEM_PROMPT,
        tools=[
            FileSearchTool(),  # For document processing
            # Add custom tools as needed
        ],
        tool_resources={
            "file_search": {
                "vector_stores": [vector_store_id]
            }
        },
        # Enable Foundry memory
        memory=MemoryConfiguration(
            enabled=True,
            topics=["user_preferences", "discovered_knowledge", "session_context"]
        )
    )
    
    return agent
```

### Thread Management with Persistence
```python
async def manage_conversation(agent, user_id: str, message: str):
    # Get or create thread for user
    thread = await get_or_create_thread(user_id)
    
    # Add user message
    await client.agents.threads.messages.create(
        thread_id=thread.id,
        role="user",
        content=message
    )
    
    # Run agent
    run = await client.agents.threads.runs.create(
        thread_id=thread.id,
        agent_id=agent.id
    )
    
    # Wait for completion and get response
    while run.status in ["queued", "in_progress"]:
        await asyncio.sleep(0.5)
        run = await client.agents.threads.runs.retrieve(
            thread_id=thread.id,
            run_id=run.id
        )
    
    # Get assistant messages
    messages = await client.agents.threads.messages.list(
        thread_id=thread.id
    )
    
    return messages
```

---

## Deployment Checklist

1. **Azure Resources**
   - [ ] Create Microsoft Foundry project
   - [ ] Deploy GPT-5 or Claude model
   - [ ] Configure Azure AI Search with Foundry IQ
   - [ ] Set up Cosmos DB for knowledge store
   - [ ] Create Blob Storage for documents
   - [ ] Register Azure Bot

2. **Agent Configuration**
   - [ ] Create agent with memory enabled
   - [ ] Configure file search tools
   - [ ] Set up vector store for documents
   - [ ] Test memory persistence

3. **Teams Deployment**
   - [ ] Create Teams app manifest
   - [ ] Configure bot messaging endpoint
   - [ ] Enable Teams channel in Azure Bot
   - [ ] Deploy and test in Teams
   - [ ] Configure file upload permissions

4. **Testing**
   - [ ] Test role discovery flow
   - [ ] Test questionnaire parsing
   - [ ] Verify knowledge attribution
   - [ ] Test cross-session memory
   - [ ] Load test with multiple users

---

## Additional Considerations

### Security
- Use Managed Identity for all Azure service connections
- Implement row-level security in Cosmos DB for knowledge isolation
- Ensure RBAC is configured for Foundry resources
- Validate uploaded documents for malware

### Scalability
- Use Foundry Agent Service managed hosting for auto-scaling
- Configure Cosmos DB for multi-region if needed
- Implement caching for frequently accessed knowledge

### Observability
- Enable Foundry tracing for all agent interactions
- Configure Application Insights integration
- Set up alerts for failed runs and high latency

### Compliance
- Implement data retention policies for knowledge store
- Enable audit logging for all user interactions
- Configure content safety guardrails in Foundry
