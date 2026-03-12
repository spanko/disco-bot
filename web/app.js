// Configuration - will be injected by Static Web App configuration
const API_BASE = window.API_ENDPOINT || 'https://discdev-func-3xr5ve.azurewebsites.net';
const API_KEY = window.API_KEY || 'VxOdrb12vGGjbYLIt7CQ7Jc6BsbyavW-dZejQhvVqo9dAzFumvoeQw=='; // Conversation function key

const userId = 'user-' + Math.random().toString(36).substring(7);
let threadId = null;

document.getElementById('userIdDisplay').textContent = userId;

const chatArea = document.getElementById('chatArea');
const messageInput = document.getElementById('messageInput');
const sendButton = document.getElementById('sendButton');
const attachButton = document.getElementById('attachButton');
const fileInput = document.getElementById('fileInput');
const loadingIndicator = document.getElementById('loading');
const uploadStatus = document.getElementById('uploadStatus');

function addMessage(content, type = 'agent') {
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${type}`;

    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';
    contentDiv.textContent = content;

    messageDiv.appendChild(contentDiv);
    chatArea.appendChild(messageDiv);
    chatArea.scrollTop = chatArea.scrollHeight;
}

async function sendMessage() {
    const message = messageInput.value.trim();
    if (!message) return;

    addMessage(message, 'user');
    messageInput.value = '';

    sendButton.disabled = true;
    loadingIndicator.classList.add('active');

    try {
        const url = API_KEY
            ? `${API_BASE}/api/conversation?code=${API_KEY}`
            : `${API_BASE}/api/conversation`;

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                userId: userId,
                message: message,
                threadId: threadId
            })
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
        }

        const data = await response.json();

        if (data.ThreadId || data.threadId) {
            threadId = data.ThreadId || data.threadId;
        }

        addMessage(data.Response || data.response || JSON.stringify(data), 'agent');

        // If knowledge was extracted, show a subtle notification
        if (data.ExtractedKnowledgeIds || data.extractedKnowledgeIds && data.ExtractedKnowledgeIds || data.extractedKnowledgeIds.length > 0) {
            addMessage(
                `✓ Captured ${data.ExtractedKnowledgeIds || data.extractedKnowledgeIds.length} knowledge item(s)`,
                'system'
            );
        }

    } catch (error) {
        console.error('Error:', error);
        addMessage(`Error: ${error.message}`, 'error');
    } finally {
        sendButton.disabled = false;
        loadingIndicator.classList.remove('active');
        messageInput.focus();
    }
}

async function uploadFile(file) {
    if (!threadId) {
        addMessage('Please start a conversation before uploading files', 'error');
        return;
    }

    uploadStatus.textContent = `Uploading ${file.name}...`;
    uploadStatus.classList.add('active');
    attachButton.disabled = true;

    try {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('userId', userId);
        formData.append('threadId', threadId);

        const url = API_KEY
            ? `${API_BASE}/api/upload?code=${API_KEY}`
            : `${API_BASE}/api/upload`;

        const response = await fetch(url, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`HTTP ${response.status}: ${errorText || response.statusText}`);
        }

        const data = await response.json();

        if (data.success) {
            if (data.isQuestionnaire) {
                addMessage(`✓ Uploaded questionnaire: ${data.fileName}`, 'system');
                addMessage('📋 I detected this is a questionnaire. I can help you work through it!', 'agent');
            } else {
                addMessage(`✓ Uploaded: ${data.fileName}`, 'system');
                addMessage('📄 I have received your document and will use it in our conversation.', 'agent');
            }
        } else {
            throw new Error(data.error || 'Upload failed');
        }

    } catch (error) {
        console.error('Upload error:', error);
        addMessage(`Upload failed: ${error.message}`, 'error');
    } finally {
        uploadStatus.textContent = '';
        uploadStatus.classList.remove('active');
        attachButton.disabled = false;
        fileInput.value = '';
    }
}

attachButton.addEventListener('click', () => {
    fileInput.click();
});

fileInput.addEventListener('change', (e) => {
    const file = e.target.files[0];
    if (file) {
        uploadFile(file);
    }
});

sendButton.addEventListener('click', sendMessage);
messageInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
        sendMessage();
    }
});

// Test health endpoint on load
fetch(`${API_BASE}/api/health`)
    .then(response => response.text())
    .then(data => {
        addMessage(`✓ Connected to Discovery Agent (${data})`, 'system');
    })
    .catch(error => {
        addMessage(`⚠ Connection check failed: ${error.message}`, 'error');
    });
