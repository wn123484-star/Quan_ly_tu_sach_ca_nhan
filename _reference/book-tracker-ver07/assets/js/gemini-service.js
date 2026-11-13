// =========================================================================
// TỆP: assets/js/gemini-service.js
// Mục đích: Xử lý gọi API Google Gemini, quản lý cấu hình AI và Khung Chat nổi
// =========================================================================

// [MÃ: NV-02] Đọc và lưu cấu hình AI từ bộ nhớ trình duyệt (localStorage)
const DEFAULT_AI_CONFIG = {
    apiKey: '',
    model: 'models/gemini-1.5-flash',
    temperature: 0.7,
    maxTokens: 800,
    systemPrompt: 'Bạn là trợ lý quản lý đọc sách thân thiện, trả lời ngắn gọn, rõ ràng bằng tiếng Việt.'
};

function getAIConfig() {
    try {
        const saved = JSON.parse(localStorage.getItem('aiConfig') || '{}');
        return { ...DEFAULT_AI_CONFIG, ...saved };
    } catch (e) {
        return { ...DEFAULT_AI_CONFIG };
    }
}

function saveAIConfig(newConfig) {
    const merged = { ...getAIConfig(), ...newConfig };
    localStorage.setItem('aiConfig', JSON.stringify(merged));
    return merged;
}

// Khởi tạo sẵn cấu hình EmailJS mặc định nếu người dùng chưa lưu trong máy
function getEffectiveEmailConfig() {
    const defaultEmail = {
        targetEmail: 'nguyenhung@example.com',
        publicKey: 'YOUR_DEFAULT_PUBLIC_KEY',
        serviceId: 'YOUR_DEFAULT_SERVICE_ID',
        templateId: 'YOUR_DEFAULT_TEMPLATE_ID'
    };
    try {
        const saved = JSON.parse(localStorage.getItem('emailConfig') || '{}');
        return {
            targetEmail: saved.targetEmail || defaultEmail.targetEmail,
            publicKey: saved.publicKey || defaultEmail.publicKey,
            serviceId: saved.serviceId || defaultEmail.serviceId,
            templateId: saved.templateId || defaultEmail.templateId
        };
    } catch (e) {
        return defaultEmail;
    }
}

// [MÃ: NV-02] Gọi API lấy danh sách các mô hình Gemini khả dụng
async function fetchAvailableGeminiModels(apiKey) {
    const cleanKey = (apiKey || '').trim();
    if (!cleanKey) {
        throw new Error('Vui lòng nhập Gemini API Key trước khi lấy danh sách mô hình.');
    }

    const response = await fetch(`https://generativelanguage.googleapis.com/v1beta/models?key=${cleanKey}`);
    const data = await response.json();

    if (!response.ok) {
        throw new Error(data.error?.message || `Lỗi HTTP ${response.status}`);
    }

    const models = (data.models || []).filter(model =>
        model.supportedGenerationMethods &&
        model.supportedGenerationMethods.includes('generateContent') &&
        !model.name.toLowerCase().includes('pro-vision')
    );

    localStorage.setItem('aiAvailableModels', JSON.stringify(models.map(m => ({
        name: m.name,
        displayName: m.displayName || m.name
    }))));

    return models;
}

// [MÃ: NV-02] Hàm lõi gọi Gemini sinh văn bản theo cấu hình đã lưu
async function callGeminiText(promptText, customSystemInstruction = '') {
    if (!navigator.onLine) {
        throw new Error('Máy tính đang mất kết nối mạng Internet.');
    }

    const config = getAIConfig();
    const apiKey = (config.apiKey || '').trim();
    if (!apiKey) {
        throw new Error('Chưa có Gemini API Key. Vui lòng vào trang Tùy chỉnh để nhập khóa API.');
    }

    const modelName = config.model || 'models/gemini-.5-flash';
    const url = `https://generativelanguage.googleapis.com/v1beta/${modelName}:generateContent?key=${apiKey}`;

    const requestBody = {
        contents: [{
            parts: [{ text: promptText }]
        }],
        generationConfig: {
            temperature: parseFloat(config.temperature ?? 0.7),
            maxOutputTokens: parseInt(config.maxTokens ?? 800, 10)
        }
    };

    const sysText = (customSystemInstruction || config.systemPrompt || '').trim();
    if (sysText) {
        requestBody.systemInstruction = {
            parts: [{ text: sysText }]
        };
    }

    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestBody)
    });

    const data = await response.json();
    if (!response.ok) {
        throw new Error(data.error?.message || `Lỗi máy chủ AI (${response.status})`);
    }

    const reply = data.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!reply) {
        throw new Error('AI không trả về nội dung văn bản.');
    }
    return reply.trim();
}

