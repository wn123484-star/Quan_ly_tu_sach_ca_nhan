/**
 * MODULE S09: P2P LOAN FLOW (s09-p2p-loan-flow.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S09
 */
import { apiFetch, celebrate } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Tạo yêu cầu mượn P2P
    const createForm = document.getElementById('s09CreateP2PForm');
    if (createForm) {
        createForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(document.getElementById('s09BookCopyId')?.value || '0', 10);
            const pickupHubId = parseInt(document.getElementById('s09PickupHubId')?.value || '3', 10);

            const res = await apiFetch('/S09P2PLoanFlow/CreateRequest', {
                method: 'POST',
                body: { bookCopyId, pickupHubId, desiredDays: 14 }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 2. Chủ sách Duyệt hoặc Từ chối P2P
    document.querySelectorAll('.js-s09-approve').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const approve = btn.getAttribute('data-approve') === 'true';

            const res = await apiFetch('/S09P2PLoanFlow/ApproveOrReject', {
                method: 'POST',
                body: { loanRequestId, approve }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 3. Chuyển bước giao nhận P2P (InTransit -> HandoverBorrowed -> Returned)
    document.querySelectorAll('.js-s09-stage').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const stage = btn.getAttribute('data-stage') || 'InTransit';

            const res = await apiFetch('/S09P2PLoanFlow/AdvanceStage', {
                method: 'POST',
                body: { loanRequestId, stage }
            });

            if (res.ok) {
                if (stage === 'Returned') {
                    // Giao dịch P2P hoàn tất: bắn pháo hoa chúc mừng trước khi tải lại
                    await celebrate();
                    setTimeout(() => window.location.reload(), 1800);
                    return;
                }
                window.location.reload();
            }
        });
    });
});
