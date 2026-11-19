/**
 * BOOKSHARE PWA SERVICE WORKER (wwwroot/service-worker.js)
 * - Cache tài nguyên tĩnh (CSS, JS, Fonts, Icons) theo chiến lược Stale-While-Revalidate
 * - KHÔNG cache trang HTML & API (dữ liệu cá nhân, phiếu mượn, CSRF token luôn lấy mới từ server)
 * - Mất mạng khi mở trang: hiển thị trang báo Offline thân thiện
 */
const CACHE_NAME = 'bookshare-static-v1';

const PRECACHE_URLS = [
    '/css/core-theme.css',
    '/js/core-api.js',
    '/vendor/bootstrap/bootstrap.min.css',
    '/vendor/bootstrap/bootstrap.bundle.min.js',
    '/vendor/bootstrap-icons/font/bootstrap-icons.css',
    '/vendor/sweetalert2/sweetalert2.all.min.js',
    '/vendor/chartjs/chart.umd.js',
    '/images/icons/icon-192.png',
    '/images/icons/icon-512.png',
    '/manifest.json'
];

// Thư viện, font, icon gần như không đổi: ưu tiên cache
const IMMUTABLE_PREFIXES = ['/vendor/', '/fonts/', '/images/', '/lib/'];
// CSS/JS của dự án đổi thường xuyên: ưu tiên mạng, mất mạng mới dùng cache
const APP_STATIC_PREFIXES = ['/css/', '/js/', '/modules/'];

const OFFLINE_HTML = `<!doctype html><html lang="vi"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>BookShare - Offline</title>
<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;
background:linear-gradient(160deg,#1e3a8a,#2563eb);color:#fff;text-align:center;padding:24px}
button{margin-top:16px;padding:10px 22px;border:0;border-radius:24px;font-weight:700;color:#1e3a8a;background:#fff}</style></head>
<body><div><div style="font-size:3rem">📚</div><h1>Bạn đang offline</h1>
<p>Không kết nối được tới máy chủ BookShare. Hãy kiểm tra mạng rồi thử lại.</p>
<button onclick="location.reload()">Thử lại</button></div></body></html>`;

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(PRECACHE_URLS))
            .catch(() => { /* thiếu 1 file không chặn cài đặt */ })
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', (event) => {
    const request = event.request;
    if (request.method !== 'GET') return;

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;

    // Thư viện / font / icon: trả cache ngay, chưa có thì tải và lưu lại (Cache First)
    if (IMMUTABLE_PREFIXES.some(prefix => url.pathname.startsWith(prefix))) {
        event.respondWith(
            caches.open(CACHE_NAME).then(async cache => {
                const cached = await cache.match(request, { ignoreSearch: true });
                if (cached) return cached;
                const response = await fetch(request);
                if (response.ok) cache.put(request, response.clone());
                return response;
            })
        );
        return;
    }

    // CSS / JS của dự án & manifest: luôn lấy bản mới, mất mạng thì dùng bản đã cache (Network First)
    if (APP_STATIC_PREFIXES.some(prefix => url.pathname.startsWith(prefix)) || url.pathname === '/manifest.json') {
        event.respondWith(
            fetch(request)
                .then(response => {
                    if (response.ok) {
                        const copy = response.clone();
                        caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
                    }
                    return response;
                })
                .catch(() => caches.match(request, { ignoreSearch: true }))
        );
        return;
    }

    // Trang HTML: luôn lấy từ mạng, mất mạng thì báo Offline
    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request).catch(() => new Response(OFFLINE_HTML, {
                headers: { 'Content-Type': 'text/html; charset=utf-8' }
            }))
        );
    }
});