// [MÃ: NV-03] Hàm tạo ghi chú AI cho một cuốn sách cụ thể
async function generateBookNote(bookData, customApiKey = null) {
    if (customApiKey) {
        saveAIConfig({ apiKey: customApiKey });
    }

    const prompt = `Hãy viết một bản ghi chú đọc sách ngắn gọn (khoảng 120 - 180 từ) bằng tiếng Việt cho cuốn sách sau:
- Tên sách: "${bookData.title}"
- Tác giả: ${bookData.author}
- Trạng thái hiện tại: ${bookData.status} (Mức ưu tiên: ${bookData.priority}, Hạn đọc: ${bookData.deadline || 'Chưa đặt'})
- Cảm nhận ban đầu của người đọc: "${bookData.description || 'Chưa có'}"

Yêu cầu cấu trúc rõ ràng gồm 3 ý:
1. Tóm tắt giá trị cốt lõi của cuốn sách.
2. Đánh giá tiến độ và lời khuyên đọc tiếp theo.
3. 3 bài học thực tế rút ra từ cuốn sách này.`;

    return await callGeminiText(prompt);
}

// [MÃ: NV-04] Xử lý hội thoại thông minh trong khung Chat nổi
async function processAIChatMessage(userMessage) {
    const books = typeof getBooks === 'function' ? getBooks() : [];
    const currentTheme = localStorage.getItem('app_theme') || 'default';

    const bookSummary = books.map((b, idx) =>
        `${idx + 1}. "${b.title}" - ${b.author} [Trạng thái: ${b.status}, Ưu tiên: ${b.priority}, Hạn: ${b.deadline}]`
    ).join('\n');

    const systemContext = `Bạn là Trợ lý AI thông minh tích hợp trong ứng dụng web "Tủ Sách Cá Nhân".
Dữ liệu tủ sách hiện tại của người dùng (${books.length} cuốn):
${bookSummary || 'Tủ sách đang trống.'}

Giao diện hiện tại: ${currentTheme}.
Danh sách mã giao diện có sẵn trong hệ thống:
- "pure": Giao diện tự thiết kế tối giản, nhẹ nhàng
- "default": Giao diện Xanh tiêu chuẩn sáng sủa
- "flatly": Giao diện Phẳng thanh lịch, yên tĩnh
- "cyborg": Giao diện Ban đêm nền tối (Dark Mode - phù hợp khi buồn, mỏi mắt, đọc đêm)
- "sketchy": Giao diện Nét vẽ tay vui nhộn, sáng tạo
- "morph": Giao diện Nổi khối mềm mại, thư giãn

QUY TẮC ĐẶC BIỆT (Rất quan trọng):
1. Nếu người dùng hỏi nên đọc sách nào: Hãy dựa vào danh sách sách "Đang đọc" hoặc "Chưa đọc" có mức ưu tiên "Cao" hoặc hạn đọc gần nhất để đề xuất cụ thể.
2. Nếu người dùng chia sẻ tâm trạng hoặc yêu cầu đổi màu/giao diện web (ví dụ: dark mode, nền tối, vẽ tay...): Hãy chọn 1 mã giao diện phù hợp nhất và chèn chính xác cụm [THEME:mã_giao_diện] vào cuối câu trả lời (Ví dụ: [THEME:cyborg] hoặc [THEME:sketchy]).
3. Nếu người dùng yêu cầu viết và gửi email ngay trong chat: Hãy soạn nội dung email hoàn chỉnh và bọc nội dung cần gửi trong cặp thẻ [EMAIL_START]nội dung email[EMAIL_END] ở cuối câu trả lời để hệ thống tự động gửi qua EmailJS.`;

    const rawReply = await callGeminiText(userMessage, systemContext);
    let cleanReply = rawReply;
    let actionNote = '';

    // Kiểm tra xem AI có gắn lệnh đổi giao diện theo tâm trạng không
    const themeMatch = cleanReply.match(/\[THEME:(pure|default|flatly|cyborg|sketchy|morph)\]/i);
    if (themeMatch) {
        const targetTheme = themeMatch[1].toLowerCase();
        cleanReply = cleanReply.replace(themeMatch[0], '').trim();
        if (typeof applyTheme === 'function') {
            applyTheme(targetTheme);
            const selector = document.getElementById('themeSelector');
            if (selector) selector.value = targetTheme;
            actionNote += `\n\n✨ [Hệ thống đã tự động chuyển sang giao diện "${targetTheme}" theo yêu cầu của bạn]`;
        }
    }

    // [BẢN CẬP NHẬT CHỐNG LỖI TOKEN] Xử lý gửi thư kể cả khi bị thiếu thẻ đóng [EMAIL_END]
    // Regex mới: Bắt đầu từ [EMAIL_START], lấy mọi thứ cho đến khi gặp [EMAIL_END] HOẶC hết chuỗi ($)
    const emailMatch = cleanReply.match(/\[EMAIL_START\]([\s\S]*?)(?:\[EMAIL_END\]|$)/i);
    
    if (emailMatch) {
        const rawMatchedString = emailMatch[0]; // Toàn bộ chuỗi chứa thẻ (để xóa khỏi giao diện chat)
        let emailContent = emailMatch[1].trim();

        // Cảnh báo nếu thư bị cắt ngang do thiếu token
        if (!cleanReply.includes('[EMAIL_END]')) {
            emailContent += '\n\n... (Nội dung bị cắt ngang do AI hết giới hạn Token)';
        }

        cleanReply = cleanReply.replace(rawMatchedString, `\n\n📧 Nội dung thư đã soạn:\n"${emailContent}"`).trim();

        const emailCfg = getEffectiveEmailConfig();
        if (typeof emailjs !== 'undefined' && emailCfg.publicKey && !emailCfg.publicKey.includes('YOUR_DEFAULT')) {
            try {
                let readCount = 0, unreadCount = 0;
                books.forEach(b => {
                    if (b.status === 'Đã đọc') readCount++;
                    else unreadCount++;
                });

                // Gửi qua EmailJS
                await emailjs.send(
                    emailCfg.serviceId,
                    emailCfg.templateId,
                    {
                        to_email: emailCfg.targetEmail,
                        message: emailContent,
                        ai_generated_note: emailContent,
                        total_books: books.length,
                        read_books: readCount,
                        unread_books: unreadCount,
                        time: new Date().toLocaleString('vi-VN')
                    },
                    emailCfg.publicKey
                );
                actionNote += `\n\n✅ [Đã gửi email tự động tới ${emailCfg.targetEmail} thành công!]`;
            } catch (err) {
                console.error("Lỗi EmailJS Chat:", err);
                const loiMayChu = (err && err.text) ? err.text : (err.message || 'Lỗi mạng');
                actionNote += `\n\n⚠️ [Soạn thư xong nhưng gửi thất bại. Báo lỗi: ${loiMayChu}]`;
            }
        } else {
            actionNote += `\n\nℹ️ [Lưu ý: Bạn cần cấu hình mã Public Key, Service ID, Template ID tại trang Tùy chỉnh để gửi email]`;
        }
    }

    return cleanReply + actionNote;
}

