import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. User đăng ký ký gửi sách P2P tại Trạm Hub
    document.getElementById('formRegisterDropoff')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('dropoffCopyId')?.value || '0', 10);
        const hubId = parseInt(document.getElementById('dropoffHubId')?.value || '3', 10);

        const res = await apiFetch('/S20ExchangeHubClub/RegisterDropoff', {
            method: 'POST',
            body: { bookCopyId, hubId }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 2. Curator xác nhận kiểm định sách thật ngoài đời ([Authorize(Roles = "Curator,Admin")])
    document.getElementById('formCuratorVerify')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('verifyCopyId')?.value || '0', 10);
        const verifiedCondition = document.getElementById('verifyCondition')?.value || 'Good';
        const shelfLocation = document.getElementById('verifyShelfLocation')?.value || 'Trạm CLB-K1';

        const res = await apiFetch('/S20ExchangeHubClub/VerifyPhysicalBook', {
            method: 'POST',
            body: {
                bookCopyId,
                hubId: 3,
                verifiedCondition,
                shelfLocation
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 3. Curator xác nhận ký gửi / bàn giao / thu hồi trung gian tại Hub
    document.querySelectorAll('.js-hub-action').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const actionType = btn.getAttribute('data-action') || 'CheckInDropoff';

            const res = await apiFetch('/S20ExchangeHubClub/ProcessHubHandover', {
                method: 'POST',
                body: { loanRequestId, actionType }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });
});
