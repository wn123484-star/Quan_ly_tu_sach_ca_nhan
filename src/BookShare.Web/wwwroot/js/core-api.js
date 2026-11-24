/**
 * BOOKSHARE CORE API & UI HELPER (wwwroot/js/core-api.js)
 * - Tự động đính kèm AntiForgeryToken (CSRF) vào mọi request POST/PUT/DELETE
 * - Cập nhật thanh trạng thái #systemStatusBox (Mã 200, 300, 400, 404, 500) & Toast SweetAlert2
 * - Spinner & khóa nút khi chờ API, Skeleton Loading, Dark Mode, ảnh Lazy Blur-up, Hover 3D, Bảng dạng Thẻ trên Mobile
 * - Chuông thông báo Polling 30 giây, Hiệu ứng chúc mừng, Trợ năng (a11y)
 * - Điều khiển hộp thoại Trợ lý AI nổi ở góc phải dưới màn hình (Markdown + "AI đang gõ...")
 */

// 100% thư viện nội bộ tự host trong wwwroot/vendor/ (0 phụ thuộc CDN ngoài)
export const CDN = {
    confetti: '/vendor/confetti/confetti.browser.min.js',
    html5Qrcode: '/vendor/qr/html5-qrcode.min.js',
    qrcode: '/vendor/qr/qrcode.min.js',
    jspdf: '/vendor/jspdf/jspdf.umd.min.js',
    html2canvas: '/vendor/html2canvas/html2canvas.min.js'
};

const loadedScripts = new Map();
export function loadScript(src) {
    if (!loadedScripts.has(src)) {
        loadedScripts.set(src, new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = src;
            s.async = true;
            s.crossOrigin = 'anonymous';
            s.onload = () => resolve();
            s.onerror = () => {
                loadedScripts.delete(src);
                reject(new Error(`Không tải được thư viện: ${src}`));
            };
            document.head.appendChild(s);
        }));
    }
    return loadedScripts.get(src);
}

export function getCsrfToken() {
    const meta = document.querySelector('meta[name="csrf-token"]');
    if (meta && meta.getAttribute('content')) {
        return meta.getAttribute('content');
    }
    const input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
}

// [BOT-GUARD] Lấy token Google reCAPTCHA v3 chạy ngầm (Smart Bypass nếu chưa bật SiteKey)
export async function getRecaptchaToken(action = 'submit') {
    const siteKey = document.querySelector('meta[name="recaptcha-site-key"]')?.getAttribute('content')?.trim();
    if (!siteKey || typeof window.grecaptcha === 'undefined') {
        return '';
    }
    try {
        return await new Promise((resolve) => {
            window.grecaptcha.ready(() => {
                window.grecaptcha.execute(siteKey, { action })
                    .then((token) => resolve(token || ''))
                    .catch(() => resolve(''));
            });
        });
    } catch {
        return '';
    }
}

// ---------- Toast & Hộp thoại xác nhận (SweetAlert2, thay alert()/confirm() của trình duyệt) ----------

function swalThemeOptions() {
    const css = getComputedStyle(document.documentElement);
    return {
        background: css.getPropertyValue('--portal-surface').trim() || '#fff',
        color: css.getPropertyValue('--portal-text').trim() || '#1e293b'
    };
}

export function toast(code, title, detail) {
    if (typeof window.Swal === 'undefined') return;
    const icon = code >= 500 ? 'error' : code >= 400 ? 'warning' : code >= 300 ? 'info' : 'success';
    window.Swal.fire({
        toast: true,
        position: 'bottom-end',
        icon,
        title,
        text: detail || '',
        showConfirmButton: false,
        showCloseButton: true,
        timer: code >= 400 ? 6000 : 3500,
        timerProgressBar: true,
        ...swalThemeOptions()
    });
}

/**
 * Hộp thoại xác nhận chuyên nghiệp thay cho confirm(). Trả về Promise<boolean>.
 */
export async function confirmDialog({ title, text, confirmText = 'Đồng ý', cancelText = 'Hủy', danger = true } = {}) {
    if (typeof window.Swal === 'undefined') {
        console.warn('SweetAlert2 chưa được nạp, hủy thao tác để an toàn.');
        return false;
    }
    const result = await window.Swal.fire({
        title,
        text,
        icon: danger ? 'warning' : 'question',
        showCancelButton: true,
        confirmButtonText: confirmText,
        cancelButtonText: cancelText,
        confirmButtonColor: danger ? '#dc3545' : '#2563eb',
        reverseButtons: true,
        focusCancel: danger,
        ...swalThemeOptions()
    });
    return result.isConfirmed;
}

export function showSystemStatus(code, title, detail, { silent = false } = {}) {
    const box = document.getElementById('systemStatusBox');
    const badge = document.getElementById('statusCodeBadge');
    const titleEl = document.getElementById('statusTitleText');
    const detailEl = document.getElementById('statusDetailText');

    let bannerVisibleInViewport = false;
    if (box && badge && titleEl && detailEl) {
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

        const rect = box.getBoundingClientRect();
        bannerVisibleInViewport = rect.height > 0 && rect.top >= 0 && rect.bottom <= window.innerHeight;
    }

    if (!silent && !bannerVisibleInViewport) {
        toast(code, title, detail);
    }
}

// ---------- Spinner & chống click 2 lần cho nút đang gọi API ----------

let lastTrigger = { el: null, at: 0 };
function rememberTrigger(el) {
    if (el) lastTrigger = { el, at: Date.now() };
}
document.addEventListener('click', (e) => {
    rememberTrigger(e.target.closest('button, input[type="submit"], .btn'));
}, true);
document.addEventListener('submit', (e) => {
    rememberTrigger(e.submitter || e.target.querySelector('[type="submit"]'));
}, true);

function startButtonLoading(btn) {
    if (!btn || btn.disabled || !btn.isConnected || btn.classList.contains('btn-loading')) return null;
    const original = btn.innerHTML;
    btn.disabled = true;
    btn.classList.add('btn-loading');
    btn.setAttribute('aria-busy', 'true');
    btn.insertAdjacentHTML('afterbegin', '<span class="spinner-border spinner-border-sm me-1 js-btn-spinner" aria-hidden="true"></span>');
    return () => {
        btn.innerHTML = original;
        btn.disabled = false;
        btn.classList.remove('btn-loading');
        btn.removeAttribute('aria-busy');
    };
}

export async function apiFetch(url, options = {}) {
    const method = (options.method || 'GET').toUpperCase();
    const headers = new Headers(options.headers || {});

    // Nút vừa bấm (trong 1 giây) sẽ hiện spinner và bị khóa tới khi Backend phản hồi
    const triggerBtn = options.button !== undefined
        ? options.button
        : (Date.now() - lastTrigger.at < 1000 ? lastTrigger.el : null);
    const stopLoading = startButtonLoading(triggerBtn);

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
    } finally {
        if (stopLoading) stopLoading();
    }
}

