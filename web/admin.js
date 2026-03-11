// Configuration
// Use direct Function App URLs with API key for admin endpoints that require authentication
const API_BASE = window.API_ENDPOINT || 'https://discdev-func-3xr5ve.azurewebsites.net';
const API_KEY = window.API_KEY || 'QCuEUGIB51YmmbNrLSEch_uFCyA-PAQ8oFiOiY1icHsjAzFuTwz7ZA==';

// Global state
let currentContexts = [];
let currentQuestionnaires = [];
let currentKnowledge = [];

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    initializeNavigation();
    initializeContexts();
    initializeInstructions();
    initializeQuestionnaires();
    initializeKnowledge();
    initializeOrgChart();
});

// ============================================================================
// Navigation
// ============================================================================

function initializeNavigation() {
    const navItems = document.querySelectorAll('.nav-item');
    const sections = document.querySelectorAll('.content-section');

    navItems.forEach(item => {
        item.addEventListener('click', (e) => {
            e.preventDefault();
            const targetSection = item.getAttribute('data-section');

            // Update navigation
            navItems.forEach(nav => nav.classList.remove('active'));
            item.classList.add('active');

            // Update sections
            sections.forEach(section => section.classList.remove('active'));
            document.getElementById(`${targetSection}-section`).classList.add('active');

            // Update header
            const titles = {
                'contexts': 'Discovery Contexts',
                'instructions': 'Agent Instructions',
                'questionnaires': 'Questionnaires',
                'knowledge': 'Knowledge Base',
                'orgchart': 'Org Chart & Roles',
                'instances': 'Bot Instances',
                'analytics': 'Analytics'
            };
            document.getElementById('sectionTitle').textContent = titles[targetSection];

            // Update primary action button
            const primaryAction = document.getElementById('primaryAction');
            if (targetSection === 'contexts') {
                primaryAction.style.display = 'flex';
                primaryAction.innerHTML = '<span class="icon">➕</span> New Context';
                primaryAction.onclick = () => openContextModal();
            } else {
                primaryAction.style.display = 'none';
            }
        });
    });
}

// ============================================================================
// Discovery Contexts
// ============================================================================

function initializeContexts() {
    loadContexts();

    // Modal controls
    document.querySelector('.modal-close').addEventListener('click', closeContextModal);
    document.getElementById('cancelContext').addEventListener('click', closeContextModal);
    document.getElementById('saveContext').addEventListener('click', saveContext);

    // Search and filter
    document.getElementById('contextSearch').addEventListener('input', filterContexts);
    document.getElementById('contextFilter').addEventListener('change', filterContexts);
}

