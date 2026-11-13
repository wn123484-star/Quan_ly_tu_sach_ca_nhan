import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Lọc nhanh theo loại thông báo trên Client
    document.querySelectorAll('.js-filter-notif').forEach(btn => {
        btn.addEventListener('click', () => {
            const targetType = btn.getAttribute('data-type') || 'all';
            document.querySelectorAll('.s18-notif-card').forEach(card => {
                const t = card.getAttribute('data-notif-type');
                card.style.display = (targetType === 'all' || t === targetType) ? 'block' : 'none';
            });
        });
    });

    // 2. Đánh dấu 1 thông báo đã đọc
    document.querySelectorAll('.js-mark-read').forEach(btn => {
        btn.addEventListener('click', async () => {
            const notificationId = parseInt(btn.getAttribute('data-id') || '0', 10);
            const res = await apiFetch('/S18NotificationHub/MarkRead', {
                method: 'POST',
                body: { notificationId }
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 450);
            }
        });
    });

    // 3. Đánh dấu tất cả đã đọc
    document.getElementById('btnMarkAllRead')?.addEventListener('click', async () => {
        const res = await apiFetch('/S18NotificationHub/MarkAllRead', {
            method: 'POST',
            body: {}
        });
        if (res.ok) {
            setTimeout(() => window.location.reload(), 550);
        }
    });

    // 4. Kích hoạt gửi chiến dịch Email qua IResendEmailService
    document.querySelectorAll('.js-trigger-campaign').forEach(btn => {
        btn.addEventListener('click', async () => {
            const campaignType = btn.getAttribute('data-campaign') || 'DueReminder';
            btn.disabled = true;
            try {
                const res = await apiFetch('/S18NotificationHub/TriggerResendEmail', {
                    method: 'POST',
                    body: { campaignType }
                });
                if (res.ok) {
                    setTimeout(() => window.location.reload(), 700);
                }
            } finally {
                btn.disabled = false;
            }
        });
    });

    // 5. Gửi thông báo tùy chỉnh kèm Resend Email
    document.getElementById('formCustomResend')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const customTitle = document.getElementById('customNotifTitle')?.value.trim();
        const customContent = document.getElementById('customNotifContent')?.value.trim();
        if (!customTitle || !customContent) return;

        const res = await apiFetch('/S18NotificationHub/TriggerResendEmail', {
            method: 'POST',
            body: {
                campaignType: 'Custom',
                customTitle,
                customContent
            }
        });
        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
        }
    });
});
