/**
 * MODULE S09: P2P LOAN FLOW (s09-p2p-loan-flow.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S09
 */
import { apiFetch, celebrate, getRecaptchaToken } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // [BS-RENTAL-FLOW] Tính nhanh tổng tạm giữ Escrow P2P theo số ngày (7 / 14 / 30 ngày)
    const p2pCopySel = document.getElementById('s09BookCopyId');
    const p2pDaysSel = document.getElementById('s09DesiredDays');
    const p2pPrevEl = document.getElementById('s09EscrowPreview');
    const updateS09Preview = () => {
        if (!p2pCopySel || !p2pPrevEl) return;
        const opt = p2pCopySel.selectedOptions[0];
        const fee = parseFloat(opt?.getAttribute('data-daily-fee') || '0') || 0;
        const dep = parseFloat(opt?.getAttribute('data-deposit') || '0') || 0;
        const days = parseInt(p2pDaysSel?.value || '14', 10) || 14;
        const totalFee = fee * days;
        const grand = totalFee + dep;
        p2pPrevEl.innerText = grand > 0
            ? `💰 Tạm giữ Ví Escrow (${days} ngày): Phí thuê ${totalFee.toLocaleString('vi-VN')}đ + Cọc ${dep.toLocaleString('vi-VN')}đ = ${grand.toLocaleString('vi-VN')} VNĐ (Hoàn 100% cọc khi trả nguyên vẹn)`
            : '🎁 Chủ sách chia sẻ miễn phí 0 VNĐ';
    };
    p2pCopySel?.addEventListener('change', updateS09Preview);
    p2pDaysSel?.addEventListener('change', updateS09Preview);
    updateS09Preview();

    // 1. Tạo yêu cầu thuê / mượn P2P ([BS-SEC-RECAPTCHA])
    const createForm = document.getElementById('s09CreateP2PForm');
    if (createForm) {
        createForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(document.getElementById('s09BookCopyId')?.value || '0', 10);
            const pickupHubId = parseInt(document.getElementById('s09PickupHubId')?.value || '3', 10);
            const desiredDays = parseInt(document.getElementById('s09DesiredDays')?.value || '14', 10);
            const recaptchaToken = await getRecaptchaToken('p2p_loan');

            const res = await apiFetch('/S09P2PLoanFlow/CreateRequest', {
                method: 'POST',
                body: { bookCopyId, pickupHubId, desiredDays, recaptchaToken }
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

    // 4. [BS-RENTAL-CONTRACT] Modal Phiếu Hợp Đồng Thuê P2P Điện Tử & In A5/80mm
    const p2pModalEl = document.getElementById('bsP2PReceiptModal');
    const p2pModal = p2pModalEl && window.bootstrap ? new window.bootstrap.Modal(p2pModalEl) : null;
    document.querySelectorAll('.js-open-p2p-receipt').forEach(btn => {
        btn.addEventListener('click', () => {
            const reqId = btn.getAttribute('data-req-id') || '';
            const loanId = btn.getAttribute('data-loan-id') || '';
            const barcode = btn.getAttribute('data-barcode') || '';
            const fee = parseFloat(btn.getAttribute('data-rental-fee') || '0') || 0;
            const dep = parseFloat(btn.getAttribute('data-deposit') || '0') || 0;
            const payload = `BOOKSHARE-P2P-RENTAL|REQ:${reqId}|LOAN:${loanId}|BARCODE:${barcode}`;

            const qrEl = document.getElementById('p2pRcQrImg');
            if (qrEl) qrEl.src = `https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=${encodeURIComponent(payload)}`;
            const setTxt = (id, v) => { const el = document.getElementById(id); if (el) el.innerText = v; };
            setTxt('p2pRcBarcode', barcode);
            setTxt('p2pRcRef', `P2P #${reqId} / Loan #${loanId}`);
            setTxt('p2pRcTitle', btn.getAttribute('data-title') || '');
            setTxt('p2pRcOwner', btn.getAttribute('data-owner') || '');
            setTxt('p2pRcBorrower', btn.getAttribute('data-borrower') || '');
            setTxt('p2pRcHub', btn.getAttribute('data-hub') || '');
            setTxt('p2pRcDue', `${btn.getAttribute('data-days') || '14'} ngày • Hạn trả: ${btn.getAttribute('data-due') || ''}`);
            setTxt('p2pRcFee', fee > 0 ? `${fee.toLocaleString('vi-VN')} VNĐ` : 'Miễn phí 0 VNĐ');
            setTxt('p2pRcDeposit', `${dep.toLocaleString('vi-VN')} VNĐ`);
            setTxt('p2pRcEscrow', btn.getAttribute('data-escrow') || 'None');
            p2pModal?.show();
        });
    });
    document.getElementById('btnPrintP2PReceipt')?.addEventListener('click', () => window.print());
});