async function loadContexts() {
    const grid = document.getElementById('contextsGrid');
    grid.innerHTML = '<div class="loading-state">Loading contexts...</div>';

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/contexts?code=${API_KEY}`
            : `${API_BASE}/api/admin/contexts`;

        const response = await fetch(url);

        if (!response.ok) {
            throw new Error('Failed to load contexts');
        }

        currentContexts = await response.json();
        renderContexts(currentContexts);
    } catch (error) {
        console.error('Error loading contexts:', error);
        grid.innerHTML = '<div class="empty-state">Failed to load contexts. Click to retry.</div>';
        grid.onclick = loadContexts;
        showToast('Failed to load contexts', 'error');
    }
}

function renderContexts(contexts) {
    const grid = document.getElementById('contextsGrid');

    if (contexts.length === 0) {
        grid.innerHTML = '<div class="empty-state">No contexts found. Create your first context to get started.</div>';
        return;
    }

    grid.innerHTML = contexts.map(context => `
        <div class="card">
            <div class="card-header">
                <div class="card-title">${escapeHtml(context.name)}</div>
                <div class="card-actions">
                    <button class="card-action-btn" onclick="editContext('${context.contextId}')" title="Edit">✏️</button>
                    <button class="card-action-btn" onclick="deleteContext('${context.contextId}')" title="Delete">🗑️</button>
                </div>
            </div>
            <div class="card-body">
                <p>${escapeHtml(context.description || 'No description')}</p>
                <div style="margin-top: 10px;">
                    <span class="badge ${context.archived ? 'badge-archived' : 'badge-active'}">
                        ${context.archived ? 'Archived' : 'Active'}
                    </span>
                </div>
            </div>
            <div class="card-footer">
                <span>Mode: ${context.discoveryMode || 'open'}</span>
                <span>${context.discoveryAreas ? context.discoveryAreas.length : 0} areas</span>
            </div>
        </div>
    `).join('');
}

function filterContexts() {
    const search = document.getElementById('contextSearch').value.toLowerCase();
    const filter = document.getElementById('contextFilter').value;

    let filtered = currentContexts;

    // Apply search
    if (search) {
        filtered = filtered.filter(c =>
            c.name.toLowerCase().includes(search) ||
            (c.description && c.description.toLowerCase().includes(search))
        );
    }

    // Apply filter
    if (filter === 'active') {
        filtered = filtered.filter(c => !c.archived);
    } else if (filter === 'archived') {
        filtered = filtered.filter(c => c.archived);
    }

    renderContexts(filtered);
}

function openContextModal(contextId = null) {
    const modal = document.getElementById('contextModal');
    const title = document.getElementById('contextModalTitle');
    const form = document.getElementById('contextForm');

    form.reset();

    if (contextId) {
        const context = currentContexts.find(c => c.contextId === contextId);
        if (context) {
            title.textContent = 'Edit Discovery Context';
            document.getElementById('contextId').value = context.contextId;
            document.getElementById('contextName').value = context.name;
            document.getElementById('contextDescription').value = context.description || '';
            document.getElementById('discoveryMode').value = context.discoveryMode || 'open';
            document.getElementById('discoveryAreas').value = (context.discoveryAreas || []).join('\n');
            document.getElementById('keyQuestions').value = (context.keyQuestions || []).join('\n');
            document.getElementById('agentInstructions').value = context.agentInstructions || '';
        }
    } else {
        title.textContent = 'New Discovery Context';
    }

    modal.classList.add('active');
}

function closeContextModal() {
    document.getElementById('contextModal').classList.remove('active');
}

async function saveContext() {
    const contextId = document.getElementById('contextId').value;
    const name = document.getElementById('contextName').value.trim();
    const description = document.getElementById('contextDescription').value.trim();
    const discoveryMode = document.getElementById('discoveryMode').value;
    const discoveryAreas = document.getElementById('discoveryAreas').value
        .split('\n').map(s => s.trim()).filter(s => s);
    const keyQuestions = document.getElementById('keyQuestions').value
        .split('\n').map(s => s.trim()).filter(s => s);
    const agentInstructions = document.getElementById('agentInstructions').value.trim();

    if (!name) {
        showToast('Context name is required', 'error');
        return;
    }

    const payload = {
        contextId: contextId || undefined,
        name,
        description,
        discoveryMode,
        discoveryAreas,
        keyQuestions,
        agentInstructions
    };

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/context?code=${API_KEY}`
            : `${API_BASE}/api/admin/context`;

        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            throw new Error('Failed to save context');
        }

        showToast(contextId ? 'Context updated' : 'Context created', 'success');
        closeContextModal();
        loadContexts();
    } catch (error) {
        console.error('Error saving context:', error);
        showToast('Failed to save context', 'error');
    }
}

function editContext(contextId) {
    openContextModal(contextId);
}

async function deleteContext(contextId) {
    if (!confirm('Are you sure you want to delete this context?')) {
        return;
    }

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/context/${contextId}?code=${API_KEY}`
            : `${API_BASE}/api/admin/context/${contextId}`;

        const response = await fetch(url, { method: 'DELETE' });

        if (!response.ok) {
            throw new Error('Failed to delete context');
        }

        showToast('Context deleted', 'success');
        loadContexts();
    } catch (error) {
        console.error('Error deleting context:', error);
        showToast('Failed to delete context', 'error');
    }
}

// ============================================================================
// Agent Instructions Editor
// ============================================================================

function initializeInstructions() {
    loadInstructions();

    document.getElementById('reloadInstructions').addEventListener('click', loadInstructions);
    document.getElementById('saveInstructions').addEventListener('click', saveInstructions);
}

async function loadInstructions() {
    const editor = document.getElementById('instructionsEditor');
    editor.value = 'Loading instructions...';

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/instructions?code=${API_KEY}`
            : `${API_BASE}/api/admin/instructions`;

        const response = await fetch(url);

        if (!response.ok) {
            throw new Error('Failed to load instructions');
        }

        const text = await response.text();
        editor.value = text;
    } catch (error) {
        console.error('Error loading instructions:', error);
        editor.value = '# Error loading instructions\n\nFailed to load the instructions file. Please try again.';
        showToast('Failed to load instructions', 'error');
    }
}

