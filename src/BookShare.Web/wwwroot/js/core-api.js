/**
 * BOOKSHARE CORE API & UI HELPER (wwwroot/js/core-api.js)
 * - Tự động đính kèm AntiForgeryToken (CSRF) vào mọi request POST/PUT/DELETE
 * - Cập nhật thanh trạng thái #systemStatusBox (Mã 200, 300, 400, 404, 500) kế thừa book-tracker-ver07 + Toast SweetAlert2
 * - Spinner & khóa nút khi chờ API, Skeleton Loading, Dark Mode, ảnh Lazy Blur-up, Hover 3D, Bảng dạng Thẻ trên Mobile
 * - Chuông thông báo Polling 30 giây (S18), Hiệu ứng chúc mừng (canvas-confetti), Trợ năng (a11y)
 * - Điều khiển hộp thoại Trợ lý AI Gemini nổi ở góc phải dưới màn hình (Markdown + "AI đang gõ...")
 */

// Thư viện ngoài chỉ nạp khi trang thật sự cần (giữ trang nhẹ)
export const CDN = {
    confetti: 'https://cdn.jsdelivr.net/npm/canvas-confetti@1.9.3/dist/confetti.browser.min.js',
    html5Qrcode: 'https://cdn.jsdelivr.net/npm/html5-qrcode@2.3.8/html5-qrcode.min.js',
    jspdf: 'https://cdn.jsdelivr.net/npm/jspdf@2.5.1/dist/jspdf.umd.min.js',
    html2canvas: 'https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js'
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
        position: 'top-end',
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
    }

    if (!silent) toast(code, title, detail);
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

// ---------- Máy quét Mã vạch / QR bằng Camera (html5-qrcode) ----------

/**
 * Mở hộp thoại quét bằng camera, trả về Promise<string|null> (mã đọc được hoặc null nếu đóng).
 */
export async function openBarcodeScanner({ title = 'Quét mã vạch bằng Camera', hint = 'Đưa mã vạch mặt sau cuốn sách vào khung' } = {}) {
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

    const stop = async () => {
        if (scanner?.isScanning) {
            try { await scanner.stop(); } catch { /* camera đã tắt */ }
        }
        try { scanner?.clear(); } catch { /* bỏ qua */ }
    };

    await window.Swal.fire({
        title,
        html: `<div id="bsBarcodeReader" style="width:100%;min-height:240px;border-radius:12px;overflow:hidden;background:#0f172a"></div>
               <p class="small text-muted mt-2 mb-0">${hint}</p>`,
        showCancelButton: true,
        showConfirmButton: false,
        cancelButtonText: 'Đóng camera',
        ...swalThemeOptions(),
        didOpen: async () => {
            scanner = new window.Html5Qrcode('bsBarcodeReader', {
                formatsToSupport: [F.EAN_13, F.EAN_8, F.UPC_A, F.UPC_E, F.CODE_128, F.CODE_39, F.QR_CODE],
                verbose: false
            });
            try {
                await scanner.start(
                    { facingMode: 'environment' },
                    { fps: 12, qrbox: (w, h) => ({ width: Math.min(300, w * 0.85), height: Math.min(160, h * 0.6) }) },
                    (decodedText) => {
                        scannedText = decodedText.trim();
                        window.Swal.close();
                    },
                    () => { /* chưa nhận ra mã ở khung hình này */ }
                );
            } catch (err) {
                window.Swal.close();
                showSystemStatus(403, 'Không mở được Camera (Mã 403)', `Hãy cho phép trình duyệt truy cập camera. Chi tiết: ${err?.message || err}`);
            }
        },
        willClose: stop
    });

    await stop();
    return scannedText;
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
    const render = () => {
        const isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
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

// ---------- Khởi tạo sự kiện dùng chung trên _Layout.cshtml ----------

document.addEventListener('DOMContentLoaded', () => {
    // Chốt chặn: nếu file bị nạp 2 lần dưới 2 URL khác nhau thì chỉ khởi tạo giao diện 1 lần
    if (window.__bookshareCoreReady) return;
    window.__bookshareCoreReady = true;

    window.showSystemStatus = showSystemStatus;
    window.apiFetch = apiFetch;
    window.BookShareUI = { toast, confirmDialog, celebrate, loadScript, renderMarkdown, renderSkeleton, openBarcodeScanner, trustTier, CDN };

    initThemeToggle();
    initAccessibility();
    initMobileTables();
    initLazyImages();
    initTilt();
    initSkeletonNavigation();
    initNotificationPolling();
    initTrustTierCelebration();

    // Thông báo do server gửi qua TempData (sau khi reload) cũng hiện dạng Toast
    const statusBox = document.getElementById('systemStatusBox');
    if (statusBox?.dataset.flash === '1') {
        const code = parseInt((document.getElementById('statusCodeBadge')?.innerText || '').replace(/\D/g, ''), 10) || 200;
        toast(code, document.getElementById('statusTitleText')?.innerText || '', document.getElementById('statusDetailText')?.innerText || '');
    }

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

    const setChatOpen = (open) => {
        if (!chatPopup) return;
        chatPopup.classList.toggle('d-none', !open);
        btnToggleChat?.setAttribute('aria-expanded', String(open));
        if (open) chatInput?.focus();
    };
    if (btnToggleChat && chatPopup) {
        btnToggleChat.addEventListener('click', () => setChatOpen(chatPopup.classList.contains('d-none')));
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

    const appendWidgetMsg = (text, isBot) => {
        if (!chatMessages) return;
        const div = document.createElement('div');
        div.className = `ai-msg ${isBot ? 'ai-msg-bot ai-md' : 'ai-msg-user'}`;
        if (isBot) {
            div.innerHTML = renderMarkdown(text); // renderMarkdown đã escape HTML (chống XSS theo RULES.md)
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
                    appendWidgetMsg(res.data.reply, true);
                } else {
                    appendWidgetMsg('Trợ lý AI: Bạn có thể tra cứu sách trong **Kho chung ĐH CMC** hoặc kiểm tra tiến độ đọc tại dịch vụ **S03 ReadingTracker**.', true);
                }
            } finally {
                typing.remove();
                chatInput.disabled = false;
                chatInput.focus();
            }
        });
    }
});
