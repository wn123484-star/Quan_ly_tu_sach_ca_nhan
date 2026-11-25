/**
 * [CORE-JS] THƯ VIỆN FRONTEND DÙNG CHUNG BOOKSHARE (wwwroot/js/core-api.js)
 * Nhiệm vụ chính:
 *   - apiFetch(): Wrapper gọi API tự động đính CSRF Token, parse lỗi 400/401/403/429/500 & hiện thông báo.
 *   - openBarcodeScannerModal(): Khung quét Camera QR/Barcode hình vuông, tự xin quyền và tự tắt khi đóng/ESC.
 *   - showHandoverQrModal() & confirmHandoverQrModal(): Giao nhận & hoàn trả sách qua QR ([CORE-QR-HANDOVER]).
 *   - Áp dụng đồng bộ 9 phong cách giao diện, 13 bảng màu, 12 nền trang, 5 phông chữ và 7 cỡ chữ ([S01]/[S13]).
 *   - Pháo hoa chúc mừng (celebrate) và quản lý thông báo pop-up nhanh ([S18]).
 */
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

    let useDeviceId = false;
    let isClosing = false;

    const releaseVideoTracks = () => {
        const container = document.getElementById('bsBarcodeReader');
        if (!container) return;
        container.querySelectorAll('video').forEach(video => {
            try {
                const stream = video.srcObject;
                if (stream && typeof stream.getTracks === 'function') {
                    stream.getTracks().forEach(track => {
                        try { track.stop(); } catch { /* bỏ qua */ }
                    });
                }
                video.srcObject = null;
            } catch { /* bỏ qua */ }
        });
    };

    const stop = async () => {
        if (scanner) {
            try {
                if (cameraActive || scanner.isScanning) {
                    await scanner.stop();
                }
            } catch { /* camera đã tắt */ }
            try { scanner.clear(); } catch { /* bỏ qua */ }
        }
        cameraActive = false;
        releaseVideoTracks();
    };

    const scannerConfig = {
        fps: 12,
        aspectRatio: 1.0,
        qrbox: (w, h) => {
            const side = Math.max(150, Math.min(230, Math.round(Math.min(w, h) * 0.72)));
            return { width: side, height: side };
        }
    };

    const onDecodeSuccess = (decodedText) => {
        if (isClosing) return;
        isClosing = true;
        scannedText = decodedText.trim();
        window.BookShareAndroidBridge?.vibrateShort?.();
        stop().finally(() => window.Swal.close());
    };

    const startCamera = async () => {
        if (!scanner || isClosing) return;
        const statusEl = document.getElementById('bsScannerStateMsg');
        const settingsBtn = document.getElementById('bsBtnOpenCamSettings');
        if (settingsBtn) settingsBtn.classList.add('d-none');
        if (statusEl) statusEl.innerText = 'Đang tự động bật máy ảnh...';

        try {
            // Chủ động mở trực tiếp camera sau ({ facingMode: 'environment' }) — xin quyền ngay khi cần
            const cameraTarget = (useDeviceId && cameras.length > 0 && cameras[camIdx]?.id)
                ? cameras[camIdx].id
                : { facingMode };

            try {
                await scanner.start(cameraTarget, scannerConfig, onDecodeSuccess, () => { /* tiếp tục quét khung hình */ });
            } catch (primaryErr) {
                const errName = String(primaryErr?.name || '');
                const errMsg = String(primaryErr?.message || primaryErr || '');
                const isDenied = /NotAllowed|PermissionDenied|permission|denied/i.test(`${errName} ${errMsg}`);
                if (isDenied || !window.Html5Qrcode?.getCameras) {
                    throw primaryErr;
                }
                cameras = await window.Html5Qrcode.getCameras();
                if (!cameras.length) throw primaryErr;
                const backIdx = cameras.findIndex(c => /back|rear|environment/i.test(c.label || ''));
                camIdx = backIdx >= 0 ? backIdx : 0;
                useDeviceId = true;
                await scanner.start(cameras[camIdx].id, scannerConfig, onDecodeSuccess, () => { /* tiếp tục quét khung hình */ });
            }

            if (isClosing) {
                await stop();
                return;
            }
            cameraActive = true;
            if (statusEl) statusEl.innerText = 'Camera đang hoạt động. Đưa mã QR hoặc mã vạch vào giữa khung vuông.';
        } catch (err) {
            cameraActive = false;
            releaseVideoTracks();
            const errText = String(err?.message || err || '');
            const isDenied = /NotAllowed|PermissionDenied|permission|denied/i.test(`${err?.name || ''} ${errText}`);
            if (statusEl) {
                statusEl.innerText = isDenied
                    ? 'Bạn chưa cấp quyền truy cập Máy ảnh. Hãy cấp quyền hoặc chọn ảnh QR từ thư viện / nhập mã bên dưới.'
                    : `Chưa bật được camera (${errText || 'kiểm tra quyền'}). Bạn có thể chọn ảnh từ máy hoặc nhập mã bên dưới.`;
            }
            if (isDenied && settingsBtn && typeof window.BookShareAndroidBridge?.openAppSettings === 'function') {
                settingsBtn.classList.remove('d-none');
            }
        }
    };

    const onEscKey = (ev) => {
        if (ev.key === 'Escape') {
            isClosing = true;
            stop();
        }
    };
    const onPageExit = () => {
        isClosing = true;
        releaseVideoTracks();
        stop();
    };

    document.addEventListener('keydown', onEscKey, true);
    window.addEventListener('pagehide', onPageExit);
    window.addEventListener('beforeunload', onPageExit);
    window.BookShareAndroidBridge?.setPullToRefreshEnabled?.(false);

    try {
        await window.Swal.fire({
            title,
            html: `
                <div class="d-flex justify-content-center">
                    <div id="bsBarcodeReader" class="bs-qr-scanner-stage"></div>
                </div>
                <div id="bsScannerStateMsg" class="small text-muted mt-2">${hint}</div>
                <div class="mt-2">
                    <button type="button" id="bsBtnOpenCamSettings" class="btn btn-sm btn-warning fw-bold d-none">
                        <i class="bi bi-gear-fill me-1"></i>Mở Cài Đặt Quyền Máy Ảnh
                    </button>
                </div>
                <div class="d-flex flex-wrap justify-content-center gap-2 mt-2">
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
            allowEscapeKey: true,
            ...swalThemeOptions(),
            didOpen: async () => {
                scanner = new window.Html5Qrcode('bsBarcodeReader', {
                    formatsToSupport: [F.QR_CODE, F.EAN_13, F.EAN_8, F.UPC_A, F.UPC_E, F.CODE_128, F.CODE_39],
                    verbose: false
                });

                await startCamera();

                document.getElementById('bsBtnOpenCamSettings')?.addEventListener('click', () => {
                    window.BookShareAndroidBridge?.openAppSettings?.();
                });

                document.getElementById('bsBtnSwitchCam')?.addEventListener('click', async () => {
                    await stop();
                    if (useDeviceId && cameras.length > 1) {
                        camIdx = (camIdx + 1) % cameras.length;
                    } else {
                        useDeviceId = false;
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
                        isClosing = true;
                        window.Swal.close();
                    } catch {
                        if (statusEl) statusEl.innerText = 'Không đọc được mã QR hoặc mã vạch từ ảnh đã chọn.';
                    }
                });

                document.getElementById('bsBtnManualSubmit')?.addEventListener('click', () => {
                    const val = document.getElementById('bsManualCodeInput')?.value.trim() || '';
                    if (!val) return;
                    scannedText = val;
                    isClosing = true;
                    stop().finally(() => window.Swal.close());
                });
            },
            willClose: () => {
                isClosing = true;
                return stop();
            },
            didClose: () => {
                isClosing = true;
                releaseVideoTracks();
            }
        });
    } finally {
        isClosing = true;
        document.removeEventListener('keydown', onEscKey, true);
        window.removeEventListener('pagehide', onPageExit);
        window.removeEventListener('beforeunload', onPageExit);
        await stop();
        window.BookShareAndroidBridge?.setPullToRefreshEnabled?.(true);
    }
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
        hint: 'Quét mã QR thanh toán, đăng nhập nhanh, bàn giao sách hoặc mã vạch ISBN'
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
        const confirmed = await confirmDialog({
            title: 'Xác nhận đăng nhập thiết bị mới?',
            text: `Thiết bị: ${info.deviceInfo} • Địa chỉ mạng: ${info.ipAddress} (còn ${info.remainingSeconds} giây).`,
            confirmText: 'Phê duyệt đăng nhập',
            cancelText: 'Từ chối',
            danger: false
        });
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
    const breakdownHtml = (Number(inv.switchFee) > 0 || Number(inv.prorationCredit) > 0)
        ? `
            <div class="d-flex justify-content-between gap-2 text-primary">
                <span>Lệ phí chuyển gói:</span>
                <strong>+${escapeHtml(formatVndVirtual(inv.switchFee))}</strong>
            </div>
            <div class="d-flex justify-content-between gap-2 text-success">
                <span>Khấu trừ ngày dư gói cũ:</span>
                <strong>-${escapeHtml(formatVndVirtual(inv.prorationCredit))}</strong>
            </div>`
        : '';
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
                    ${breakdownHtml}
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

// ---------- Hệ thống Đa Giao Diện Bootstrap (Style / Palette / Font / FontSize / Dark Mode) ----------

const STYLE_CSS_MAP = {
    standard: '/vendor/bootstrap/bootstrap.min.css',
    sketchy: '/vendor/bootswatch/sketchy.min.css',
    flatly: '/vendor/bootswatch/flatly.min.css',
    morph: '/vendor/bootswatch/morph.min.css',
    cyborg: '/vendor/bootswatch/cyborg.min.css',
    paper: '/vendor/bootstrap/bootstrap.min.css',
    neobrutal: '/vendor/bootstrap/bootstrap.min.css',
    pastel: '/vendor/bootstrap/bootstrap.min.css',
    terminal: '/vendor/bootswatch/cyborg.min.css'
};

export const STYLE_LABELS = {
    standard: 'Chuẩn Hiện Đại',
    sketchy: 'Nét Vẽ Nghệch Ngoặc',
    morph: 'Kính Mờ Mềm Mại',
    flatly: 'Phẳng Sắc Nét',
    cyborg: 'Đêm Công Nghệ',
    paper: 'Trang Sách Cổ Điển',
    neobrutal: 'Truyện Tranh Pop',
    pastel: 'Kẹo Ngọt Dịu Mắt',
    terminal: 'Lập Trình Viên'
};

export const PALETTE_LABELS = {
    blue: 'Xanh Sapphire',
    ocean: 'Xanh Đại Dương',
    emerald: 'Lục Bảo Thư Viện',
    gold: 'Vàng Mộng Mơ',
    rose: 'Hồng Ấm Áp',
    lavender: 'Tím Oải Hương',
    sepia: 'Giấy Cổ Thư Viện',
    coral: 'Cam Hoàng Hôn',
    royal: 'Tím Hoàng Gia',
    midnight: 'Đêm Ngân Hà',
    mint: 'Bạc Hà Tươi Mát',
    sakura: 'Hoa Anh Đào Pastel',
    obsidian: 'Đen Kim Tước'
};

export const BG_LABELS = {
    solid: 'Nền Trơn Tiêu Chuẩn',
    'paper-grain': 'Giấy Sách Ấm Áp',
    'dot-grid': 'Lưới Chấm Sổ Tay',
    'graph-grid': 'Kẻ Ô Kỹ Thuật',
    'notebook-lines': 'Trang Vở Kẻ Ngang',
    'soft-aurora': 'Cực Quang Dịu Nhẹ',
    'sunset-glow': 'Ánh Hoàng Hôn Ấm',
    botanical: 'Vườn Sách Xanh Mát',
    'starry-night': 'Bầu Trời Sao Nhẹ',
    'warm-linen': 'Vải Bố Cổ Điển',
    'pastel-clouds': 'Mây Pastel Mộng Mơ',
    'geometric-prism': 'Hoa Văn Hình Học Nhẹ'
};

const ALLOWED_FONT_SIZES = ['xs', 'sm', 'md', 'lg', 'xl', '2xl', '3xl'];

const DARK_LOCKED_STYLES = new Set(['cyborg', 'terminal']);

export function getUiPrefs() {
    const def = { theme: 'light', style: 'standard', palette: 'blue', background: 'solid', font: 'roboto', fontSize: 'md' };
    try {
        const raw = localStorage.getItem('bookshare_ui_prefs');
        if (raw) {
            const p = JSON.parse(raw);
            return {
                theme: p.theme === 'dark' ? 'dark' : 'light',
                style: STYLE_CSS_MAP[p.style] ? p.style : 'standard',
                palette: PALETTE_LABELS[p.palette] ? p.palette : 'blue',
                background: BG_LABELS[p.background] ? p.background : 'solid',
                font: ['roboto', 'bevietnam', 'lora', 'patrick', 'jetbrains'].includes(p.font) ? p.font : 'roboto',
                fontSize: ALLOWED_FONT_SIZES.includes(p.fontSize) ? p.fontSize : 'md'
            };
        }
        const legacyTheme = localStorage.getItem('bookshare_theme');
        const legacyPalette = localStorage.getItem('bookshare_palette');
        if (legacyTheme === 'dark') def.theme = 'dark';
        if (legacyPalette && PALETTE_LABELS[legacyPalette]) def.palette = legacyPalette;
    } catch { /* chế độ riêng tư */ }
    return def;
}

export function applyUiPrefs(partial = {}, { persistRemote = false, silent = true } = {}) {
    const current = getUiPrefs();
    const next = {
        theme: partial.theme !== undefined ? (partial.theme === 'dark' ? 'dark' : 'light') : current.theme,
        style: partial.style !== undefined && STYLE_CSS_MAP[partial.style] ? partial.style : current.style,
        palette: partial.palette !== undefined && PALETTE_LABELS[partial.palette] ? partial.palette : current.palette,
        background: partial.background !== undefined && BG_LABELS[partial.background] ? partial.background : current.background,
        font: partial.font !== undefined && ['roboto', 'bevietnam', 'lora', 'patrick', 'jetbrains'].includes(partial.font) ? partial.font : current.font,
        fontSize: partial.fontSize !== undefined && ALLOWED_FONT_SIZES.includes(partial.fontSize) ? partial.fontSize : current.fontSize
    };

    const effectiveTheme = DARK_LOCKED_STYLES.has(next.style) ? 'dark' : next.theme;
    const html = document.documentElement;
    html.setAttribute('data-bs-theme', effectiveTheme);
    html.setAttribute('data-ui-style', next.style);
    html.setAttribute('data-bg-pattern', next.background);
    html.setAttribute('data-font-family', next.font);
    html.setAttribute('data-font-size', next.fontSize);
    document.body?.setAttribute('data-portal-palette', next.palette);

    const bsLink = document.getElementById('bsBootstrapThemeLink');
    const targetHref = STYLE_CSS_MAP[next.style] || STYLE_CSS_MAP.standard;
    if (bsLink && !bsLink.getAttribute('href')?.endsWith(targetHref)) {
        bsLink.setAttribute('href', targetHref);
    }

    try {
        localStorage.setItem('bookshare_ui_prefs', JSON.stringify(next));
        localStorage.setItem('bookshare_theme', effectiveTheme);
        localStorage.setItem('bookshare_palette', next.palette);
    } catch { /* bỏ qua */ }

    const btn = document.getElementById('btnThemeToggle');
    const metaTheme = document.querySelector('meta[name="theme-color"]');
    if (metaTheme) metaTheme.setAttribute('content', effectiveTheme === 'dark' ? '#0f172a' : '#f8fafc');
    if (btn) {
        btn.setAttribute('aria-pressed', String(effectiveTheme === 'dark'));
        btn.setAttribute('aria-label', effectiveTheme === 'dark' ? 'Tắt chế độ tối' : 'Bật chế độ tối');
        btn.innerHTML = effectiveTheme === 'dark'
            ? '<i class="bi bi-sun-fill" aria-hidden="true"></i><span>Sáng</span>'
            : '<i class="bi bi-moon-stars-fill" aria-hidden="true"></i><span>Tối</span>';
    }

    const activeLabel = document.getElementById('activePaletteLabel');
    if (activeLabel) activeLabel.innerText = PALETTE_LABELS[next.palette] || 'Giao Diện';

    document.querySelectorAll('[data-set-palette]').forEach(el => {
        el.classList.toggle('active', el.getAttribute('data-set-palette') === next.palette);
    });
    document.querySelectorAll('[data-set-ui-style]').forEach(el => {
        el.classList.toggle('active', el.getAttribute('data-set-ui-style') === next.style);
    });

    const syncSel = (id, val) => { const s = document.getElementById(id); if (s && s.value !== val) s.value = val; };
    syncSel('aiStyleSelect', next.style);
    syncSel('aiPaletteSelect', next.palette);
    syncSel('aiBgSelect', next.background);
    syncSel('aiFontSelect', next.font);
    syncSel('aiFontSizeSelect', next.fontSize);
    syncSel('s13StyleSelect', next.style);
    syncSel('s13PaletteSelect', next.palette);
    syncSel('s13BgSelect', next.background);
    syncSel('s13FontSelect', next.font);
    syncSel('s13FontSizeSelect', next.fontSize);
    syncSel('prefThemeMode', effectiveTheme);
    syncSel('prefUiStyle', next.style);
    syncSel('prefPalette', next.palette);
    syncSel('prefBgPattern', next.background);
    syncSel('prefFontFamily', next.font);
    syncSel('prefFontSize', next.fontSize);

    applyChartTheme(effectiveTheme === 'dark');

    if (persistRemote && document.body?.getAttribute('data-authenticated') === '1') {
        apiFetch('/S01AuthOtpGoogle/SaveUiPreferences', {
            method: 'POST',
            body: next,
            button: null
        }).catch(() => { /* lưu cục bộ đã thành công */ });
    }

    if (!silent) {
        showSystemStatus(200, 'Đã cập nhật giao diện', `Màu: ${PALETTE_LABELS[next.palette] || next.palette} • Nền: ${BG_LABELS[next.background] || next.background} • Style: ${STYLE_LABELS[next.style] || next.style}`);
    }
    return next;
}

export function setPortalThemeMode(mode) {
    applyUiPrefs({ theme: mode === 'dark' ? 'dark' : 'light' }, { persistRemote: true, silent: true });
}

export function applyClientCommands(clientCommands = []) {
    if (!Array.isArray(clientCommands)) return;
    for (const cmd of clientCommands) {
        if (cmd?.command === 'set_theme' && cmd.args?.theme) {
            setPortalThemeMode(cmd.args.theme);
        } else if (cmd?.command === 'set_ui_prefs' && cmd.args) {
            applyUiPrefs(cmd.args, { persistRemote: true, silent: true });
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
                } else if (cmd.command === 'set_ui_prefs') {
                    applyUiPrefs(cmd.args || {}, { persistRemote: true, silent: false });
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
    applyUiPrefs({}, { persistRemote: false, silent: true });

    btn?.addEventListener('click', () => {
        const isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
        const nextTheme = isDark ? 'light' : 'dark';
        const current = getUiPrefs();
        const patch = { theme: nextTheme };
        if (nextTheme === 'light' && DARK_LOCKED_STYLES.has(current.style)) {
            patch.style = 'standard';
        }
        applyUiPrefs(patch, { persistRemote: true, silent: true });
    });

    // Đồng bộ cấu hình giao diện đã lưu từ máy chủ khi người dùng đã đăng nhập (chỉ áp dụng nếu máy chưa tùy biến hoặc lần đầu vào)
    if (document.body?.getAttribute('data-authenticated') === '1') {
        fetch('/S01AuthOtpGoogle/GetUiPreferences', { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
            .then(r => (r.ok ? r.json() : null))
            .then(data => {
                if (!data?.authenticated || !data.preferences) return;
                const hasLocal = Boolean(localStorage.getItem('bookshare_ui_prefs'));
                if (!hasLocal) {
                    applyUiPrefs(data.preferences, { persistRemote: false, silent: true });
                }
            })
            .catch(() => { /* bỏ qua */ });
    }
}

// ---------- Chuông thông báo Real-time + Popup Nhanh & Xem Chi Tiết (S18) ----------

function formatShortTime(iso) {
    if (!iso) return '';
    try {
        const d = new Date(iso);
        return d.toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    } catch { return ''; }
}

function initNotificationPolling() {
    const bell = document.getElementById('notificationBell');
    if (!bell) return;
    const dot = bell.querySelector('.notif-dot');
    const popupList = document.getElementById('notifPopupList');
    const popupSummary = document.getElementById('notifPopupSummary');
    const btnPopupMarkAll = document.getElementById('btnPopupMarkAllRead');
    const storageKey = 'bookshare_unread_seen';

    const updateBadge = (count) => {
        if (dot) {
            dot.textContent = count > 99 ? '99+' : String(count);
            dot.classList.toggle('d-none', count === 0);
        }
        bell.setAttribute('aria-label', count ? `Thông báo: ${count} chưa đọc` : 'Thông báo');

        const mobileBadge = document.getElementById('mobileUnreadBadge');
        if (mobileBadge) {
            mobileBadge.textContent = count > 99 ? '99+' : String(count);
            mobileBadge.classList.toggle('d-none', count === 0);
        }
    };

    const appendNotifIdParam = (url, id) => {
        if (!url || !id) return url;
        if (url.includes('notifId=')) return url;
        return `${url}${url.includes('?') ? '&' : '?'}notifId=${encodeURIComponent(id)}`;
    };

    const markReadSilently = async (notificationId) => {
        if (!notificationId) return false;
        try {
            const headers = { Accept: 'application/json', 'Content-Type': 'application/json' };
            const csrf = getCsrfToken();
            if (csrf) headers['RequestVerificationToken'] = csrf;
            const resp = await fetch('/S18NotificationHub/MarkRead', {
                method: 'POST',
                headers,
                credentials: 'same-origin',
                body: JSON.stringify({ notificationId })
            });
            return resp.ok;
        } catch {
            return false;
        }
    };

    const openNotificationDetailModal = async (item) => {
        if (!item.isRead) {
            await markReadSilently(item.id);
            item.isRead = true;
            loadPopupList();
        }

        const baseTarget = item.detailUrl || `/S18NotificationHub?highlight=${item.id}`;
        const targetUrl = appendNotifIdParam(baseTarget, item.id);
        const res = await window.Swal.fire({
            title: item.title || 'Chi tiết thông báo',
            html: `
                <div class="text-start">
                    <div class="d-flex justify-content-between align-items-center mb-2 small text-muted">
                        <span class="badge bg-primary">${escapeHtml(item.type || 'Hệ thống')}</span>
                        <span>${escapeHtml(formatShortTime(item.createdAt))}</span>
                    </div>
                    <div class="p-3 rounded-3 border bg-body-tertiary small" style="white-space:pre-line;line-height:1.55;">
                        ${escapeHtml(item.content || '')}
                    </div>
                </div>`,
            showCancelButton: true,
            confirmButtonText: 'Đi tới trang chi tiết →',
            cancelButtonText: 'Đóng',
            confirmButtonColor: '#2563eb',
            ...swalThemeOptions()
        });
        if (res.isConfirmed) {
            if (!item.isRead) {
                await markReadSilently(item.id);
                item.isRead = true;
            }
            window.location.href = targetUrl;
        }
    };

    const loadPopupList = async () => {
        if (!popupList) return;
        try {
            const res = await fetch('/S18NotificationHub/RecentPopup?take=8', {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });
            if (!res.ok) return;
            const data = await res.json();
            const count = Number(data.unreadCount) || 0;
            const items = Array.isArray(data.items) ? data.items : [];
            updateBadge(count);
            if (popupSummary) {
                popupSummary.textContent = count > 0 ? `${count} chưa đọc / ${data.totalCount || items.length} thông báo` : 'Bạn đã đọc hết thông báo';
            }

            if (!items.length) {
                popupList.innerHTML = '<div class="p-4 text-center small text-muted"><i class="bi bi-bell-slash d-block fs-4 mb-1"></i>Chưa có thông báo nào.</div>';
                return;
            }

            popupList.innerHTML = '';
            for (const item of items) {
                const row = document.createElement('div');
                row.className = `bs-notif-item ${item.isRead ? '' : 'is-unread'}`;

                const header = document.createElement('div');
                header.className = 'd-flex justify-content-between align-items-center gap-2 mb-1';
                header.innerHTML = `
                    <span class="badge ${item.isRead ? 'bg-secondary' : 'bg-primary'}" style="font-size:0.65rem;">${escapeHtml(item.type || 'Hệ thống')}</span>
                    <small class="text-muted" style="font-size:0.68rem;">${escapeHtml(formatShortTime(item.createdAt))}</small>`;

                const titleEl = document.createElement('div');
                titleEl.className = 'bs-notif-item-title';
                titleEl.innerText = item.title || '';

                const bodyEl = document.createElement('div');
                bodyEl.className = 'bs-notif-item-body';
                bodyEl.innerText = item.content || '';

                const actions = document.createElement('div');
                actions.className = 'd-flex justify-content-between align-items-center mt-2 pt-1';

                const btnQuick = document.createElement('button');
                btnQuick.type = 'button';
                btnQuick.className = 'btn btn-xs btn-outline-primary py-0 px-2';
                btnQuick.innerText = 'Xem nhanh';
                btnQuick.addEventListener('click', (ev) => {
                    ev.stopPropagation();
                    openNotificationDetailModal(item);
                });

                const linkGo = document.createElement('a');
                const detailHref = appendNotifIdParam(item.detailUrl || `/S18NotificationHub?highlight=${item.id}`, item.id);
                linkGo.href = detailHref;
                linkGo.className = 'small fw-semibold text-decoration-none';
                linkGo.innerHTML = 'Mở trang chi tiết <i class="bi bi-arrow-right-short"></i>';
                linkGo.addEventListener('click', async (ev) => {
                    ev.stopPropagation();
                    if (!item.isRead) {
                        ev.preventDefault();
                        await markReadSilently(item.id);
                        item.isRead = true;
                        window.location.href = detailHref;
                    }
                });

                actions.appendChild(btnQuick);
                actions.appendChild(linkGo);

                row.appendChild(header);
                row.appendChild(titleEl);
                row.appendChild(bodyEl);
                row.appendChild(actions);
                row.addEventListener('click', () => openNotificationDetailModal(item));
                popupList.appendChild(row);
            }
        } catch {
            popupList.innerHTML = '<div class="p-3 text-center small text-muted">Không tải được danh sách thông báo lúc này.</div>';
        }
    };

    bell.addEventListener('show.bs.dropdown', loadPopupList);
    bell.addEventListener('click', loadPopupList);

    btnPopupMarkAll?.addEventListener('click', async (ev) => {
        ev.preventDefault();
        const res = await apiFetch('/S18NotificationHub/MarkAllRead', { method: 'POST', body: {}, button: btnPopupMarkAll });
        if (res.ok) {
            await loadPopupList();
        }
    });

    const lastNotifIdKey = 'bookshare_last_toasted_notif_id';
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
            const latestId = Number(data.latestId) || 0;
            updateBadge(count);

            let lastToastedId = 0;
            try { lastToastedId = Number(localStorage.getItem(lastNotifIdKey)) || 0; } catch { /* bỏ qua */ }
            if (count > 0 && latestId > 0 && latestId !== lastToastedId) {
                bell.classList.remove('is-ringing');
                void bell.offsetWidth;
                bell.classList.add('is-ringing');
                if (lastToastedId > 0) {
                    toast(300, `Bạn có ${count} thông báo chưa đọc`, data.latestTitle || '');
                }
                try { localStorage.setItem(lastNotifIdKey, String(latestId)); } catch { /* bỏ qua */ }
            }
            try { sessionStorage.setItem(storageKey, String(count)); } catch { /* bỏ qua */ }
        } catch {
            // Mất mạng tạm thời: lần poll sau thử lại
        }
    };

    // Tự động đánh dấu đã đọc khi mở từ thông báo Mobile hoặc link ?notifId=ID
    const urlNotifId = parseInt(new URLSearchParams(window.location.search).get('notifId') || '0', 10);
    if (urlNotifId > 0) {
        markReadSilently(urlNotifId).then(() => poll());
    } else {
        poll();
    }
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
                const ret = form.querySelector('input[name="returnUrl"]')?.value?.trim() || '';
                const qs = ret ? `?returnUrl=${encodeURIComponent(ret)}` : '';
                window.BookShareAndroidBridge.openExternalAuth(`${window.location.origin}/S01AuthOtpGoogle/GoogleLoginMobile${qs}`);
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
    window.BookShareUI = { toast, confirmDialog, celebrate, loadScript, renderMarkdown, renderSkeleton, renderQrCode, showHandoverQrModal, confirmHandoverQrModal, openBarcodeScanner, openUniversalScanner, openQrPaymentScanner, openQrInvoiceConfirmDialog, showQrPaymentInvoiceModal, applyClientCommands, renderCopilotExtras, setPortalThemeMode, getUiPrefs, applyUiPrefs, PALETTE_LABELS, trustTier, getRecaptchaToken, CDN };

    document.addEventListener('click', (e) => {
        const qrPayBtn = e.target.closest('.js-open-qr-pay-scanner');
        if (qrPayBtn) {
            e.preventDefault();
            openQrPaymentScanner();
            return;
        }
        const quickQrLoginBtn = e.target.closest('.js-open-quick-qr-login');
        if (quickQrLoginBtn) {
            e.preventDefault();
            openUniversalScanner();
        }
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

    // Nút đóng thanh trạng thái
    const btnCloseStatus = document.getElementById('btnHideStatusBox');
    if (btnCloseStatus) {
        btnCloseStatus.addEventListener('click', () => {
            document.getElementById('systemStatusBox')?.classList.add('hidden-banner');
        });
    }

    // Gắn sự kiện chọn Bảng màu & Phong cách Bootstrap trên toàn Header/Menu
    document.querySelectorAll('[data-set-palette]').forEach(btn => {
        btn.addEventListener('click', () => {
            const p = btn.getAttribute('data-set-palette') || 'blue';
            applyUiPrefs({ palette: p }, { persistRemote: true, silent: false });
        });
    });

    document.querySelectorAll('[data-set-ui-style]').forEach(btn => {
        btn.addEventListener('click', () => {
            const st = btn.getAttribute('data-set-ui-style') || 'standard';
            const patch = { style: st };
            if (st === 'sketchy') patch.font = 'patrick';
            else if (st === 'paper') patch.font = 'lora';
            else if (st === 'terminal') patch.font = 'jetbrains';
            else if (st === 'standard' && ['patrick', 'jetbrains'].includes(getUiPrefs().font)) patch.font = 'roboto';
            applyUiPrefs(patch, { persistRemote: true, silent: false });
        });
    });

    // Khung Chat AI Gemini nổi góc phải (Tải lịch sử bền vững theo phân quyền + Reset + Gửi Email + Style/Font)
    const aiWidget = document.getElementById('aiFloatingWidget');
    const btnToggleChat = document.getElementById('btnToggleAIChat');
    const btnMinimizeChat = document.getElementById('btnMinimizeAIChat');
    const btnCloseChat = document.getElementById('btnCloseAIChat');
    const btnResetChat = document.getElementById('btnResetAIChat');
    const btnEmailChat = document.getElementById('btnEmailAIChat');
    const btnToggleStyleBar = document.getElementById('btnToggleAiStyleBar');
    const aiStyleBar = document.getElementById('aiWidgetStyleBar');
    const chatPopup = document.getElementById('aiChatPopup');
    const chatForm = document.getElementById('aiChatWidgetForm');
    const chatInput = document.getElementById('aiChatWidgetInput');
    const chatMessages = document.getElementById('aiChatWidgetMessages');
    const isAuthUser = aiWidget?.getAttribute('data-authenticated') === '1';
    const guestChatKey = 'bookshare_guest_ai_chat_v1';
    let activeAiSessionId = 0;
    let historyLoaded = false;

    const scrollChatToBottom = () => {
        if (chatMessages) chatMessages.scrollTo({ top: chatMessages.scrollHeight, behavior: 'smooth' });
    };

    const appendWidgetMsg = (text, isBot, extras = null, { saveGuest = false } = {}) => {
        if (!chatMessages) return;
        const div = document.createElement('div');
        div.className = `ai-msg ${isBot ? 'ai-msg-bot ai-md' : 'ai-msg-user'}`;
        if (isBot) {
            div.innerHTML = renderMarkdown(text);
            if (extras) {
                renderCopilotExtras(div, extras);
            }
        } else {
            div.innerText = text;
        }
        chatMessages.appendChild(div);
        scrollChatToBottom();

        if (saveGuest && !isAuthUser) {
            try {
                const list = JSON.parse(sessionStorage.getItem(guestChatKey) || '[]');
                list.push({ sender: isBot ? 'Gemini' : 'User', text });
                sessionStorage.setItem(guestChatKey, JSON.stringify(list.slice(-30)));
            } catch { /* bỏ qua */ }
        }
    };

    const loadPersistentAiHistory = async () => {
        if (!chatMessages || historyLoaded) return;
        historyLoaded = true;
        if (isAuthUser) {
            try {
                const res = await fetch('/api/ai-chat/history', {
                    headers: { Accept: 'application/json' },
                    credentials: 'same-origin'
                });
                if (!res.ok) return;
                const data = await res.json();
                activeAiSessionId = Number(data.sessionId) || 0;
                const msgs = Array.isArray(data.messages) ? data.messages : [];
                if (msgs.length > 0) {
                    chatMessages.innerHTML = '';
                    for (const m of msgs) {
                        const isBot = String(m.sender || '').toLowerCase() === 'gemini';
                        appendWidgetMsg(m.messageText || '', isBot);
                    }
                }
            } catch { /* giữ lời chào mặc định nếu mất mạng tạm */ }
        } else {
            try {
                const saved = JSON.parse(sessionStorage.getItem(guestChatKey) || '[]');
                if (Array.isArray(saved) && saved.length > 0) {
                    chatMessages.innerHTML = '';
                    for (const m of saved) {
                        appendWidgetMsg(m.text || '', m.sender === 'Gemini');
                    }
                }
            } catch { /* bỏ qua */ }
        }
    };

    // Nạp lịch sử nền ngay khi tải trang (giữ đúng lịch sử theo tài khoản & phân quyền)
    if (aiWidget) {
        loadPersistentAiHistory();
    }

    const setChatOpen = (open) => {
        if (!chatPopup) return;
        chatPopup.classList.toggle('d-none', !open);
        btnToggleChat?.setAttribute('aria-expanded', String(open));
        if (open) {
            loadPersistentAiHistory();
            scrollChatToBottom();
            chatInput?.focus();
        }
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

    // Thanh chọn Style/Màu/Font/Cỡ chữ ngay trong Widget Chat AI
    btnToggleStyleBar?.addEventListener('click', () => {
        aiStyleBar?.classList.toggle('d-none');
        applyUiPrefs({}, { persistRemote: false, silent: true });
    });
    document.getElementById('aiStyleSelect')?.addEventListener('change', (e) => {
        const st = e.target.value;
        const patch = { style: st };
        if (st === 'sketchy') patch.font = 'patrick';
        else if (st === 'paper') patch.font = 'lora';
        else if (st === 'terminal') patch.font = 'jetbrains';
        applyUiPrefs(patch, { persistRemote: true, silent: true });
    });
    document.getElementById('aiPaletteSelect')?.addEventListener('change', (e) => {
        applyUiPrefs({ palette: e.target.value }, { persistRemote: true, silent: true });
    });
    document.getElementById('aiBgSelect')?.addEventListener('change', (e) => {
        applyUiPrefs({ background: e.target.value }, { persistRemote: true, silent: true });
    });
    document.getElementById('aiFontSelect')?.addEventListener('change', (e) => {
        applyUiPrefs({ font: e.target.value }, { persistRemote: true, silent: true });
    });
    document.getElementById('aiFontSizeSelect')?.addEventListener('change', (e) => {
        applyUiPrefs({ fontSize: e.target.value }, { persistRemote: true, silent: true });
    });

    // Nút Reset Chat (xóa lịch sử phiên hiện tại & làm mới lời chào theo phân quyền)
    btnResetChat?.addEventListener('click', async () => {
        const ok = await confirmDialog({
            title: 'Đặt lại cuộc trò chuyện AI?',
            text: 'Lịch sử tin nhắn trong phiên chat hiện tại sẽ được làm mới sạch sẽ.',
            confirmText: 'Đặt lại ngay',
            danger: true
        });
        if (!ok) return;

        if (!isAuthUser) {
            try { sessionStorage.removeItem(guestChatKey); } catch { /* bỏ qua */ }
            if (chatMessages) {
                chatMessages.innerHTML = '';
                appendWidgetMsg('Đã làm mới cuộc trò chuyện! Bạn cần tìm sách mượn miễn phí hay đổi phong cách giao diện?', true);
            }
            toast(200, 'Đã đặt lại khung chat AI', 'Bạn có thể bắt đầu cuộc trò chuyện mới.');
            return;
        }

        const res = await apiFetch('/api/ai-chat/reset', {
            method: 'POST',
            body: { sessionId: activeAiSessionId || null },
            button: btnResetChat
        });
        if (res.ok && chatMessages) {
            activeAiSessionId = Number(res.data?.sessionId) || activeAiSessionId;
            chatMessages.innerHTML = '';
            const greet = res.data?.greeting?.messageText || 'Đã làm mới cuộc trò chuyện Trợ lý AI!';
            appendWidgetMsg(greet, true);
        }
    });

    // Nút Gửi tóm tắt hội thoại AI qua Email (bắt buộc có Email hợp lệ trong Cài Đặt)
    btnEmailChat?.addEventListener('click', async () => {
        const res = await apiFetch('/S13GeminiAiAssistant/SendChatSummaryEmail', {
            method: 'POST',
            body: { sessionId: activeAiSessionId || null },
            button: btnEmailChat
        });
        if (!res.ok && res.data?.requiresEmailSetup) {
            const go = await confirmDialog({
                title: 'Chưa thiết lập Email nhận thư',
                text: res.message || 'Vui lòng điền địa chỉ Email hợp lệ trong Cài Đặt Tài Khoản trước khi gửi thư.',
                confirmText: 'Đi tới Cài Đặt điền Email →',
                cancelText: 'Để sau',
                danger: false
            });
            if (go) window.location.href = '/S01AuthOtpGoogle';
        }
    });

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

            appendWidgetMsg(promptText, false, null, { saveGuest: true });
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
                    if (res.data.sessionId) activeAiSessionId = Number(res.data.sessionId) || activeAiSessionId;
                    applyClientCommands(res.data.clientCommands);
                    appendWidgetMsg(res.data.reply, true, {
                        clientCommands: res.data.clientCommands || [],
                        actionCards: res.data.actionCards || []
                    }, { saveGuest: true });
                } else {
                    appendWidgetMsg('Trợ lý AI: Bạn có thể tra cứu sách trong **Kho chung ĐH CMC** hoặc kiểm tra tiến độ đọc tại mục **Tiến Độ Đọc Sách**.', true, null, { saveGuest: true });
                }
            } finally {
                typing.remove();
                chatInput.disabled = false;
                chatInput.focus();
            }
        });
    }
});
