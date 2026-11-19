/**
 * MODULE S08: LIBRARY LOAN FLOW (s08-library-loan-flow.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S08
 */
import { apiFetch, confirmDialog, getRecaptchaToken } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // [BS-RENTAL-FLOW] Tính nhanh tổng tạm giữ Escrow khi chọn sách & số ngày
    const copySel = document.getElementById('s08BookCopyId');
    const daysSel = document.getElementById('s08DesiredDays');
    const previewEl = document.getElementById('s08EscrowPreview');
    const updateS08Preview = () => {
        if (!copySel || !previewEl) return;
        const opt = copySel.selectedOptions[0];
        const fee = parseFloat(opt?.getAttribute('data-daily-fee') || '0') || 0;
        const dep = parseFloat(opt?.getAttribute('data-deposit') || '0') || 0;
        const days = parseInt(daysSel?.value || '14', 10) || 14;
        const totalFee = fee * days;
        const grand = totalFee + dep;
        previewEl.innerText = grand > 0
            ? `💰 Dự kiến tạm giữ Escrow (${days} ngày): Phí thuê ${totalFee.toLocaleString('vi-VN')}đ + Cọc ${dep.toLocaleString('vi-VN')}đ = ${grand.toLocaleString('vi-VN')} VNĐ (Hoàn 100% cọc khi trả nguyên vẹn)`
            : '🎁 Bản sao thuộc Kho học thuật Miễn phí 0 VNĐ (Không cần cọc Escrow)';
    };
    copySel?.addEventListener('change', updateS08Preview);
    daysSel?.addEventListener('change', updateS08Preview);
    updateS08Preview();

    // 1. Tạo yêu cầu thuê / mượn Kho chung mới ([BS-SEC-RECAPTCHA])
    const createForm = document.getElementById('s08CreateRequestForm');
    if (createForm) {
        createForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(document.getElementById('s08BookCopyId')?.value || '0', 10);
            const desiredDays = parseInt(document.getElementById('s08DesiredDays')?.value || '14', 10);
            const recaptchaToken = await getRecaptchaToken('library_loan');

            const res = await apiFetch('/S08LibraryLoanFlow/CreateRequest', {
                method: 'POST',
                body: { bookCopyId, desiredDays, recaptchaToken }
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

                document.querySelectorAll('.js-s08-cancel').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
                        if (!loanRequestId) return;

                        const ok = await confirmDialog({
                            title: `Hủy phiếu yêu cầu #${loanRequestId}?`,
                            text: 'Phiếu yêu cầu mượn đang chờ này sẽ được xóa khỏi hệ thống.',
                            confirmText: 'Xác nhận hủy'
                        });
                        if (!ok) return;

                        const res = await apiFetch('/S08LibraryLoanFlow/CancelRequest', {
                            method: 'POST',
                            body: { loanRequestId },
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });

                // [BS-RENTAL-CONTRACT] Mở Modal Phiếu Hợp Đồng Thuê Sách Điện Tử & In A5/80mm
                const receiptModalEl = document.getElementById('bsRentalReceiptModal');
                const receiptModal = receiptModalEl && window.bootstrap ? new window.bootstrap.Modal(receiptModalEl) : null;
                document.querySelectorAll('.js-open-rental-receipt').forEach(btn => {
                    btn.addEventListener('click', () => {
                        const reqId = btn.getAttribute('data-req-id') || '';
                        const loanId = btn.getAttribute('data-loan-id') || '';
                        const barcode = btn.getAttribute('data-barcode') || '';
                        const fee = parseFloat(btn.getAttribute('data-rental-fee') || '0') || 0;
                        const dep = parseFloat(btn.getAttribute('data-deposit') || '0') || 0;
                        const payload = `BOOKSHARE-RENTAL|REQ:${reqId}|LOAN:${loanId}|BARCODE:${barcode}`;

                        const qrEl = document.getElementById('rcQrImg');
                        if (qrEl) qrEl.src = `https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=${encodeURIComponent(payload)}`;
                        const setTxt = (id, v) => { const el = document.getElementById(id); if (el) el.innerText = v; };
                        setTxt('rcBarcode', barcode);
                        setTxt('rcRef', `Phiếu #${reqId} / Loan #${loanId}`);
                        setTxt('rcTitle', btn.getAttribute('data-title') || '');
                        setTxt('rcOwner', btn.getAttribute('data-owner') || '');
                        setTxt('rcBorrower', btn.getAttribute('data-borrower') || '');
                        setTxt('rcDue', `${btn.getAttribute('data-days') || '14'} ngày • Hạn trả: ${btn.getAttribute('data-due') || ''}`);
                        setTxt('rcFee', fee > 0 ? `${fee.toLocaleString('vi-VN')} VNĐ` : 'Miễn phí 0 VNĐ');
                        setTxt('rcDeposit', `${dep.toLocaleString('vi-VN')} VNĐ`);
                        setTxt('rcEscrow', btn.getAttribute('data-escrow') || 'None');
                        receiptModal?.show();
                    });
                });
                document.getElementById('btnPrintRentalReceipt')?.addEventListener('click', () => window.print());
            });