// ---------- Markdown an toàn cho câu trả lời AI (escape HTML trước, chống XSS) ----------

function escapeHtml(text) {
    return String(text ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function inlineMarkdown(line) {
    return line
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>');
}

export function renderMarkdown(text) {
    const lines = escapeHtml(text).replace(/\r\n/g, '\n').split('\n');
    const html = [];
    let list = null;
    let paragraph = [];

    const flushParagraph = () => {
        if (paragraph.length) {
            html.push(`<p>${paragraph.map(inlineMarkdown).join('<br>')}</p>`);
            paragraph = [];
        }
    };
    const closeList = () => {
        if (list) {
            html.push(`</${list}>`);
            list = null;
        }
    };

    for (const raw of lines) {
        const line = raw.trim();
        const bullet = line.match(/^[-*•]\s+(.*)$/);
        const ordered = line.match(/^\d+[.)]\s+(.*)$/);
        const heading = line.match(/^#{1,6}\s+(.*)$/);

        if (bullet || ordered) {
            flushParagraph();
            const type = bullet ? 'ul' : 'ol';
            if (list !== type) {
                closeList();
                html.push(`<${type}>`);
                list = type;
            }
            html.push(`<li>${inlineMarkdown((bullet || ordered)[1])}</li>`);
        } else if (heading) {
            flushParagraph();
            closeList();
            html.push(`<p><strong>${inlineMarkdown(heading[1])}</strong></p>`);
        } else if (!line) {
            flushParagraph();
            closeList();
        } else {
            closeList();
            paragraph.push(line);
        }
    }
    flushParagraph();
    closeList();
    return html.join('');
}

export function createTypingIndicator(label = 'AI đang gõ') {
    const el = document.createElement('div');
    el.className = 'ai-msg ai-msg-bot';
    el.setAttribute('aria-label', `${label}...`);
    el.innerHTML = `<span class="ai-typing" aria-hidden="true"><span></span><span></span><span></span></span><span class="visually-hidden">${label}...</span>`;
    return el;
}

// ---------- Hiệu ứng chúc mừng (canvas-confetti) ----------

export async function celebrate() {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    try {
        await loadScript(CDN.confetti);
        const fire = (ratio, opts) => window.confetti({ origin: { y: 0.7 }, particleCount: Math.floor(220 * ratio), ...opts });
        fire(0.25, { spread: 26, startVelocity: 55 });
        fire(0.2, { spread: 60 });
        fire(0.35, { spread: 100, decay: 0.91, scalar: 0.8 });
        fire(0.1, { spread: 120, startVelocity: 25, decay: 0.92, scalar: 1.2 });
        fire(0.1, { spread: 120, startVelocity: 45 });
    } catch {
        // Không có mạng tới CDN thì bỏ qua hiệu ứng, không ảnh hưởng nghiệp vụ
    }
}

// ---------- Huy hiệu TrustScore & chúc mừng khi lên hạng ----------

const TRUST_TIERS = ['Bronze', 'Silver', 'Gold', 'Platinum'];
export function trustTier(score) {
    return score >= 150 ? 'Platinum' : score >= 130 ? 'Gold' : score >= 110 ? 'Silver' : 'Bronze';
}

function initTrustTierCelebration() {
    // Ưu tiên điểm mới nhất từ CSDL ở trang S15, sau đó tới huy hiệu trên header
    const source = document.querySelector('.s15-my-score[data-trust-score]') || document.getElementById('headerTrustTier');
    if (!source) return;
    const score = parseInt(source.getAttribute('data-trust-score') || '0', 10);
    const userId = source.getAttribute('data-user-id') || 'guest';
    const tier = trustTier(score);

    // Đồng bộ huy hiệu trên header với điểm mới nhất
    const headerBadge = document.getElementById('headerTrustTier');
    if (headerBadge && source !== headerBadge) {
        headerBadge.className = `trust-tier trust-tier-${tier.toLowerCase()}`;
        headerBadge.innerHTML = `<i class="bi bi-gem" aria-hidden="true"></i>${tier}`;
        headerBadge.setAttribute('aria-label', `Hạng uy tín ${tier}`);
        headerBadge.title = `Hạng uy tín ${tier} (TrustScore ${score})`;
    }

    const key = `bookshare_trust_tier_${userId}`;
    let previous = null;
    try { previous = localStorage.getItem(key); } catch { /* bỏ qua */ }
    if (previous && TRUST_TIERS.indexOf(tier) > TRUST_TIERS.indexOf(previous)) {
        celebrate();
        toast(200, `Chúc mừng! Bạn đã lên hạng ${tier} 🎉`, `TrustScore hiện tại: ${score} điểm.`);
    }
    try { localStorage.setItem(key, tier); } catch { /* bỏ qua */ }
}

// ---------- Vẽ mã QR chuẩn ISO & Máy quét Camera Đa Năng (Tự host trong /vendor/qr/) ----------

export async function renderQrCode(containerEl, payloadText, size = 220) {
    if (!containerEl || !payloadText) return;
    await loadScript(CDN.qrcode);

    const safeSize = Math.max(140, Number(size) || 220);
    const quietZone = 12;

    // Vẽ vào phần tử tạm ngoài DOM để ngắt hoàn toàn img.onload bất đồng bộ của qrcodejs
    // (tránh lỗi kinh điển: mã QR hiện 1 giây rồi img.onload ẩn canvas thành ô trắng).
    const offscreen = document.createElement('div');
    offscreen.style.position = 'fixed';
    offscreen.style.left = '-9999px';
    offscreen.style.top = '-9999px';
    offscreen.style.visibility = 'hidden';
    document.body.appendChild(offscreen);

    try {
        new window.QRCode(offscreen, {
            text: String(payloadText),
            width: safeSize,
            height: safeSize,
            colorDark: '#0f172a',
            colorLight: '#ffffff',
            correctLevel: window.QRCode.CorrectLevel.M
        });

        offscreen.querySelectorAll('img').forEach(img => {
            img.onload = null;
            img.onerror = null;
            img.src = '';
            img.remove();
        });

        const srcCanvas = offscreen.querySelector('canvas');
        containerEl.innerHTML = '';

        if (srcCanvas && srcCanvas.width > 0 && srcCanvas.height > 0) {
            const ownedCanvas = document.createElement('canvas');
            ownedCanvas.width = safeSize + quietZone * 2;
            ownedCanvas.height = safeSize + quietZone * 2;
            ownedCanvas.setAttribute('role', 'img');
            ownedCanvas.setAttribute('aria-label', 'Mã QR xác nhận BookShare');
            ownedCanvas.style.display = 'block';
            ownedCanvas.style.margin = '0 auto';
            ownedCanvas.style.width = `${safeSize}px`;
            ownedCanvas.style.maxWidth = '100%';
            ownedCanvas.style.height = 'auto';
            ownedCanvas.style.borderRadius = '10px';
            ownedCanvas.style.background = '#ffffff';

            const ctx = ownedCanvas.getContext('2d');
            if (ctx) {
                ctx.fillStyle = '#ffffff';
                ctx.fillRect(0, 0, ownedCanvas.width, ownedCanvas.height);
                ctx.imageSmoothingEnabled = false;
                ctx.drawImage(srcCanvas, quietZone, quietZone, safeSize, safeSize);
            }
            containerEl.appendChild(ownedCanvas);
        } else {
            const fallbackTable = offscreen.querySelector('table');
            if (fallbackTable) {
                fallbackTable.style.margin = '0 auto';
                containerEl.appendChild(fallbackTable);
            }
        }
    } finally {
        offscreen.remove();
    }
}

/**
 * Hộp thoại quét QR / Mã vạch bằng Camera (Bật/Tắt Camera, Đổi trước/sau, Đèn Flash, Chọn ảnh từ máy).
 */
export async function openBarcodeScanner({ title = 'Quét Mã QR / Mã Vạch Bằng Camera', hint = 'Đưa mã QR hoặc mã vạch vào khung hình' } = {}) {
    if (typeof window.Swal === 'undefined') return null;
    try {
        await loadScript(CDN.html5Qrcode);
    } catch (err) {
        showSystemStatus(500, 'Không tải được thư viện quét mã (Mã 500)', err.message);
        return null;
    }

    const F = window.Html5QrcodeSupportedFormats;
    let scanner = null;
    let scannedText = null;
    let cameras = [];
    let camIdx = 0;
    let facingMode = 'environment';
    let torchOn = false;
    let cameraActive = false;

    const stop = async () => {
        if (cameraActive && scanner?.isScanning) {
            try { await scanner.stop(); } catch { /* camera đã tắt */ }
        }
        cameraActive = false;
        try { scanner?.clear(); } catch { /* bỏ qua */ }
    };

    const startCamera = async () => {
        if (!scanner) return;
        const statusEl = document.getElementById('bsScannerStateMsg');
        try {
            if (!cameras.length && window.Html5Qrcode.getCameras) {
                try { cameras = await window.Html5Qrcode.getCameras(); } catch { cameras = []; }
            }
            const cameraConfig = cameras.length > 0 && cameras[camIdx]?.id
                ? cameras[camIdx].id
                : { facingMode };
            await scanner.start(
                cameraConfig,
                { fps: 12, qrbox: (w, h) => ({ width: Math.min(280, Math.max(160, w * 0.8)), height: Math.min(200, Math.max(140, h * 0.65)) }) },
                (decodedText) => {
                    scannedText = decodedText.trim();
                    window.BookShareAndroidBridge?.vibrateShort?.();
                    window.Swal.close();
                },
                () => { /* tiếp tục quét khung hình */ }
            );
            cameraActive = true;
            if (statusEl) statusEl.innerText = 'Camera đang hoạt động. Hướng vào mã QR hoặc mã vạch.';
        } catch (err) {
            cameraActive = false;
            if (statusEl) statusEl.innerText = `Chưa bật được camera (${err?.message || 'kiểm tra quyền'}). Bạn có thể chọn ảnh từ máy hoặc nhập mã bên dưới.`;
        }
    };

    await window.Swal.fire({
        title,
        html: `
            <div id="bsBarcodeReader" style="width:100%;min-height:220px;border-radius:12px;overflow:hidden;background:#f1f5f9;border:1px solid #cbd5e1"></div>
            <div id="bsScannerStateMsg" class="small text-muted mt-2">${hint}</div>
            <div class="d-flex flex-wrap justify-content-center gap-2 mt-2">
                <button type="button" id="bsBtnToggleCam" class="btn btn-sm btn-outline-primary"><i class="bi bi-camera-video me-1"></i>Bật/Tắt Camera</button>
                <button type="button" id="bsBtnSwitchCam" class="btn btn-sm btn-outline-secondary"><i class="bi bi-arrow-repeat me-1"></i>Đổi Camera</button>
                <button type="button" id="bsBtnFlashCam" class="btn btn-sm btn-outline-warning text-dark"><i class="bi bi-lightning-charge me-1"></i>Đèn Flash</button>
                <label class="btn btn-sm btn-outline-success mb-0" style="cursor:pointer;">
                    <i class="bi bi-image me-1"></i>Chọn ảnh
                    <input type="file" id="bsQrFileInput" accept="image/*" class="d-none" />
                </label>
            </div>
            <div class="input-group input-group-sm mt-3">
                <input type="text" id="bsManualCodeInput" class="form-control" placeholder="Hoặc dán / nhập mã QR, mã ISBN, mã xác nhận..." />
                <button type="button" id="bsBtnManualSubmit" class="btn btn-primary fw-bold">Xác nhận mã</button>
            </div>`,
        showCancelButton: true,
        showConfirmButton: false,
        cancelButtonText: 'Đóng',
        ...swalThemeOptions(),
        didOpen: async () => {
            scanner = new window.Html5Qrcode('bsBarcodeReader', {
                formatsToSupport: [F.QR_CODE, F.EAN_13, F.EAN_8, F.UPC_A, F.UPC_E, F.CODE_128, F.CODE_39],
                verbose: false
            });

            await startCamera();

            document.getElementById('bsBtnToggleCam')?.addEventListener('click', async () => {
                const statusEl = document.getElementById('bsScannerStateMsg');
                if (cameraActive) {
                    await stop();
                    if (statusEl) statusEl.innerText = 'Đã tạm tắt camera.';
                } else {
                    await startCamera();
                }
            });

            document.getElementById('bsBtnSwitchCam')?.addEventListener('click', async () => {
                await stop();
                if (cameras.length > 1) {
                    camIdx = (camIdx + 1) % cameras.length;
                } else {
                    facingMode = facingMode === 'environment' ? 'user' : 'environment';
                }
                await startCamera();
            });

            document.getElementById('bsBtnFlashCam')?.addEventListener('click', async () => {
                const statusEl = document.getElementById('bsScannerStateMsg');
                try {
                    const caps = scanner.getRunningTrackCapabilities?.();
                    if (!caps?.torch) {
                        if (statusEl) statusEl.innerText = 'Thiết bị này không hỗ trợ bật đèn Flash trên trình duyệt.';
                        return;
                    }
                    torchOn = !torchOn;
                    await scanner.applyVideoConstraints({ advanced: [{ torch: torchOn }] });
                } catch {
                    if (statusEl) statusEl.innerText = 'Không bật được đèn Flash trên thiết bị hiện tại.';
                }
            });

            document.getElementById('bsQrFileInput')?.addEventListener('change', async (ev) => {
                const file = ev.target.files?.[0];
                if (!file) return;
                const statusEl = document.getElementById('bsScannerStateMsg');
                try {
                    await stop();
                    const decoded = await scanner.scanFile(file, true);
                    scannedText = String(decoded || '').trim();
                    window.BookShareAndroidBridge?.vibrateShort?.();
                    window.Swal.close();
                } catch {
                    if (statusEl) statusEl.innerText = 'Không đọc được mã QR hoặc mã vạch từ ảnh đã chọn.';
                }
            });

            document.getElementById('bsBtnManualSubmit')?.addEventListener('click', () => {
                const val = document.getElementById('bsManualCodeInput')?.value.trim() || '';
                if (!val) return;
                scannedText = val;
                window.Swal.close();
            });
        },
        willClose: stop
    });

    await stop();
    return scannedText;
}

export async function showHandoverQrModal({ loanRequestId, handoverType = 'Handover' } = {}) {
    const res = await apiFetch('/api/handover-qr/create', {
        method: 'POST',
        body: { loanRequestId, handoverType }
    });
    if (!res.ok || !res.data?.data) return;
    const data = res.data.data;

    await window.Swal.fire({
        title: handoverType === 'Handover' ? 'Mã QR Bàn Giao Nhận Sách' : 'Mã QR Xác Nhận Hoàn Trả Sách',
        html: `
            <p class="small text-muted mb-2">Đưa mã QR này cho bạn đọc quét (hiệu lực <strong>10 phút</strong>, chỉ dùng 1 lần):</p>
            <div id="bsHandoverQrBox" class="p-3 bg-white border rounded-4 d-inline-block my-2"></div>
            <div class="mt-2 small">Mã xác nhận dự phòng: <strong class="fs-5 text-primary font-monospace">${escapeHtml(data.shortCode)}</strong></div>
            <div class="mt-1 small text-muted text-break">Mã định danh: <code>${escapeHtml(data.qrPayload)}</code></div>`,
        confirmButtonText: 'Đóng',
        confirmButtonColor: '#2563eb',
        ...swalThemeOptions(),
        didOpen: async () => {
            const box = document.getElementById('bsHandoverQrBox');
            await renderQrCode(box, data.qrPayload, 200);
        }
    });
}

export async function confirmHandoverQrModal({ loanRequestId = null, returnCondition = 'Good' } = {}) {
    const code = await openBarcodeScanner({
        title: 'Quét Mã QR Bàn Giao / Hoàn Trả Sách',
        hint: 'Quét mã QR hoặc nhập mã xác nhận dự phòng gồm 6 ký tự'
    });
    if (!code) return false;

    const isFullQr = code.toUpperCase().startsWith('BOOKSHARE-HANDOVER:') || code.length > 16;
    const payload = isFullQr
        ? { qrPayload: code, loanRequestId, returnCondition }
        : { shortCode: code, loanRequestId, returnCondition };

    const res = await apiFetch('/api/handover-qr/confirm', {
        method: 'POST',
        body: payload
    });
    if (res.ok) {
        setTimeout(() => window.location.reload(), 700);
        return true;
    }
    return false;
}

export async function openUniversalScanner() {
    const code = await openBarcodeScanner({
        title: 'Máy Quét QR & Mã Vạch Đa Năng',
        hint: 'Quét mã đăng nhập nhanh, mã bàn giao nhận sách hoặc mã vạch sách'
    });
    if (!code) return false;

    const trimmed = code.trim();
    const upper = trimmed.toUpperCase();

    if (upper.startsWith('BOOKSHARE-QRLOGIN:')) {
        const preview = await apiFetch('/api/qr-login/preview', {
            method: 'POST',
            body: { qrPayload: trimmed }
        });
        if (!preview.ok || !preview.data?.data) {
            if (preview.status === 401) {
                toast(401, 'Cần đăng nhập trước', 'Hãy đăng nhập trên thiết bị này trước khi quét mã phê duyệt thiết bị khác.');
            }
            return false;
        }
        const info = preview.data.data;
        const confirmed = await confirmDialog(
            'Xác nhận đăng nhập thiết bị mới?',
            `Thiết bị: ${info.deviceInfo} • Địa chỉ mạng: ${info.ipAddress} (còn ${info.remainingSeconds} giây).`,
            'Phê duyệt đăng nhập'
        );
        if (!confirmed) {
            await apiFetch('/api/qr-login/approve', {
                method: 'POST',
                body: { sessionId: info.sessionId, approve: false }
            });
            return false;
        }
        const approve = await apiFetch('/api/qr-login/approve', {
            method: 'POST',
            body: { sessionId: info.sessionId, approve: true }
        });
        return Boolean(approve.ok);
    }

    if (upper.startsWith('BOOKSHARE-MOBILELINK:')) {
        const claim = await apiFetch('/api/qr-login/mobile-claim', {
            method: 'POST',
            body: { qrPayload: trimmed }
        });
        if (claim.ok) {
            setTimeout(() => { window.location.href = claim.data?.redirectUrl || '/'; }, 600);
            return true;
        }
        return false;
    }

    if (upper.startsWith('BOOKSHARE-HANDOVER:')) {
        const res = await apiFetch('/api/handover-qr/confirm', {
            method: 'POST',
            body: { qrPayload: trimmed, returnCondition: 'Good' }
        });
        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
            return true;
        }
        return false;
    }

    if (upper.startsWith('BOOKSHARE-PAY:') || upper.startsWith('BOOKSHARE:PAY:') || /^#?TX-\d+$/i.test(trimmed)) {
        return await openQrInvoiceConfirmDialog(trimmed);
    }

    window.location.href = `/S06SearchDiscovery?q=${encodeURIComponent(trimmed)}`;
    return true;
}

// ---------- Quét QR Thanh Toán Tiền Ảo Giống Ứng Dụng Ngân Hàng (Đồng bộ Web <-> Android Web App) ----------

function formatVndVirtual(val) {
    return `${Math.round(Number(val) || 0).toLocaleString('vi-VN')}đ ảo`;
}

export async function openQrInvoiceConfirmDialog(qrPayloadOrTxId) {
    const raw = String(qrPayloadOrTxId ?? '').trim();
    if (!raw) return false;

    const preview = await apiFetch('/S17SandboxPayment/PreviewQrInvoice', {
        method: 'POST',
        body: { qrPayload: raw }
    });

    if (!preview.ok || !preview.data?.invoice) {
        if (preview.status === 401) {
            toast(401, 'Cần đăng nhập', 'Vui lòng đăng nhập tài khoản trước khi quét QR thanh toán hóa đơn.');
        }
        return false;
    }

    const inv = preview.data.invoice;
    const enough = Boolean(inv.hasSufficientBalance);
    const balanceStatusHtml = enough
        ? `<div class="p-2 rounded-3 bg-success-subtle text-success small fw-semibold"><i class="bi bi-check-circle-fill me-1"></i>Số dư Ví Tiền Ảo đủ thanh toán • Còn lại dự kiến: <strong>${escapeHtml(formatVndVirtual(inv.balanceAfterPayment))}</strong></div>`
        : `<div class="p-2 rounded-3 bg-warning-subtle text-warning-emphasis small fw-semibold"><i class="bi bi-exclamation-triangle-fill me-1"></i>Số dư chưa đủ (còn thiếu <strong>${escapeHtml(formatVndVirtual(inv.shortfallAmount))}</strong>). Hãy nạp thêm tiền ảo miễn phí!</div>`;

    const result = await window.Swal.fire({
        title: 'Hóa Đơn Thanh Toán Tiền Ảo',
        html: `
            <div class="text-start border rounded-4 p-3 bg-body-tertiary">
                <div class="d-flex justify-content-between align-items-center mb-2 pb-2 border-bottom">
                    <span class="badge bg-primary rounded-pill">${escapeHtml(inv.invoiceCode)}</span>
                    <span class="small text-muted">${escapeHtml(inv.serviceLabel)}</span>
                </div>
                <div class="text-center py-2 mb-2">
                    <div class="small text-muted">Số tiền cần thanh toán (100% tiền ảo)</div>
                    <div class="fs-2 fw-bold text-primary">${escapeHtml(formatVndVirtual(inv.amount))}</div>
                </div>
                <div class="small d-flex flex-column gap-1 mb-3">
                    <div class="d-flex justify-content-between gap-2">
                        <span class="text-muted">Nội dung:</span>
                        <strong class="text-end">${escapeHtml(inv.description)}</strong>
                    </div>
                    <div class="d-flex justify-content-between gap-2">
                        <span class="text-muted">Tài khoản:</span>
                        <strong>${escapeHtml(inv.ownerFullName)}</strong>
                    </div>
                    <div class="d-flex justify-content-between gap-2">
                        <span class="text-muted">Số dư Ví hiện tại:</span>
                        <strong class="text-success">${escapeHtml(formatVndVirtual(inv.walletBalance))}</strong>
                    </div>
                </div>
                ${balanceStatusHtml}
            </div>`,
        showCancelButton: true,
        confirmButtonText: enough ? 'Xác Nhận Thanh Toán' : 'Đi Nạp Tiền Ảo Miễn Phí',
        cancelButtonText: 'Hủy',
        confirmButtonColor: enough ? '#16a34a' : '#2563eb',
        reverseButtons: true,
        ...swalThemeOptions()
    });

    if (!result.isConfirmed) return false;

    if (!enough) {
        window.location.href = `/S17SandboxPayment?txId=${encodeURIComponent(inv.transactionId)}#walletTopUpCard`;
        return false;
    }

    const confirmRes = await apiFetch('/S17SandboxPayment/ConfirmQrInvoice', {
        method: 'POST',
        body: { transactionId: inv.transactionId }
    });

    if (confirmRes.ok) {
        celebrate();
        toast(200, 'Thanh toán QR thành công!', confirmRes.data?.message || `Đã thanh toán hóa đơn ${inv.invoiceCode}.`);
        setTimeout(() => window.location.reload(), 900);
        return true;
    }
    return false;
}

export async function openQrPaymentScanner() {
    const code = await openBarcodeScanner({
        title: 'Quét Mã QR Thanh Toán',
        hint: 'Hướng camera vào mã QR hóa đơn BookShare (hoặc chọn ảnh QR / nhập mã #TX-...)'
    });
    if (!code) return false;
    return await openQrInvoiceConfirmDialog(code);
}

export async function showQrPaymentInvoiceModal({ transactionId, amount = 0, description = '', qrPayload = '' } = {}) {
    if (!transactionId) return false;
    const payload = qrPayload || `BOOKSHARE-PAY:${transactionId}`;
    let pollTimer = null;
    let paid = false;

    const stopPoll = () => {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    };

    const result = await window.Swal.fire({
        title: `Mã QR Thanh Toán #TX-${escapeHtml(transactionId)}`,
        html: `
            <p class="small text-muted mb-2">${escapeHtml(description || 'Hóa đơn thanh toán tiền ảo nội bộ BookShare')}</p>
            <div class="fs-4 fw-bold text-primary mb-2">${escapeHtml(formatVndVirtual(amount))}</div>
            <div class="d-inline-block p-3 bg-white border rounded-4 shadow-sm my-1">
                <div id="bsPayQrCanvasBox" style="min-width:220px; min-height:220px;" class="d-flex align-items-center justify-content-center">
                    <span class="small text-muted">Đang tạo mã QR...</span>
                </div>
            </div>
            <div class="mt-2 small text-muted">
                Dùng nút <strong>Quét QR Thanh Toán</strong> trên điện thoại / Web App Android để quét và xác nhận hóa đơn.
            </div>
            <div id="bsPayQrPollBadge" class="mt-2 badge bg-warning-subtle text-warning-emphasis border px-3 py-2">
                <span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Đang chờ quét &amp; xác nhận thanh toán...
            </div>`,
        showCancelButton: true,
        confirmButtonText: 'Xem Hóa Đơn & Thanh Toán Ngay',
        cancelButtonText: 'Đóng',
        confirmButtonColor: '#16a34a',
        ...swalThemeOptions(),
        didOpen: async () => {
            const box = document.getElementById('bsPayQrCanvasBox');
            await renderQrCode(box, payload, 220);

            pollTimer = setInterval(async () => {
                try {
                    const res = await fetch(`/S17SandboxPayment/InvoiceStatus?txId=${encodeURIComponent(transactionId)}`, {
                        headers: { Accept: 'application/json' },
                        credentials: 'same-origin'
                    });
                    if (!res.ok) return;
                    const st = await res.json();
                    if (st.status === 'Completed') {
                        paid = true;
                        stopPoll();
                        const badge = document.getElementById('bsPayQrPollBadge');
                        if (badge) {
                            badge.className = 'mt-2 badge bg-success text-white px-3 py-2';
                            badge.innerHTML = '<i class="bi bi-check-circle-fill me-1"></i>Đã thanh toán thành công! Đang cập nhật...';
                        }
                        celebrate();
                        setTimeout(() => {
                            window.Swal.close();
                            window.location.reload();
                        }, 1000);
                    }
                } catch { /* bỏ qua lỗi mạng tạm */ }
            }, 2500);
        },
        willClose: stopPoll
    });

    stopPoll();
    if (!paid && result.isConfirmed) {
        return await openQrInvoiceConfirmDialog(payload);
    }
    return paid;
}

// ---------- AI Copilot 2 tầng: Thực thi lệnh giao diện & Dựng thẻ Xác nhận 1 chạm (Chống XSS) ----------

export function setPortalThemeMode(mode) {
    const next = mode === 'dark' ? 'dark' : 'light';
    document.documentElement.setAttribute('data-bs-theme', next);
    try { localStorage.setItem('bookshare_theme', next); } catch { /* bỏ qua */ }
    const btn = document.getElementById('btnThemeToggle');
    const metaTheme = document.querySelector('meta[name="theme-color"]');
    if (metaTheme) metaTheme.setAttribute('content', next === 'dark' ? '#0f172a' : '#f8fafc');
    if (btn) {
        btn.setAttribute('aria-pressed', String(next === 'dark'));
        btn.setAttribute('aria-label', next === 'dark' ? 'Tắt chế độ tối' : 'Bật chế độ tối');
        btn.innerHTML = next === 'dark'
            ? '<i class="bi bi-sun-fill" aria-hidden="true"></i><span>Sáng</span>'
            : '<i class="bi bi-moon-stars-fill" aria-hidden="true"></i><span>Tối</span>';
    }
    applyChartTheme(next === 'dark');
}

export function applyClientCommands(clientCommands = []) {
    if (!Array.isArray(clientCommands)) return;
    for (const cmd of clientCommands) {
        if (cmd?.command === 'set_theme' && cmd.args?.theme) {
            setPortalThemeMode(cmd.args.theme);
        }
    }
}

export function renderCopilotExtras(containerEl, { clientCommands = [], actionCards = [] } = {}) {
    if (!containerEl) return;
    if ((!Array.isArray(clientCommands) || !clientCommands.length) && (!Array.isArray(actionCards) || !actionCards.length)) {
        return;
    }

    const wrap = document.createElement('div');
    wrap.className = 'ai-copilot-extras';

    if (Array.isArray(clientCommands) && clientCommands.length) {
        const chipRow = document.createElement('div');
        chipRow.className = 'ai-cmd-chips';
        for (const cmd of clientCommands) {
            const chipBtn = document.createElement('button');
            chipBtn.type = 'button';
            chipBtn.className = 'ai-cmd-chip';
            chipBtn.innerText = cmd.label || 'Thực hiện nhanh';
            chipBtn.addEventListener('click', () => {
                if (cmd.command === 'set_theme') {
                    setPortalThemeMode(cmd.args?.theme || 'light');
                    showSystemStatus(200, 'Đã cập nhật giao diện', `Chuyển sang chế độ ${cmd.args?.theme === 'dark' ? 'Tối' : 'Sáng'}.`);
                } else if ((cmd.command === 'navigate_to' || cmd.command === 'search_catalog') && cmd.args?.url) {
                    window.location.href = cmd.args.url;
                }
            });
            chipRow.appendChild(chipBtn);
        }
        wrap.appendChild(chipRow);
    }

    if (Array.isArray(actionCards) && actionCards.length) {
        for (const card of actionCards) {
            const cardEl = document.createElement('div');
            cardEl.className = 'ai-action-card';

            const titleEl = document.createElement('div');
            titleEl.className = 'ai-action-card-title';
            titleEl.innerText = card.title || 'Đề xuất hành động';

            const descEl = document.createElement('div');
            descEl.className = 'ai-action-card-desc';
            descEl.innerText = card.description || '';

            const confirmBtn = document.createElement('button');
            confirmBtn.type = 'button';
            confirmBtn.className = 'btn btn-primary btn-xs fw-bold js-copilot-confirm-btn';
            confirmBtn.innerText = card.confirmLabel || 'Xác nhận thực hiện';

            confirmBtn.addEventListener('click', async () => {
                const res = await apiFetch('/S13GeminiAiAssistant/ExecuteCopilotAction', {
                    method: 'POST',
                    body: {
                        actionKey: card.actionKey,
                        parameters: card.parameters || {}
                    },
                    button: confirmBtn
                });
                if (res.ok) {
                    confirmBtn.disabled = true;
                    confirmBtn.className = 'btn btn-success btn-xs fw-bold';
                    confirmBtn.innerText = 'Đã thực hiện xong';
                }
            });

            cardEl.appendChild(titleEl);
            cardEl.appendChild(descEl);
            cardEl.appendChild(confirmBtn);
            wrap.appendChild(cardEl);
        }
    }

    containerEl.appendChild(wrap);
}

// ---------- Skeleton Loading cho danh sách sách ----------

export function renderSkeleton(target, type = 'grid', count = 6) {
    if (!target) return;
    target.style.minHeight = `${target.offsetHeight}px`;
    target.setAttribute('aria-busy', 'true');
    if (type === 'table') {
        const rows = Array.from({ length: count }, () => `
            <div class="d-flex gap-3 align-items-center py-3 border-bottom">
                <div class="skeleton" style="width:44px;height:60px"></div>
                <div class="flex-grow-1">
                    <div class="skeleton skeleton-line" style="width:45%"></div>
                    <div class="skeleton skeleton-line mb-0" style="width:70%"></div>
                </div>
                <div class="skeleton d-none d-md-block" style="width:90px;height:24px"></div>
            </div>`).join('');
        target.innerHTML = `<div class="p-3">${rows}</div>`;
        return;
    }
    const cards = Array.from({ length: count }, () => `
        <div class="col-xl-4 col-md-6">
            <div class="skeleton-card">
                <div class="skeleton skeleton-line" style="width:40%"></div>
                <div class="d-flex gap-3 my-3">
                    <div class="skeleton skeleton-cover"></div>
                    <div class="flex-grow-1">
                        <div class="skeleton skeleton-line" style="width:90%"></div>
                        <div class="skeleton skeleton-line" style="width:60%"></div>
                        <div class="skeleton skeleton-line" style="width:75%"></div>
                    </div>
                </div>
                <div class="skeleton" style="height:34px"></div>
            </div>
        </div>`).join('');
    target.innerHTML = target.classList.contains('row') ? cards : `<div class="row g-4">${cards}</div>`;
    if (!target.classList.contains('row')) {
        target.classList.remove('p-5', 'text-center', 'card', 'shadow-sm', 'bg-white', 'border', 'bg-light');
    }
}

function initSkeletonNavigation() {
    const target = document.querySelector('[data-skeleton-target]');
    if (!target) return;
    const type = target.getAttribute('data-skeleton-target') || 'grid';
    const show = () => renderSkeleton(target, type);

    document.querySelectorAll('form[method="get"], form[method="GET"]').forEach(form => {
        if (form.closest('header')) return;
        form.addEventListener('submit', (e) => {
            show();
            startButtonLoading(e.submitter || form.querySelector('[type="submit"]'));
        });
    });
    document.querySelectorAll('.pagination a.page-link').forEach(link => {
        link.addEventListener('click', (e) => {
            if (link.closest('.page-item.disabled, .page-item.active') || e.ctrlKey || e.metaKey) return;
            show();
        });
    });
    // Quay lại trang bằng nút Back: trình duyệt khôi phục từ cache, bỏ skeleton cũ
    window.addEventListener('pageshow', (e) => { if (e.persisted) window.location.reload(); });
}

// ---------- Ảnh Lazy Loading + Blur-up ----------

export function initLazyImages(root = document) {
    root.querySelectorAll('img.lazy-blur:not(.is-loaded)').forEach(img => {
        const done = () => img.classList.add('is-loaded');
        if (img.complete && img.naturalWidth > 0) {
            done();
        } else {
            img.addEventListener('load', done, { once: true });
            img.addEventListener('error', done, { once: true });
        }
    });
}

// ---------- Hover 3D nghiêng theo hướng chuột ----------

export function initTilt(root = document) {
    const canHover = window.matchMedia('(hover: hover) and (pointer: fine)').matches;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!canHover || reduce) return;

    root.querySelectorAll('.tilt-3d').forEach(card => {
        if (card.dataset.tiltReady) return;
        card.dataset.tiltReady = '1';
        card.addEventListener('mousemove', (e) => {
            const rect = card.getBoundingClientRect();
            const px = (e.clientX - rect.left) / rect.width - 0.5;
            const py = (e.clientY - rect.top) / rect.height - 0.5;
            card.classList.add('is-tilting');
            card.style.setProperty('--tilt-shadow-x', `${(-px * 24).toFixed(1)}px`);
            card.style.transform = `perspective(800px) rotateY(${(px * 12).toFixed(2)}deg) rotateX(${(-py * 10).toFixed(2)}deg) translateY(-4px)`;
        });
        card.addEventListener('mouseleave', () => {
            card.classList.remove('is-tilting');
            card.style.transform = '';
        });
    });
}

