/**
 * MODULE S08: LIBRARY LOAN FLOW (s08-library-loan-flow.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S08
 */
import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Tạo yêu cầu mượn Kho chung mới
    const createForm = document.getElementById('s08CreateRequestForm');
    if (createForm) {
        createForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(document.getElementById('s08BookCopyId')?.value || '0', 10);
            const desiredDays = parseInt(document.getElementById('s08DesiredDays')?.value || '14', 10);

            const res = await apiFetch('/S08LibraryLoanFlow/CreateRequest', {
                method: 'POST',
                body: { bookCopyId, desiredDays }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 2. Thủ thư Duyệt hoặc Từ chối phiếu mượn
    document.querySelectorAll('.js-s08-approve').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const approve = btn.getAttribute('data-approve') === 'true';

            const res = await apiFetch('/S08LibraryLoanFlow/ApproveOrReject', {
                method: 'POST',
                body: { loanRequestId, approve }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 3. Xác nhận nhận sách tại Thư viện (Tạo Loan & đổi sang Borrowed)
    document.querySelectorAll('.js-s08-handover').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);

            const res = await apiFetch('/S08LibraryLoanFlow/ConfirmHandover', {
                method: 'POST',
                body: { loanRequestId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 4. Xác nhận trả sách & kiểm tra tình trạng
    document.querySelectorAll('.js-s08-return').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const condSelect = document.getElementById(`returnCond-${loanRequestId}`);
            const returnCondition = condSelect?.value || 'Good';

            const res = await apiFetch('/S08LibraryLoanFlow/ConfirmReturn', {
                method: 'POST',
                body: { loanRequestId, returnCondition }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });
});
