import { apiFetch, confirmDialog, getRecaptchaToken } from '../../js/core-api.js';

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

    const btnS18SendOtp = document.getElementById('btnS18SendOtp');
    const btnS18ConfirmOtp = document.getElementById('btnS18ConfirmOtp');
    const s18EmailInput = document.getElementById('s18VerifyEmailInput');
    const s18OtpInput = document.getElementById('s18OtpCodeInput');
    const s18DevBanner = document.getElementById('s18DevOtpBanner');
    const s18DevVal = document.getElementById('s18DevOtpValue');
    const s18VerifiedBadge = document.getElementById('s18OtpVerifiedBadge');

    btnS18SendOtp?.addEventListener('click', async () => {
        const email = s18EmailInput?.value.trim() || '';
        if (!email) return;
        // [BOT-GUARD] Đính kèm token Google reCAPTCHA v3 khi gửi OTP xác minh Email thông báo
        const recaptchaToken = await getRecaptchaToken('send_otp');
        const res = await apiFetch('/S01AuthOtpGoogle/SendOtp', {
            method: 'POST',
            button: btnS18SendOtp,
            body: { email, purpose: 'VerifyNotificationEmail', recaptchaToken }
        });
        if (res.ok && res.data) {
            if (res.data.devOtpCode && s18DevBanner && s18DevVal && s18OtpInput) {
                s18DevVal.innerText = res.data.devOtpCode;
                s18OtpInput.value = res.data.devOtpCode;
                s18DevBanner.classList.remove('d-none');
            }
        }
    });

    btnS18ConfirmOtp?.addEventListener('click', async () => {
        const email = s18EmailInput?.value.trim() || '';
        const otpCode = s18OtpInput?.value.trim() || '';
        if (!email || otpCode.length !== 6) return;
        const res = await apiFetch('/S01AuthOtpGoogle/VerifyOtp', {
            method: 'POST',
            button: btnS18ConfirmOtp,
            body: { email, otpCode, purpose: 'VerifyNotificationEmail' }
        });
        if (res.ok && s18VerifiedBadge) {
            s18VerifiedBadge.className = 'badge bg-success';
            s18VerifiedBadge.innerText = 'Đã xác minh OTP';
            s18DevBanner?.classList.add('d-none');
        }
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

            document.querySelectorAll('.js-delete-notif').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const notificationId = parseInt(btn.getAttribute('data-id') || '0', 10);
                    if (!notificationId) return;

                    const ok = await confirmDialog({
                        title: `Xóa thông báo #${notificationId}?`,
                        text: 'Thông báo này sẽ được xóa khỏi hộp thư của bạn.',
                        confirmText: 'Xóa thông báo'
                    });
                    if (!ok) return;

                    const res = await apiFetch('/S18NotificationHub/DeleteNotification', {
                        method: 'POST',
                        body: { notificationId },
                        button: btn
                    });

                    if (res.ok) {
                        window.location.reload();
                    }
                });
            });
        });