// ---------- Bảng dữ liệu dạng Thẻ trên Mobile: gắn nhãn cột cho từng ô ----------

export function initMobileTables(root = document) {
    root.querySelectorAll('table.table-mobile-cards').forEach(table => {
        const labels = [...table.querySelectorAll('thead th')].map(th => th.innerText.trim());
        table.querySelectorAll('tbody tr').forEach(tr => {
            [...tr.children].forEach((td, i) => {
                if (labels[i] && !td.hasAttribute('data-label')) td.setAttribute('data-label', labels[i]);
            });
        });
    });
}

// ---------- Trợ năng (a11y) ----------

function initAccessibility() {
    // Icon trang trí không được đọc bởi trình đọc màn hình
    document.querySelectorAll('i.bi:not([aria-hidden])').forEach(i => i.setAttribute('aria-hidden', 'true'));

    // Nút / link chỉ có icon: lấy title làm nhãn
    document.querySelectorAll('button, a.btn, a[role="button"]').forEach(el => {
        if (!el.hasAttribute('aria-label') && !el.innerText.trim()) {
            const label = el.getAttribute('title') || el.getAttribute('data-bs-original-title');
            if (label) el.setAttribute('aria-label', label);
        }
    });

    // Thẻ sách có thể Tab tới, có viền sáng khi focus
    document.querySelectorAll('.book-card, .s06-copy-card, .s02-copy-card').forEach(card => {
        if (!card.hasAttribute('tabindex')) card.setAttribute('tabindex', '0');
        const title = card.querySelector('h5, h6');
        if (title && !card.hasAttribute('aria-label')) card.setAttribute('aria-label', title.innerText.trim());
    });

    // Ảnh thiếu alt
    document.querySelectorAll('img:not([alt])').forEach(img => img.setAttribute('alt', ''));
}

