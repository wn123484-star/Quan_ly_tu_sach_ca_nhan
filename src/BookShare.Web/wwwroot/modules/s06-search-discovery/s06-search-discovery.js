/**
 * MODULE S06: SEARCH & DISCOVERY (s06-search-discovery.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S06
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const modalEl = document.getElementById('s06QuickLoanModal');
    const loanForm = document.getElementById('s06QuickLoanForm');
    const copyIdInput = document.getElementById('loanCopyIdInput');
    const modalBookTitle = document.getElementById('modalBookTitle');
    const modalBarcode = document.getElementById('modalBarcode');
    const modalOwnerName = document.getElementById('modalOwnerName');
    const loanDesiredDays = document.getElementById('loanDesiredDays');
    const loanPickupHubId = document.getElementById('loanPickupHubId');
    const loanRequestNote = document.getElementById('loanRequestNote');

    let bsModal = null;
    if (modalEl && window.bootstrap) {
        bsModal = new window.bootstrap.Modal(modalEl);
    }

    // 1. Mở hộp thoại đăng ký mượn sách vật lý
    document.querySelectorAll('.js-open-loan-modal').forEach(btn => {
        btn.addEventListener('click', () => {
            const copyId = btn.getAttribute('data-copy-id') || '';
            const barcode = btn.getAttribute('data-barcode') || '';
            const title = btn.getAttribute('data-title') || '';
            const owner = btn.getAttribute('data-owner') || '';

            if (copyIdInput) copyIdInput.value = copyId;
            if (modalBookTitle) modalBookTitle.innerText = title;
            if (modalBarcode) modalBarcode.innerText = barcode;
            if (modalOwnerName) modalOwnerName.innerText = owner;
            if (loanRequestNote) {
                loanRequestNote.value = `Đăng ký mượn bản sao [${barcode}] - ${title}. Cam kết bảo quản sách tốt và trả đúng hạn.`;
            }

            if (bsModal) {
                bsModal.show();
            }
        });
    });

    // 2. Gửi phiếu yêu cầu mượn sách (LoanRequests)
    if (loanForm) {
        loanForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(copyIdInput?.value || '0', 10);
            const desiredDays = parseInt(loanDesiredDays?.value || '14', 10);
            const pickupHubId = parseInt(loanPickupHubId?.value || '3', 10);
            const requestNote = loanRequestNote?.value.trim() || '';

            if (!bookCopyId) {
                showSystemStatus(400, 'Thiếu mã bản sao (Mã 400)', 'Vui lòng chọn một bản sao sách hợp lệ.');
                return;
            }

            const res = await apiFetch('/S06SearchDiscovery/QuickRequestLoan', {
                method: 'POST',
                body: {
                    bookCopyId,
                    desiredDays,
                    pickupHubId,
                    requestNote
                }
            });

            if (res.ok && res.data) {
                if (bsModal) {
                    bsModal.hide();
                }
                showSystemStatus(200, res.data.title || 'Đã gửi yêu cầu mượn sách (Mã 200)', res.data.message);
            }
        });
    }
});
