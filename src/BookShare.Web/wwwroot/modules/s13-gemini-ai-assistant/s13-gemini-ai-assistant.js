import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const viewport = document.getElementById('s13ChatViewport');
    const formSendChat = document.getElementById('formSendS13Chat');
    const messageInput = document.getElementById('s13MessageInput');
    const sessionIdInput = document.getElementById('s13CurrentSessionId');
    const btnSend = document.getElementById('btnSendS13Chat');
    const formCreateSession = document.getElementById('formCreateAiSession');
    const btnCreateSession = document.getElementById('btnCreateSession');

    if (viewport) {
        viewport.scrollTop = viewport.scrollHeight;
    }

    function appendMessageBubble(senderLabel, text, isAi, metaSuffix = 'Vừa xong') {
        if (!viewport) return;

        const row = document.createElement('div');
        row.className = `s13-msg-row ${isAi ? 'ai' : 'user'}`;

        const avatar = document.createElement('div');
        avatar.className = 's13-msg-avatar';
        avatar.innerText = isAi ? '🤖' : '👤';

        const bubble = document.createElement('div');
        bubble.className = 's13-msg-bubble';

        const meta = document.createElement('div');
        meta.className = 's13-msg-meta';

        const strong = document.createElement('strong');
        strong.innerText = senderLabel;

        const span = document.createElement('span');
        span.innerText = metaSuffix;

        meta.appendChild(strong);
        meta.appendChild(span);

        const body = document.createElement('div');
        body.className = 's13-msg-text';
        // Bắt buộc dùng innerText để chống XSS theo RULES.md
        body.innerText = text;

        bubble.appendChild(meta);
        bubble.appendChild(body);

        row.appendChild(avatar);
        row.appendChild(bubble);
        viewport.appendChild(row);
        viewport.scrollTop = viewport.scrollHeight;
    }

    // 1. Gửi câu hỏi thông qua gợi ý nhanh
    document.querySelectorAll('.js-quick-prompt').forEach(chip => {
        chip.addEventListener('click', () => {
            const promptText = chip.getAttribute('data-prompt') || '';
            if (messageInput && promptText) {
                messageInput.value = promptText;
                messageInput.focus();
            }
        });
    });

    // 2. Gửi câu hỏi lên S13GeminiAiAssistant/SendMessage
    formSendChat?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const text = messageInput.value.trim();
        if (!text) return;

        const sessionId = parseInt(sessionIdInput?.value || '0', 10) || null;
        appendMessageBubble('Bạn', text, false, 'Đang gửi...');
        messageInput.value = '';

        const res = await apiFetch('/S13GeminiAiAssistant/SendMessage', {
            method: 'POST',
            body: JSON.stringify({
                sessionId,
                message: text
            })
        }, btnSend);

        if (res.ok && res.data) {
            if (res.data.sessionId && sessionIdInput) {
                sessionIdInput.value = res.data.sessionId;
            }
            const metaText = `${res.data.sourceMode || 'AI'} • ${res.data.promptTokens || 0} tokens`;
            appendMessageBubble('Trợ lý AI BookShare', res.data.replyText || '', true, metaText);
        }
    });

    // 3. Tạo phiên hội thoại mới
    formCreateSession?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const titleInput = document.getElementById('newSessionTitle');
        const title = titleInput?.value.trim() || '';
        if (!title) {
            showSystemStatus(400, 'Thiếu tiêu đề phiên chat', 'Vui lòng nhập tên chủ đề hội thoại mới.');
            return;
        }

        const res = await apiFetch('/S13GeminiAiAssistant/CreateSession', {
            method: 'POST',
            body: JSON.stringify({ title })
        }, btnCreateSession);

        if (res.ok && res.data?.sessionId) {
            setTimeout(() => {
                window.location.href = `/S13GeminiAiAssistant?sessionId=${res.data.sessionId}`;
            }, 450);
        }
    });
});