async function saveInstructions() {
    const editor = document.getElementById('instructionsEditor');
    const content = editor.value;

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/instructions?code=${API_KEY}`
            : `${API_BASE}/api/admin/instructions`;

        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'text/plain' },
            body: content
        });

        if (!response.ok) {
            throw new Error('Failed to save instructions');
        }

        showToast('Instructions saved successfully', 'success');
    } catch (error) {
        console.error('Error saving instructions:', error);
        showToast('Failed to save instructions', 'error');
    }
}

// ============================================================================
// Questionnaires
// ============================================================================

function initializeQuestionnaires() {
    loadQuestionnaires();

    const uploadArea = document.getElementById('questionnaireUpload');
    const fileInput = document.getElementById('questionnaireFileInput');

    uploadArea.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', handleQuestionnaireUpload);

    // Drag and drop
    uploadArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadArea.classList.add('drag-over');
    });

    uploadArea.addEventListener('dragleave', () => {
        uploadArea.classList.remove('drag-over');
    });

    uploadArea.addEventListener('drop', (e) => {
        e.preventDefault();
        uploadArea.classList.remove('drag-over');
        handleQuestionnaireUpload({ target: { files: e.dataTransfer.files } });
    });

    document.getElementById('questionnaireSearch').addEventListener('input', filterQuestionnaires);
}

async function loadQuestionnaires() {
    const grid = document.getElementById('questionnairesGrid');
    grid.innerHTML = '<div class="loading-state">Loading questionnaires...</div>';

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/questionnaires?code=${API_KEY}`
            : `${API_BASE}/api/admin/questionnaires`;

        const response = await fetch(url);

        if (!response.ok) {
            throw new Error('Failed to load questionnaires');
        }

        currentQuestionnaires = await response.json();
        renderQuestionnaires(currentQuestionnaires);
    } catch (error) {
        console.error('Error loading questionnaires:', error);
        grid.innerHTML = '<div class="empty-state">No questionnaires uploaded yet. Upload your first questionnaire to get started.</div>';
    }
}

function renderQuestionnaires(questionnaires) {
    const grid = document.getElementById('questionnairesGrid');

    if (questionnaires.length === 0) {
        grid.innerHTML = '<div class="empty-state">No questionnaires found.</div>';
        return;
    }

    grid.innerHTML = questionnaires.map(q => `
        <div class="card">
            <div class="card-header">
                <div class="card-title">${escapeHtml(q.title || q.fileName)}</div>
                <div class="card-actions">
                    <button class="card-action-btn" onclick="viewQuestionnaire('${q.questionnaireId}')" title="View">👁️</button>
                    <button class="card-action-btn" onclick="deleteQuestionnaire('${q.questionnaireId}')" title="Delete">🗑️</button>
                </div>
            </div>
            <div class="card-body">
                <p>${q.sections ? q.sections.length : 0} sections, ${q.questions ? q.questions.length : 0} questions</p>
            </div>
            <div class="card-footer">
                <span>${new Date(q.uploadedAt).toLocaleDateString()}</span>
            </div>
        </div>
    `).join('');
}

function filterQuestionnaires() {
    const search = document.getElementById('questionnaireSearch').value.toLowerCase();
    const filtered = currentQuestionnaires.filter(q =>
        (q.title && q.title.toLowerCase().includes(search)) ||
        (q.fileName && q.fileName.toLowerCase().includes(search))
    );
    renderQuestionnaires(filtered);
}

async function handleQuestionnaireUpload(e) {
    const files = e.target.files;
    if (!files || files.length === 0) return;

    for (const file of files) {
        // Implementation will use the existing upload endpoint
        showToast(`Uploading ${file.name}...`, 'info');
    }
}

function viewQuestionnaire(id) {
    // TODO: Implement questionnaire viewer
    showToast('Questionnaire viewer coming soon', 'info');
}

async function deleteQuestionnaire(id) {
    if (!confirm('Are you sure you want to delete this questionnaire?')) return;

    try {
        const url = API_KEY
            ? `${API_BASE}/api/admin/questionnaire/${id}?code=${API_KEY}`
            : `${API_BASE}/api/admin/questionnaire/${id}`;

        const response = await fetch(url, { method: 'DELETE' });

        if (!response.ok) throw new Error('Failed to delete');

        showToast('Questionnaire deleted', 'success');
        loadQuestionnaires();
    } catch (error) {
        showToast('Failed to delete questionnaire', 'error');
    }
}

