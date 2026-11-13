/**
 * MODULE S12: LOAN RENEWAL & OVERDUE FINE (s12-loan-renewal-fine.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S12
 */
import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Gia hạn thời gian mượn (+7 ngày, tối đa 2 lần nếu không có người đặt trước)
    document.querySelectorAll('.js-s12-renew').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanId = parseInt(btn.getAttribute('data-loan-id') || '0', 10);
            if (!loanId) return;

            const res = await apiFetch('/S12LoanRenewalFine/RenewLoan', {
                method: 'POST',
                body: { loanId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 2. Tính phí phạt trễ hạn & lập phiếu thu trong bảng Transactions
    document.querySelectorAll('.js-s12-assess-fine').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanId = parseInt(btn.getAttribute('data-loan-id') || '0', 10);
            if (!loanId) return;

            const res = await apiFetch('/S12LoanRenewalFine/AssessFine', {
                method: 'POST',
                body: {
                    loanId,
                    simulatedOverdueDays: 5,
                    markPaidImmediately: true
                }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });
});
