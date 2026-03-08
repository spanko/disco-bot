// Configuration - will be injected by Static Web App configuration
const API_BASE = window.API_ENDPOINT || 'https://discdev-func-3xr5ve.azurewebsites.net';
const API_KEY = window.API_KEY || 'pgA3mK7ono-cL9-IbQtLeLLWxfQa-UegTwIpjsgo7Z-vAzFuKKQSAw=='; // Replace with your actual default key

const userId = 'user-' + Math.random().toString(36).substring(7);
let threadId = null;

document.getElementById('userIdDisplay').textContent = userId;

const chatArea = document.getElementById('chatArea');
const messageInput = document.getElementById('messageInput');
const sendButton = document.getElementById('sendButton');
const loadingIndicator = document.getElementById('loading');

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

        if (data.threadId) {
            threadId = data.threadId;
        }

        addMessage(data.response || JSON.stringify(data), 'agent');

        // If knowledge was extracted, show a subtle notification
        if (data.extractedKnowledgeIds && data.extractedKnowledgeIds.length > 0) {
            addMessage(
                `✓ Captured ${data.extractedKnowledgeIds.length} knowledge item(s)`,
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
