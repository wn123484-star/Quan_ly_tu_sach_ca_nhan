import { apiFetch, showSystemStatus, renderMarkdown } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const viewport = document.getElementById('s13ChatViewport');
    const formSendChat = document.getElementById('formSendS13Chat');
    const messageInput = document.getElementById('s13MessageInput');
    const sessionIdInput = document.getElementById('s13CurrentSessionId');
    const btnSend = document.getElementById('btnSendS13Chat');
    const formCreateSession = document.getElementById('formCreateAiSession');
    const btnCreateSession = document.getElementById('btnCreateSession');

    const scrollToLatest = (smooth = true) => {
        if (viewport) viewport.scrollTo({ top: viewport.scrollHeight, behavior: smooth ? 'smooth' : 'auto' });
    };

    // Lịch sử trả lời AI do server render dạng text: chuyển sang Markdown (in đậm, danh sách...)
    document.querySelectorAll('.s13-msg-row.ai .s13-msg-text').forEach(el => {
        el.innerHTML = renderMarkdown(el.innerText);
        el.classList.add('ai-md');
    });
    scrollToLatest(false);

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
        if (isAi) {
            // renderMarkdown escape HTML trước khi dựng thẻ → vẫn chống XSS theo RULES.md
            body.classList.add('ai-md');
            body.innerHTML = renderMarkdown(text);
        } else {
            body.innerText = text;
        }

        bubble.appendChild(meta);
        bubble.appendChild(body);

        row.appendChild(avatar);
        row.appendChild(bubble);
        viewport.appendChild(row);
        scrollToLatest();
        return row;
    }

    // Hiệu ứng "AI đang gõ..." trong lúc chờ Gemini trả lời
    function showTypingIndicator() {
        const row = appendMessageBubble('Trợ lý AI BookShare', '', true, 'Đang soạn câu trả lời...');
        const body = row?.querySelector('.s13-msg-text');
        if (body) {
            body.innerHTML = '<span class="ai-typing" aria-hidden="true"><span></span><span></span><span></span></span><span class="visually-hidden">AI đang gõ...</span>';
        }
        return row;
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
        const typingRow = showTypingIndicator();

        const res = await apiFetch('/S13GeminiAiAssistant/SendMessage', {
            method: 'POST',
            body: JSON.stringify({
                sessionId,
                message: text
            }),
            button: btnSend
        });
        typingRow?.remove();

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
            body: JSON.stringify({ title }),
            button: btnCreateSession
        });

        if (res.ok && res.data?.sessionId) {
            setTimeout(() => {
                window.location.href = `/S13GeminiAiAssistant?sessionId=${res.data.sessionId}`;
            }, 450);
        }
    });
});
