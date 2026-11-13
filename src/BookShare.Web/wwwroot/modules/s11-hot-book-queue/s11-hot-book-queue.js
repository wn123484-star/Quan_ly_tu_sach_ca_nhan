/**
 * MODULE S11: HOT BOOK QUEUE (s11-hot-book-queue.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S11
 */
import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Đặt trước xếp vào hàng chờ (Ưu tiên Premium)
    document.querySelectorAll('.js-s11-enqueue').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookId = parseInt(btn.getAttribute('data-book-id') || '0', 10);
            if (!bookId) return;

            const res = await apiFetch('/S11HotBookQueue/Enqueue', {
                method: 'POST',
                body: { bookId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 2. Thông báo tự động cho người đứng đầu hàng chờ khi có sách trả về
    document.querySelectorAll('.js-s11-notify-top').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookId = parseInt(btn.getAttribute('data-book-id') || '0', 10);
            if (!bookId) return;

            const res = await apiFetch('/S11HotBookQueue/NotifyTop', {
                method: 'POST',
                body: { bookId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 3. Hủy phiếu đặt trước & đôn thứ tự hàng chờ
    document.querySelectorAll('.js-s11-cancel').forEach(btn => {
        btn.addEventListener('click', async () => {
            const reservationId = parseInt(btn.getAttribute('data-res-id') || '0', 10);
            if (!reservationId) return;

            const res = await apiFetch('/S11HotBookQueue/Cancel', {
                method: 'POST',
                body: { reservationId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });
});