// ---------- Dark Mode ----------

function applyChartTheme(isDark) {
    if (typeof window.Chart === 'undefined') return;
    window.Chart.defaults.color = isDark ? '#cbd5e1' : '#64748b';
    window.Chart.defaults.borderColor = isDark ? 'rgba(148, 163, 184, 0.18)' : 'rgba(0, 0, 0, 0.1)';
    Object.values(window.Chart.instances || {}).forEach(chart => chart.update());
}

function initThemeToggle() {
    const btn = document.getElementById('btnThemeToggle');
    const metaTheme = document.querySelector('meta[name="theme-color"]');
    const render = () => {
        const isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
        if (metaTheme) {
            metaTheme.setAttribute('content', isDark ? '#0f172a' : '#f8fafc');
        }
        if (btn) {
            btn.setAttribute('aria-pressed', String(isDark));
            btn.setAttribute('aria-label', isDark ? 'Tắt chế độ tối' : 'Bật chế độ tối');
            btn.innerHTML = isDark
                ? '<i class="bi bi-sun-fill" aria-hidden="true"></i><span>Sáng</span>'
                : '<i class="bi bi-moon-stars-fill" aria-hidden="true"></i><span>Tối</span>';
        }
        applyChartTheme(isDark);
    };
    render();
    btn?.addEventListener('click', () => {
        const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-bs-theme', next);
        try { localStorage.setItem('bookshare_theme', next); } catch { /* chế độ riêng tư */ }
        render();
    });
}

