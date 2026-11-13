/**
 * BOOKSHARE CORE API & UI HELPER (wwwroot/js/core-api.js)
 * - Tự động đính kèm AntiForgeryToken (CSRF) vào mọi request POST/PUT/DELETE
 * - Cập nhật thanh trạng thái #systemStatusBox (Mã 200, 300, 400, 404, 500) kế thừa book-tracker-ver07
 * - Điều khiển hộp thoại Trợ lý AI Gemini nổi ở góc phải dưới màn hình
 */

export function getCsrfToken() {
    const meta = document.querySelector('meta[name="csrf-token"]');
    if (meta && meta.getAttribute('content')) {
        return meta.getAttribute('content');
    }
    const input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
}

export function showSystemStatus(code, title, detail) {
    const box = document.getElementById('systemStatusBox');
    const badge = document.getElementById('statusCodeBadge');
    const titleEl = document.getElementById('statusTitleText');
    const detailEl = document.getElementById('statusDetailText');

    if (!box || !badge || !titleEl || !detailEl) return;

    box.classList.remove('hidden-banner', 'd-none', 'status-200', 'status-300', 'status-400', 'status-404', 'status-500');
    let cls = 'status-200';
    if (code >= 500) cls = 'status-500';
    else if (code === 404) cls = 'status-404';
    else if (code >= 400) cls = 'status-400';
    else if (code >= 300) cls = 'status-300';

    box.classList.add(cls);
    badge.innerText = `MÃ ${code}`;
    titleEl.innerText = title;
    detailEl.innerText = detail || '';
}

export async function apiFetch(url, options = {}) {
    const method = (options.method || 'GET').toUpperCase();
    const headers = new Headers(options.headers || {});

    if (!headers.has('Accept')) {
        headers.set('Accept', 'application/json');
    }

    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
        const csrfToken = getCsrfToken();
        if (csrfToken) {
            headers.set('RequestVerificationToken', csrfToken);
        }
        if (options.body && typeof options.body === 'object' && !(options.body instanceof FormData)) {
            headers.set('Content-Type', 'application/json');
            options.body = JSON.stringify(options.body);
        } else if (typeof options.body === 'string' && !headers.has('Content-Type')) {
            headers.set('Content-Type', 'application/json');
        }
    }

    try {
        const response = await fetch(url, {
            ...options,
            method,
            headers,
            credentials: 'same-origin'
        });

        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json')
            ? await response.json()
            : await response.text();

        if (!response.ok) {
            const errMsg = (payload && payload.message) ? payload.message : `Yêu cầu thất bại với mã HTTP ${response.status}`;
            showSystemStatus(response.status, `Cảnh báo phản hồi máy chủ (Mã ${response.status})`, errMsg);
            return { ok: false, status: response.status, data: payload, message: errMsg };
        }

        if (payload && payload.message) {
            showSystemStatus(response.status, payload.title || 'Thao tác thành công (Mã 200)', payload.message);
        }

        return { ok: true, status: response.status, data: payload };
    } catch (error) {
        showSystemStatus(500, 'Lỗi kết nối máy chủ (Mã 500)', error.message);
        return { ok: false, status: 500, data: null, message: error.message };
    }
}

// Khởi tạo sự kiện dùng chung trên _Layout.cshtml
document.addEventListener('DOMContentLoaded', () => {
    window.showSystemStatus = showSystemStatus;
    window.apiFetch = apiFetch;

    // Nút đóng thanh trạng thái
    const btnCloseStatus = document.getElementById('btnHideStatusBox');
    if (btnCloseStatus) {
        btnCloseStatus.addEventListener('click', () => {
            document.getElementById('systemStatusBox')?.classList.add('hidden-banner');
        });
    }

    // Chuyển đổi tông màu Portal (Blue / Coral / Emerald)
    const savedPalette = localStorage.getItem('bookshare_palette') || 'blue';
    document.body.setAttribute('data-portal-palette', savedPalette);
    document.querySelectorAll('[data-set-palette]').forEach(btn => {
        btn.addEventListener('click', () => {
            const p = btn.getAttribute('data-set-palette');
            document.body.setAttribute('data-portal-palette', p);
            localStorage.setItem('bookshare_palette', p);
            showSystemStatus(200, 'Đã đổi tông màu giao diện Portal', `Đang áp dụng bảng màu: ${p.toUpperCase()}`);
        });
    });

    // Khung Chat AI Gemini nổi góc phải
    const btnToggleChat = document.getElementById('btnToggleAIChat');
    const btnCloseChat = document.getElementById('btnCloseAIChat');
    const chatPopup = document.getElementById('aiChatPopup');
    const chatForm = document.getElementById('aiChatWidgetForm');
    const chatInput = document.getElementById('aiChatWidgetInput');
    const chatMessages = document.getElementById('aiChatWidgetMessages');

    if (btnToggleChat && chatPopup) {
        btnToggleChat.addEventListener('click', () => chatPopup.classList.toggle('d-none'));
    }
    if (btnCloseChat && chatPopup) {
        btnCloseChat.addEventListener('click', () => chatPopup.classList.add('d-none'));
    }

    const appendWidgetMsg = (text, isBot) => {
        if (!chatMessages) return;
        const div = document.createElement('div');
        div.className = `ai-msg ${isBot ? 'ai-msg-bot' : 'ai-msg-user'}`;
        div.innerText = text; // Dùng innerText chống XSS theo đúng RULES.md
        chatMessages.appendChild(div);
        chatMessages.scrollTop = chatMessages.scrollHeight;
    };

    document.querySelectorAll('.ai-quick-prompt').forEach(btn => {
        btn.addEventListener('click', () => {
            if (!chatInput || !chatForm) return;
            chatInput.value = btn.getAttribute('data-prompt') || '';
            chatForm.dispatchEvent(new Event('submit'));
        });
    });

    if (chatForm && chatInput) {
        chatForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const promptText = chatInput.value.trim();
            if (!promptText) return;

            appendWidgetMsg(promptText, false);
            chatInput.value = '';
            chatInput.disabled = true;

            try {
                const res = await apiFetch('/api/ai-chat/quick-ask', {
                    method: 'POST',
                    body: { message: promptText }
                });
                if (res.ok && res.data && res.data.reply) {
                    appendWidgetMsg(res.data.reply, true);
                } else {
                    appendWidgetMsg('Trợ lý AI: Bạn có thể tra cứu sách trong Kho chung ĐH CMC hoặc kiểm tra tiến độ đọc tại dịch vụ S03 ReadingTracker.', true);
                }
            } finally {
                chatInput.disabled = false;
                chatInput.focus();
            }
        });
    }
});