// ============================================================================
// Knowledge Base
// ============================================================================

function initializeKnowledge() {
    loadKnowledge();

    document.getElementById('knowledgeSearch').addEventListener('input', filterKnowledge);
    document.getElementById('knowledgeFilter').addEventListener('change', filterKnowledge);
    document.getElementById('knowledgeContext').addEventListener('change', filterKnowledge);
}

async function loadKnowledge() {
    const grid = document.getElementById('knowledgeGrid');
    grid.innerHTML = '<div class="loading-state">Loading knowledge items...</div>';

    try {
        const url = API_KEY
            ? `${API_BASE}/api/knowledge?code=${API_KEY}`
            : `${API_BASE}/api/knowledge`;

        const response = await fetch(url);

        if (!response.ok) {
            throw new Error('Failed to load knowledge');
        }

        currentKnowledge = await response.json();
        renderKnowledge(currentKnowledge);

        // Populate context filter
        const contexts = [...new Set(currentKnowledge.map(k => k.relatedContextId).filter(Boolean))];
        const contextSelect = document.getElementById('knowledgeContext');
        contextSelect.innerHTML = '<option value="">All Contexts</option>' +
            contexts.map(c => `<option value="${c}">${c}</option>`).join('');
    } catch (error) {
        console.error('Error loading knowledge:', error);
        grid.innerHTML = '<div class="empty-state">No knowledge items found.</div>';
    }
}

function renderKnowledge(items) {
    const grid = document.getElementById('knowledgeGrid');

    if (items.length === 0) {
        grid.innerHTML = '<div class="empty-state">No knowledge items found.</div>';
        return;
    }

    grid.innerHTML = items.map(item => `
        <div class="card">
            <div class="card-header">
                <span class="badge badge-active">${item.category || 'Uncategorized'}</span>
            </div>
            <div class="card-body">
                <p>${escapeHtml(item.content.substring(0, 150))}${item.content.length > 150 ? '...' : ''}</p>
            </div>
            <div class="card-footer">
                <span>Confidence: ${Math.round((item.confidence || 0) * 100)}%</span>
                <span>${new Date(item.extractionTimestamp).toLocaleDateString()}</span>
            </div>
        </div>
    `).join('');
}

function filterKnowledge() {
    const search = document.getElementById('knowledgeSearch').value.toLowerCase();
    const categoryFilter = document.getElementById('knowledgeFilter').value;
    const contextFilter = document.getElementById('knowledgeContext').value;

    let filtered = currentKnowledge;

    if (search) {
        filtered = filtered.filter(k => k.content.toLowerCase().includes(search));
    }

    if (categoryFilter !== 'all') {
        filtered = filtered.filter(k => k.category === categoryFilter);
    }

    if (contextFilter) {
        filtered = filtered.filter(k => k.relatedContextId === contextFilter);
    }

    renderKnowledge(filtered);
}

// ============================================================================
// Org Chart
// ============================================================================

function initializeOrgChart() {
    const uploadArea = document.getElementById('orgchartUpload');
    const fileInput = document.getElementById('orgchartFileInput');

    uploadArea.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', handleOrgChartUpload);

    // Drag and drop
    uploadArea.addEventListener('dragover', (e) => {
        e.preventDefault();
        uploadArea.classList.add('drag-over');
    });

    uploadArea.addEventListener('dragleave', () => {
        uploadArea.classList.remove('drag-over');
    });

    uploadArea.addEventListener('drop', (e) => {
        e.preventDefault();
        uploadArea.classList.remove('drag-over');
        handleOrgChartUpload({ target: { files: e.dataTransfer.files } });
    });
}

async function handleOrgChartUpload(e) {
    const file = e.target.files[0];
    if (!file) return;

    showToast(`Uploading ${file.name}...`, 'info');

    // TODO: Implement org chart upload
    showToast('Org chart upload coming soon', 'info');
}

// ============================================================================
// Utilities
// ============================================================================

function showToast(message, type = 'info') {
    const container = document.getElementById('toastContainer');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.innerHTML = `<div class="toast-message">${escapeHtml(message)}</div>`;

    container.appendChild(toast);

    setTimeout(() => {
        toast.style.animation = 'slideIn 0.3s ease-out reverse';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