// ---------- Chuông thông báo Real-time bằng Polling 30 giây (S18) ----------

function initNotificationPolling() {
    const bell = document.getElementById('notificationBell');
    if (!bell) return;
    const dot = bell.querySelector('.notif-dot');
    const storageKey = 'bookshare_unread_seen';

    const poll = async () => {
        if (document.hidden) return;
        try {
            const res = await fetch('/S18NotificationHub/UnreadCount', {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });
            if (!res.ok || !(res.headers.get('content-type') || '').includes('application/json')) return;
            const data = await res.json();
            const count = Number(data.unreadCount) || 0;

            dot.textContent = count > 99 ? '99+' : String(count);
            dot.classList.toggle('d-none', count === 0);
            bell.setAttribute('aria-label', count ? `Thông báo: ${count} chưa đọc` : 'Thông báo');

            const mobileBadge = document.getElementById('mobileUnreadBadge');
            if (mobileBadge) {
                mobileBadge.textContent = count > 99 ? '99+' : String(count);
                mobileBadge.classList.toggle('d-none', count === 0);
            }

            let seen = 0;
            try { seen = Number(sessionStorage.getItem(storageKey)) || 0; } catch { /* bỏ qua */ }
            if (count > seen) {
                bell.classList.remove('is-ringing');
                void bell.offsetWidth; // khởi động lại animation
                bell.classList.add('is-ringing');
                if (seen > 0 || data.latestTitle) {
                    toast(300, `Bạn có ${count} thông báo chưa đọc`, data.latestTitle || '');
                }
            }
            try { sessionStorage.setItem(storageKey, String(count)); } catch { /* bỏ qua */ }
        } catch {
            // Mất mạng tạm thời: lần poll sau thử lại
        }
    };

    poll();
    setInterval(poll, 30000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
}

function initMobileCompanionBridge() {
    document.getElementById('btnMobileCenterQr')?.addEventListener('click', () => {
        openUniversalScanner();
    });

    const mobileBadge = document.getElementById('mobileUnreadBadge');
    if (mobileBadge) {
        fetch('/api/mobile/session', { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
            .then(r => (r.ok ? r.json() : null))
            .then(data => {
                if (!data || !data.authenticated) return;
                const cnt = Number(data.unreadCount) || 0;
                mobileBadge.textContent = cnt > 99 ? '99+' : String(cnt);
                mobileBadge.classList.toggle('d-none', cnt === 0);
            })
            .catch(() => { /* Bỏ qua khi ngoại tuyến */ });
    }

    if (window.BookShareAndroidBridge?.openExternalAuth) {
        document.querySelectorAll('form[action*="GoogleLogin"], button.s01-google-btn').forEach(el => {
            const form = el.tagName === 'FORM' ? el : el.closest('form');
            const submitBtn = form?.querySelector('button[type="submit"], button.s01-google-btn');
            if (!form || submitBtn?.disabled || form.dataset.androidBridgeBound) return;
            form.dataset.androidBridgeBound = '1';
            form.addEventListener('submit', (e) => {
                e.preventDefault();
                window.BookShareAndroidBridge.openExternalAuth(`${window.location.origin}/S01AuthOtpGoogle/GoogleLogin`);
            });
        });
    }
}

// ---------- Khởi tạo sự kiện dùng chung trên _Layout.cshtml ----------

document.addEventListener('DOMContentLoaded', () => {
    // Chốt chặn: nếu file bị nạp 2 lần dưới 2 URL khác nhau thì chỉ khởi tạo giao diện 1 lần
    if (window.__bookshareCoreReady) return;
    window.__bookshareCoreReady = true;

    window.showSystemStatus = showSystemStatus;
    window.apiFetch = apiFetch;
    window.BookShareUI = { toast, confirmDialog, celebrate, loadScript, renderMarkdown, renderSkeleton, renderQrCode, showHandoverQrModal, confirmHandoverQrModal, openBarcodeScanner, openUniversalScanner, openQrPaymentScanner, openQrInvoiceConfirmDialog, showQrPaymentInvoiceModal, applyClientCommands, renderCopilotExtras, setPortalThemeMode, trustTier, getRecaptchaToken, CDN };

    document.addEventListener('click', (e) => {
        const qrPayBtn = e.target.closest('.js-open-qr-pay-scanner');
        if (!qrPayBtn) return;
        e.preventDefault();
        openQrPaymentScanner();
    });

    initThemeToggle();
    initAccessibility();
    initMobileTables();
    initLazyImages();
    initTilt();
    initSkeletonNavigation();
    initNotificationPolling();
    initMobileCompanionBridge();
    initTrustTierCelebration();

    // Nút đóng thanh trạng thái (tránh phát lặp Toast khi TempData flash banner đã hiển thị trực tiếp)
    const btnCloseStatus = document.getElementById('btnHideStatusBox');
    if (btnCloseStatus) {
        btnCloseStatus.addEventListener('click', () => {
            document.getElementById('systemStatusBox')?.classList.add('hidden-banner');
        });
    }

    // Chuyển đổi tông màu Portal (Blue / Coral / Emerald)
    const savedPalette = localStorage.getItem('bookshare_palette') || 'blue';
    document.body.setAttribute('data-portal-palette', savedPalette);
    const activeThemeLabelEl = document.getElementById('activePaletteLabel');
    const syncPaletteMenuState = (paletteKey) => {
        document.querySelectorAll('[data-set-palette]').forEach(btn => {
            const isMatch = btn.getAttribute('data-set-palette') === paletteKey;
            btn.classList.toggle('active', isMatch);
            if (isMatch && activeThemeLabelEl) {
                activeThemeLabelEl.innerText = btn.getAttribute('data-palette-name') || 'Giao Diện';
            }
        });
    };
    syncPaletteMenuState(savedPalette);
    document.querySelectorAll('[data-set-palette]').forEach(btn => {
        btn.addEventListener('click', () => {
            const p = btn.getAttribute('data-set-palette') || 'blue';
            const pName = btn.getAttribute('data-palette-name') || p.toUpperCase();
            document.body.setAttribute('data-portal-palette', p);
            localStorage.setItem('bookshare_palette', p);
            syncPaletteMenuState(p);
            showSystemStatus(200, 'Đã chuyển đổi chủ đề giao diện', `Đang áp dụng chủ đề: ${pName}`);
        });
    });

    // Khung Chat AI Gemini nổi góc phải
    const btnToggleChat = document.getElementById('btnToggleAIChat');
    const btnMinimizeChat = document.getElementById('btnMinimizeAIChat');
    const btnCloseChat = document.getElementById('btnCloseAIChat');
    const chatPopup = document.getElementById('aiChatPopup');
    const chatForm = document.getElementById('aiChatWidgetForm');
    const chatInput = document.getElementById('aiChatWidgetInput');
    const chatMessages = document.getElementById('aiChatWidgetMessages');

    const setChatOpen = (open) => {
        if (!chatPopup) return;
        chatPopup.classList.toggle('d-none', !open);
        btnToggleChat?.setAttribute('aria-expanded', String(open));
        if (open) chatInput?.focus();
    };
    if (btnToggleChat && chatPopup) {
        btnToggleChat.addEventListener('click', () => setChatOpen(chatPopup.classList.contains('d-none')));
    }
    if (btnMinimizeChat && chatPopup) {
        btnMinimizeChat.addEventListener('click', () => {
            setChatOpen(false);
            btnToggleChat?.focus();
        });
    }
    if (btnCloseChat && chatPopup) {
        btnCloseChat.addEventListener('click', () => {
            setChatOpen(false);
            btnToggleChat?.focus();
        });
    }
    chatPopup?.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            setChatOpen(false);
            btnToggleChat?.focus();
        }
    });

    const scrollChatToBottom = () => {
        if (chatMessages) chatMessages.scrollTo({ top: chatMessages.scrollHeight, behavior: 'smooth' });
    };

    const appendWidgetMsg = (text, isBot, extras = null) => {
        if (!chatMessages) return;
        const div = document.createElement('div');
        div.className = `ai-msg ${isBot ? 'ai-msg-bot ai-md' : 'ai-msg-user'}`;
        if (isBot) {
            div.innerHTML = renderMarkdown(text); // renderMarkdown đã escape HTML (chống XSS theo RULES.md)
            if (extras) {
                renderCopilotExtras(div, extras);
            }
        } else {
            div.innerText = text;
        }
        chatMessages.appendChild(div);
        scrollChatToBottom();
    };

    document.querySelectorAll('.ai-quick-prompt').forEach(btn => {
        btn.addEventListener('click', () => {
            if (!chatInput || !chatForm) return;
            chatInput.value = btn.getAttribute('data-prompt') || '';
            chatForm.requestSubmit();
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

            const typing = createTypingIndicator();
            chatMessages?.appendChild(typing);
            scrollChatToBottom();

            try {
                const res = await apiFetch('/api/ai-chat/quick-ask', {
                    method: 'POST',
                    body: { message: promptText }
                });
                typing.remove();
                if (res.ok && res.data && res.data.reply) {
                    applyClientCommands(res.data.clientCommands);
                    appendWidgetMsg(res.data.reply, true, {
                        clientCommands: res.data.clientCommands || [],
                        actionCards: res.data.actionCards || []
                    });
                } else {
                    appendWidgetMsg('Trợ lý AI: Bạn có thể tra cứu sách trong **Kho chung ĐH CMC** hoặc kiểm tra tiến độ đọc tại mục **Tiến Độ Đọc Sách**.', true);
                }
            } finally {
                typing.remove();
                chatInput.disabled = false;
                chatInput.focus();
            }
        });
    }
});