// [MÃ: GD-21] Tự động vẽ nút bấm mở Chat và hộp thoại Chat nổi ở góc phải dưới màn hình
function initFloatingAIChatWidget() {
    if (document.getElementById('aiFloatingWidget')) return;

    const widgetHTML = `
        <div id="aiFloatingWidget" class="ai-chat-widget">
            <button type="button" id="btnToggleAIChat" class="ai-chat-fab shadow" title="Trò chuyện với Trợ lý AI">
                <i class="bi bi-robot fs-4"></i>
                <span class="ai-fab-label">Trợ lý AI</span>
            </button>

            <div id="aiChatPopup" class="ai-chat-popup shadow-lg d-none">
                <div class="ai-chat-header bg-primary text-white d-flex justify-content-between align-items-center px-3 py-2">
                    <div class="d-flex align-items-center gap-2">
                        <i class="bi bi-stars"></i>
                        <strong class="small">Trợ lý Tủ Sách AI</strong>
                    </div>
                    <div class="d-flex gap-1">
                        <a href="settings.html#aiSettingsSection" class="btn btn-sm text-white p-0 px-1" title="Cấu hình AI">
                            <i class="bi bi-gear"></i>
                        </a>
                        <button type="button" id="btnCloseAIChat" class="btn btn-sm text-white p-0 px-1" title="Đóng cửa sổ">
                            <i class="bi bi-x-lg"></i>
                        </button>
                    </div>
                </div>

                <div class="ai-chat-quick-bar px-2 py-1 border-bottom bg-body-tertiary d-flex gap-1 flex-wrap">
                    <button type="button" class="btn btn-outline-primary btn-xs ai-quick-prompt" data-prompt="Dựa vào tủ sách của tôi, hôm nay tôi nên ưu tiên đọc cuốn nào trước?">📖 Đề xuất sách</button>
                    <button type="button" class="btn btn-outline-secondary btn-xs ai-quick-prompt" data-prompt="Tôi đang muốn đọc sách ban đêm cho đỡ mỏi mắt, hãy đổi giao diện phù hợp giúp tôi.">🌙 Đổi giao diện</button>
                    <button type="button" class="btn btn-outline-success btn-xs ai-quick-prompt" data-prompt="Hãy viết một bức thư ngắn tổng kết tiến độ đọc sách hiện tại của tôi và gửi email giúp tôi.">✉️ Viết & Gửi mail</button>
                </div>

                <div id="aiChatMessages" class="ai-chat-messages p-3">
                    <div class="ai-msg ai-msg-bot">
                        Xin chào! Mình có thể gợi ý sách nên đọc tiếp theo, đổi giao diện web theo tâm trạng hoặc tự động soạn & gửi email báo cáo giúp bạn.
                    </div>
                </div>

                <form id="aiChatForm" class="ai-chat-footer border-top p-2 d-flex gap-2 bg-body">
                    <input type="text" id="aiChatInput" class="form-control form-control-sm" placeholder="Nhập câu hỏi hoặc yêu cầu..." autocomplete="off">
                    <button type="submit" id="btnSendAIChat" class="btn btn-primary btn-sm px-3">
                        <i class="bi bi-send"></i>
                    </button>
                </form>
            </div>
        </div>
    `;

    document.body.insertAdjacentHTML('beforeend', widgetHTML);

    const btnToggle = document.getElementById('btnToggleAIChat');
    const btnClose = document.getElementById('btnCloseAIChat');
    const popup = document.getElementById('aiChatPopup');
    const chatForm = document.getElementById('aiChatForm');
    const chatInput = document.getElementById('aiChatInput');
    const msgContainer = document.getElementById('aiChatMessages');
    const btnSend = document.getElementById('btnSendAIChat');

    const appendMessage = (text, sender = 'bot') => {
        const div = document.createElement('div');
        div.className = `ai-msg ${sender === 'user' ? 'ai-msg-user' : 'ai-msg-bot'}`;
        div.innerText = text;
        msgContainer.appendChild(div);
        msgContainer.scrollTop = msgContainer.scrollHeight;
        return div;
    };

    btnToggle.addEventListener('click', () => {
        popup.classList.toggle('d-none');
        if (!popup.classList.contains('d-none')) {
            chatInput.focus();
        }
    });

    btnClose.addEventListener('click', () => {
        popup.classList.add('d-none');
    });

    document.querySelectorAll('.ai-quick-prompt').forEach(btn => {
        btn.addEventListener('click', () => {
            chatInput.value = btn.getAttribute('data-prompt');
            chatForm.dispatchEvent(new Event('submit'));
        });
    });

    chatForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const text = chatInput.value.trim();
        if (!text) return;

        chatInput.value = '';
        appendMessage(text, 'user');

        btnSend.disabled = true;
        chatInput.disabled = true;
        const loadingBubble = appendMessage('Đang suy nghĩ...', 'bot');
        loadingBubble.classList.add('fst-italic', 'opacity-75');

        try {
            const reply = await processAIChatMessage(text);
            loadingBubble.classList.remove('fst-italic', 'opacity-75');
            loadingBubble.innerText = reply;
            if (typeof hienThiThongBaoNgoaiLeHTML === 'function') {
                hienThiThongBaoNgoaiLeHTML(200, 'Trợ lý AI phản hồi thành công (Mã 200)', 'Đã xử lý yêu cầu trò chuyện trực tiếp.');
            }
        } catch (err) {
            loadingBubble.classList.remove('fst-italic', 'opacity-75');
            loadingBubble.classList.add('text-danger');
            loadingBubble.innerText = `⚠️ Lỗi: ${err.message}`;
            if (typeof hienThiThongBaoNgoaiLeHTML === 'function') {
                hienThiThongBaoNgoaiLeHTML(400, 'Lỗi kết nối Trợ lý AI (Mã 400)', err.message);
            }
        } finally {
            btnSend.disabled = false;
            chatInput.disabled = false;
            chatInput.focus();
            msgContainer.scrollTop = msgContainer.scrollHeight;
        }
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initFloatingAIChatWidget();
});