/**
 * [BS-MOBILE-SUPERAPP] BOOKSHARE MOBILE SUPER-APP CONTROLLER (5 TAB — IM LẶNG 100%, KHÔNG WEB AUDIO API)
 * - Đồng bộ Cookie Auth với Web PC
 * - Tab 1: Thẻ thư viện & Sách đang thuê
 * - Tab 2: Reading Studio (Pomodoro im lặng, Voice-to-Quote, Gia hạn +7 ngày)
 * - Tab 3: Camera Quét QR đa năng (html5-qrcode): BSQR-* duyệt Web PC, ISBN, Barcode
 * - Tab 4: Ví Pay 3D (Nạp Demo thoải mái, Thanh toán QR 2 bước có PIN)
 * - Tab 5: Thông báo đẩy Web Notification, AI Copilot Hành Động & Chat Thủ thư
 */
import { loadScript, CDN, renderMarkdown } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const toastEl = document.getElementById('mappToast');
    let toastTimer = null;

    const showToast = (msg, kind = 'success') => {
        if (!toastEl) return;
        toastEl.innerText = msg;
        toastEl.classList.remove('d-none', 'is-error', 'is-success');
        toastEl.classList.add(kind === 'error' ? 'is-error' : 'is-success');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toastEl.classList.add('d-none'), 4000);
    };

    const jsonPost = async (url, body = {}) => {
        const csrf = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content') || '';
        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                ...(csrf ? { 'RequestVerificationToken': csrf } : {})
            },
            credentials: 'same-origin',
            body: JSON.stringify(body)
        });
        const data = await res.json().catch(() => ({}));
        return { ok: res.ok, status: res.status, data };
    };

    // 1. Điều hướng 5 Tab
    const switchTab = (tabName) => {
        document.querySelectorAll('.mapp-pane').forEach(p => p.classList.toggle('active', p.getAttribute('data-pane') === tabName));
        document.querySelectorAll('.mapp-nav-item').forEach(b => b.classList.toggle('active', b.getAttribute('data-tab') === tabName));
        if (tabName !== 'scan') stopCameraScanner();
        if (tabName === 'hub') loadStaffMessages();
    };

    document.querySelectorAll('.mapp-nav-item').forEach(btn => {
        btn.addEventListener('click', () => switchTab(btn.getAttribute('data-tab') || 'home'));
    });

    document.querySelectorAll('.js-mapp-open-studio').forEach(btn => {
        btn.addEventListener('click', () => {
            const bid = btn.getAttribute('data-book-id');
            if (bid) {
                const inp = document.getElementById('mappStudioBookId');
                if (inp) inp.value = bid;
            }
            switchTab('reading');
        });
    });

    // 2. Đăng nhập / Đăng xuất đồng bộ Cookie Auth với Web PC
    document.querySelectorAll('.js-mapp-demo').forEach(btn => {
        btn.addEventListener('click', () => {
            const email = btn.getAttribute('data-email') || '';
            const emEl = document.getElementById('mappEmail');
            const pwEl = document.getElementById('mappPass');
            if (emEl) emEl.value = email;
            if (pwEl) pwEl.value = 'BookShare@123';
        });
    });

    document.getElementById('mappLoginForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const email = document.getElementById('mappEmail')?.value.trim() || '';
        const password = document.getElementById('mappPass')?.value || '';
        const r = await jsonPost('/MobileApp/SignIn', { email, password });
        if (r.ok) {
            showToast(r.data.message || 'Đăng nhập thành công!');
            setTimeout(() => window.location.reload(), 650);
        } else {
            showToast(r.data.message || 'Đăng nhập thất bại.', 'error');
        }
    });

    document.getElementById('mappBtnSignOut')?.addEventListener('click', async () => {
        const r = await jsonPost('/MobileApp/SignOut', {});
        if (r.ok) {
            showToast('Đã đăng xuất an toàn.');
            setTimeout(() => window.location.reload(), 500);
        }
    });

    // 3. Theme Sáng/Tối & 7 Bảng màu Doanh Nghiệp
    const applyPalette = (p) => {
        document.body.setAttribute('data-portal-palette', p);
        try { localStorage.setItem('bookshare_palette', p); } catch { }
    };
    try { applyPalette(localStorage.getItem('bookshare_palette') || 'blue'); } catch { }

    document.getElementById('mappBtnTheme')?.addEventListener('click', () => {
        const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-bs-theme', next);
        try { localStorage.setItem('bookshare_theme', next); } catch { }
        showToast(next === 'dark' ? '🌙 Đã bật Chế độ Tối' : '☀️ Đã bật Chế độ Sáng');
    });

    document.querySelectorAll('.js-mapp-palette').forEach(btn => {
        btn.addEventListener('click', () => {
            const p = btn.getAttribute('data-palette') || 'blue';
            applyPalette(p);
            showToast(`🎨 Đã áp dụng chủ đề ${p.toUpperCase()}`);
        });
    });

    // 4. Tab Đọc Sách: Pomodoro Im Lặng + Log Session + Voice-to-Quote + Gia hạn nhanh
    let mpSeconds = 25 * 60;
    let mpTimer = null;
    const mpDigits = document.getElementById('mappPomoDigits');
    const mpState = document.getElementById('mappPomoState');
    const renderMp = () => {
        const m = String(Math.floor(mpSeconds / 60)).padStart(2, '0');
        const s = String(mpSeconds % 60).padStart(2, '0');
        if (mpDigits) mpDigits.innerText = `${m}:${s}`;
    };

    document.querySelectorAll('.js-mpomo-preset').forEach(btn => {
        btn.addEventListener('click', () => {
            clearInterval(mpTimer); mpTimer = null;
            const mins = parseInt(btn.getAttribute('data-mins') || '25', 10);
            mpSeconds = mins * 60;
            const inp = document.getElementById('mappSessMins');
            if (inp) inp.value = String(mins);
            renderMp();
        });
    });

    document.getElementById('mappPomoStart')?.addEventListener('click', () => {
        if (mpTimer) return;
        if (mpState) mpState.innerText = 'Đang tập trung đọc sách (Im lặng)...';
        mpTimer = setInterval(() => {
            mpSeconds = Math.max(0, mpSeconds - 1);
            renderMp();
            if (mpSeconds === 0) {
                clearInterval(mpTimer); mpTimer = null;
                if (mpState) mpState.innerText = '✅ Hoàn tất phiên tập trung!';
                showToast('✅ Hoàn tất phiên Pomodoro im lặng! Hãy bấm Lưu để cộng trang.');
            }
        }, 1000);
    });

    document.getElementById('mappPomoPause')?.addEventListener('click', () => {
        clearInterval(mpTimer); mpTimer = null;
        if (mpState) mpState.innerText = 'Đã tạm dừng.';
    });

    document.getElementById('mappLogSessionForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookId = parseInt(document.getElementById('mappStudioBookId')?.value || '0', 10);
        const durationMinutes = parseInt(document.getElementById('mappSessMins')?.value || '25', 10);
        const pagesRead = parseInt(document.getElementById('mappSessPages')?.value || '10', 10);
        const r = await jsonPost('/api/mobile/reading/log-session', { bookId, durationMinutes, pagesRead });
        if (r.ok) {
            showToast(r.data.message || 'Đã lưu phiên đọc!');
            const elP = document.getElementById('mappStudioPage');
            const elS = document.getElementById('mappStudioSpeed');
            const elSt = document.getElementById('mappStreakBadge');
            if (elP) elP.innerText = String(r.data.currentPage);
            if (elS) elS.innerText = `${r.data.pagesPerHour} tr/giờ`;
            if (elSt) elSt.innerText = `🔥 ${r.data.readingStreakDays} ngày`;
        } else {
            showToast(r.data.message || 'Không lưu được phiên đọc.', 'error');
        }
    });

    document.getElementById('mappBtnQuickRenew')?.addEventListener('click', async (e) => {
        const loanId = parseInt(e.currentTarget.getAttribute('data-loan-id') || '0', 10);
        const r = await jsonPost('/api/mobile/reading/quick-renew', { loanId });
        showToast(r.data.message || (r.ok ? 'Gia hạn thành công!' : 'Gia hạn thất bại.'), r.ok ? 'success' : 'error');
    });

    document.getElementById('mappBtnVoice')?.addEventListener('click', () => {
        const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SR) {
            showToast('Trình duyệt không hỗ trợ SpeechRecognition. Vui lòng nhập tay.', 'error');
            return;
        }
        const rec = new SR();
        rec.lang = 'vi-VN';
        rec.onresult = (ev) => {
            const t = ev.results?.[0]?.[0]?.transcript || '';
            const box = document.getElementById('mappVoiceText');
            if (box && t) box.value = box.value ? `${box.value} ${t}` : t;
            showToast('✅ Đã nhận diện trích dẫn giọng nói!');
        };
        rec.start();
    });

    document.getElementById('mappVoiceForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookId = parseInt(document.getElementById('mappStudioBookId')?.value || '0', 10);
        const quoteText = document.getElementById('mappVoiceText')?.value.trim() || '';
        const r = await jsonPost('/api/mobile/reading/voice-quote', { bookId, pageNumber: 1, quoteText });
        if (r.ok) {
            showToast(r.data.message || 'Đã lưu trích dẫn!');
            e.target.reset();
        } else {
            showToast(r.data.message || 'Lỗi lưu trích dẫn.', 'error');
        }
    });

    // 5. Tab Quét QR Đa Năng (BSQR-* duyệt Web PC, ISBN, Barcode)
    let qrScanner = null;
    const scanResultEl = document.getElementById('mappScanResult');

    async function stopCameraScanner() {
        if (qrScanner?.isScanning) {
            try { await qrScanner.stop(); } catch { }
        }
        document.getElementById('mappBtnStopCam')?.classList.add('d-none');
    }

    async function handleScannedCode(rawCode) {
        const code = (rawCode || '').trim();
        if (!code) return;
        if (scanResultEl) scanResultEl.innerText = `Đang xử lý mã: ${code} ...`;

        if (code.toUpperCase().startsWith('BSQR-')) {
            const r = await jsonPost('/api/mobile/qr-login/approve', { token: code });
            if (r.ok) {
                if (scanResultEl) scanResultEl.innerText = `✅ ${r.data.message}`;
                showToast(r.data.message || 'Đã duyệt đăng nhập Web PC thành công!');
            } else {
                const err = r.status === 403 ? 'Vui lòng đăng nhập trong Mobile App (Tab Trang Chủ) trước khi duyệt QR Web PC!' : (r.data.message || 'Duyệt QR thất bại.');
                if (scanResultEl) scanResultEl.innerText = `❌ ${err}`;
                showToast(err, 'error');
            }
            return;
        }

        if (/^(978|979)\d{10}$/.test(code.replace(/-/g, ''))) {
            if (scanResultEl) scanResultEl.innerText = `🏷️ Nhận diện mã ISBN ${code} -> Đang mở Biên mục ISBN...`;
            window.location.href = `/S05IsbnAutoCatalog?isbn=${encodeURIComponent(code)}`;
            return;
        }

        if (scanResultEl) scanResultEl.innerText = `📚 Nhận diện Barcode bản sao ${code} -> Đang tra cứu sách...`;
        window.location.href = `/S06SearchDiscovery?keyword=${encodeURIComponent(code)}`;
    }

    document.getElementById('mappBtnStartCam')?.addEventListener('click', async () => {
        try {
            await loadScript(CDN.html5Qrcode);
            const F = window.Html5QrcodeSupportedFormats;
            qrScanner = new window.Html5Qrcode('mappQrReader', {
                formatsToSupport: [F.QR_CODE, F.EAN_13, F.EAN_8, F.CODE_128, F.CODE_39],
                verbose: false
            });
            document.getElementById('mappBtnStopCam')?.classList.remove('d-none');
            await qrScanner.start(
                { facingMode: 'environment' },
                { fps: 10, qrbox: { width: 230, height: 230 } },
                async (decodedText) => {
                    await stopCameraScanner();
                    await handleScannedCode(decodedText);
                },
                () => { }
            );
        } catch (err) {
            showToast(`Không mở được Camera (${err?.message || err}). Bạn có thể dán mã vào ô bên dưới.`, 'error');
        }
    });

    document.getElementById('mappBtnStopCam')?.addEventListener('click', stopCameraScanner);

    document.getElementById('mappManualScanForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const code = document.getElementById('mappManualCode')?.value.trim() || '';
        await handleScannedCode(code);
    });

    // 6. Tab Ví Pay 3D (Nạp Demo thoải mái + Thanh toán QR 2 bước + PIN)
    const syncBalanceUI = (bal) => {
        const fmt = `${Number(bal || 0).toLocaleString('vi-VN')} VNĐ`;
        const elW = document.getElementById('mappWalletBal');
        const elH = document.getElementById('mappHomeBal');
        if (elW) elW.innerText = fmt;
        if (elH) elH.innerText = `${Number(bal || 0).toLocaleString('vi-VN')}đ`;
    };

    const mappTopUp = async (amount) => {
        const r = await jsonPost('/api/mobile/wallet/topup', { amount, note: 'Mobile Super-App Demo TopUp' });
        if (r.ok) {
            syncBalanceUI(r.data.balance);
            showToast(r.data.message || 'Nạp tiền thành công!');
        } else {
            showToast(r.data.message || 'Lỗi nạp tiền.', 'error');
        }
    };

    document.querySelectorAll('.js-mapp-topup').forEach(btn => {
        btn.addEventListener('click', () => mappTopUp(parseFloat(btn.getAttribute('data-amount') || '200000')));
    });

    document.getElementById('mappCustomTopUpForm')?.addEventListener('submit', (e) => {
        e.preventDefault();
        mappTopUp(parseFloat(document.getElementById('mappTopUpAmt')?.value || '0'));
    });

    const mappPayRef = document.getElementById('mappPayRef');
    if (mappPayRef && !mappPayRef.value) mappPayRef.value = `MQR-${Date.now().toString().slice(-6)}`;

    document.getElementById('mappPayQrForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const amount = parseFloat(document.getElementById('mappPayAmt')?.value || '0');
        const pin = document.getElementById('mappPayPin')?.value.trim() || '';
        const paymentRef = document.getElementById('mappPayRef')?.value.trim() || `MQR-${Date.now()}`;
        const r = await jsonPost('/api/mobile/wallet/pay-qr', { amount, pin, paymentRef, description: 'Mobile QR Pay' });
        if (r.ok) {
            syncBalanceUI(r.data.balance);
            if (mappPayRef) mappPayRef.value = `MQR-${Date.now().toString().slice(-6)}`;
            showToast(r.data.message || 'Thanh toán QR thành công!');
        } else {
            showToast(r.data.message || 'Thanh toán QR thất bại.', 'error');
        }
    });

    // 7. Tab 5: AI Copilot Hành Động & Chat Thủ Thư + Web Notification
    const aiBox = document.getElementById('mappAiChatBox');
    const staffBox = document.getElementById('mappStaffChatBox');
    const staffWrap = document.getElementById('mappStaffChatWrap');
    const aiForm = document.getElementById('mappAiChatForm');

    const btnTabAi = document.getElementById('mappChatTabAi');
    const btnTabStaff = document.getElementById('mappChatTabStaff');

    btnTabAi?.addEventListener('click', () => {
        aiBox?.classList.remove('d-none');
        aiForm?.classList.remove('d-none');
        staffWrap?.classList.add('d-none');
        btnTabAi?.classList.replace('btn-outline-secondary', 'btn-primary');
        btnTabStaff?.classList.replace('btn-primary', 'btn-outline-secondary');
    });

    btnTabStaff?.addEventListener('click', () => {
        aiBox?.classList.add('d-none');
        aiForm?.classList.add('d-none');
        staffWrap?.classList.remove('d-none');
        btnTabStaff?.classList.replace('btn-outline-secondary', 'btn-primary');
        btnTabAi?.classList.replace('btn-primary', 'btn-outline-secondary');
        loadStaffMessages();
    });

    const escHtml = (str) => String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');

    aiForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const inp = document.getElementById('mappAiInput');
        const msg = inp?.value.trim() || '';
        if (!msg) return;
        inp.value = '';
        const uDiv = document.createElement('div');
        uDiv.className = 'ai-msg ai-msg-user';
        uDiv.innerText = msg;
        aiBox?.appendChild(uDiv);

        const r = await jsonPost('/api/ai-chat/quick-ask', { message: msg });
        const bDiv = document.createElement('div');
        bDiv.className = 'ai-msg ai-msg-bot ai-md';
        bDiv.innerHTML = renderMarkdown(r.data?.reply || 'AI Copilot đã tiếp nhận yêu cầu.');
        aiBox?.appendChild(bDiv);
        if (aiBox) aiBox.scrollTop = aiBox.scrollHeight;

        const actions = r.data?.actions || [];
        for (const a of actions) {
            const t = a.type || a.Type;
            const v = a.value || a.Value;
            if (t === 'SET_DARK_MODE' && (v === 'dark' || v === 'light')) {
                document.documentElement.setAttribute('data-bs-theme', v);
                try { localStorage.setItem('bookshare_theme', v); } catch { }
            } else if (t === 'SET_THEME' && v) {
                applyPalette(v);
            } else if (t === 'NAVIGATE' && v?.startsWith('/')) {
                setTimeout(() => { window.location.href = v; }, 800);
            }
        }
    });

    async function loadStaffMessages() {
        if (!staffBox) return;
        try {
            const res = await fetch('/api/mobile/support-chat/messages?channel=Librarian', { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
            if (!res.ok) return;
            const list = await res.json();
            staffBox.innerHTML = list.length
                ? list.map(m => `<div class="ai-msg ${m.isMine ? 'ai-msg-user' : 'ai-msg-bot'}"><div style="font-size:0.68rem;opacity:0.8"><strong>${escHtml(m.senderName)}</strong></div><div>${escHtml(m.messageText)}</div></div>`).join('')
                : '<div class="small text-muted text-center">Chưa có tin nhắn. Hãy nhắn trực tiếp Thủ thư!</div>';
            staffBox.scrollTop = staffBox.scrollHeight;
        } catch { }
    }

    document.getElementById('mappStaffChatForm')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const inp = document.getElementById('mappStaffInput');
        const messageText = inp?.value.trim() || '';
        if (!messageText) return;
        inp.value = '';
        const r = await jsonPost('/api/mobile/support-chat/send', { channel: 'Librarian', messageText });
        if (r.ok) await loadStaffMessages();
    });

    document.getElementById('mappBtnNotifPerm')?.addEventListener('click', async () => {
        if (!('Notification' in window)) {
            showToast('Trình duyệt không hỗ trợ Web Notification API.', 'error');
            return;
        }
        const perm = await Notification.requestPermission();
        if (perm === 'granted') {
            new Notification('BookShare Mobile', { body: 'Đã bật thông báo đẩy nhắc hạn thuê sách & giải ngân cọc Escrow!' });
            showToast('🔔 Đã bật Thông báo đẩy Web Notification!');
        } else {
            showToast('Bạn chưa cấp quyền thông báo cho trình duyệt.', 'error');
        }
    });
});
