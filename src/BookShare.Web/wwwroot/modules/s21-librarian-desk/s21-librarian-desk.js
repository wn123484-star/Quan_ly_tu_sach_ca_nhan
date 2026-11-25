import { apiFetch, openBarcodeScanner, showSystemStatus, confirmDialog, showHandoverQrModal } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const scanInput = document.getElementById('scanBarcodeInput');
    const scanAction = document.getElementById('scanDeskAction');

    // Quét Barcode bản sao bằng Camera và tự điền vào ô nhập
    document.getElementById('btnScanBarcodeCamera')?.addEventListener('click', async () => {
        const code = await openBarcodeScanner({ title: 'Quét Barcode bản sao', hint: 'Đưa nhãn Barcode dán trên sách vào khung' });
        if (!code || !scanInput) return;
        scanInput.value = code;
        scanInput.focus();
        showSystemStatus(200, 'Đã đọc được mã từ Camera', `Barcode: ${code}. Chọn thao tác rồi bấm xử lý.`);
    });

    // Điền nhanh Barcode mẫu
    document.querySelectorAll('.js-sample-barcode').forEach(btn => {
        btn.addEventListener('click', () => {
            if (scanInput) scanInput.value = btn.getAttribute('data-code') || '';
        });
    });

    // 0a. Cho mượn sách trực tiếp tại Quầy (1 thao tác, hỗ trợ cả Kho chung & P2P)
    document.getElementById('formDirectLend')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('directLendCopyId')?.value || '0', 10);
        const borrowerUserId = document.getElementById('directLendBorrowerId')?.value || '';
        const desiredDays = parseInt(document.getElementById('directLendDays')?.value || '14', 10);
        const immediateHandover = Boolean(document.getElementById('directLendImmediate')?.checked);

        if (!bookCopyId || !borrowerUserId) {
            showSystemStatus(400, 'Thiếu thông tin cho mượn', 'Vui lòng chọn đầy đủ Bản sao sách và Độc giả.');
            return;
        }

        const res = await apiFetch('/S21LibrarianDesk/DirectLendBook', {
            method: 'POST',
            body: { bookCopyId, borrowerUserId, desiredDays, immediateHandover }
        });
        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 0b. Điều phối phiếu mượn hợp nhất (Duyệt / Từ chối / Giao sách / Nhận trả)
    document.querySelectorAll('.js-s21-loan-action').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const action = btn.getAttribute('data-action') || 'Approve';
            const returnCondition = btn.getAttribute('data-cond') || 'Good';
            if (!loanRequestId) return;

            if (action === 'Reject' || returnCondition === 'Damaged') {
                const ok = await confirmDialog({
                    title: action === 'Reject' ? `Từ chối phiếu mượn #${loanRequestId}?` : `Xác nhận sách trả bị hư hại (#${loanRequestId})?`,
                    text: action === 'Reject'
                        ? 'Phiếu yêu cầu sẽ chuyển sang trạng thái Từ chối và giải phóng bản sao sách.'
                        : 'Bản sao sẽ chuyển sang trạng thái Damaged và trừ 10 điểm uy tín độc giả.',
                    confirmText: 'Xác nhận',
                    danger: true
                });
                if (!ok) return;
            }

            const res = await apiFetch('/S21LibrarianDesk/ProcessLoanAction', {
                method: 'POST',
                body: { loanRequestId, action, returnCondition },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    // 0c. Tạo mã QR bàn giao / thu hồi ngay tại bảng điều phối Thủ thư
    document.querySelectorAll('.js-s21-qr-create').forEach(btn => {
        btn.addEventListener('click', () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const handoverType = btn.getAttribute('data-type') || 'Handover';
            if (loanRequestId) showHandoverQrModal({ loanRequestId, handoverType });
        });
    });

    // Quét thu hồi nhanh từ bảng độc giả quá hạn
    document.querySelectorAll('.js-scan-overdue').forEach(btn => {
        btn.addEventListener('click', async () => {
            const barcode = btn.getAttribute('data-code') || '';
            if (scanInput) scanInput.value = barcode;
            if (scanAction) scanAction.value = 'Checkin';

            const res = await apiFetch('/S21LibrarianDesk/QuickScanBarcode', {
                method: 'POST',
                body: {
                    barcode,
                    deskAction: 'Checkin',
                    returnCondition: 'Good'
                },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 700);
            }
        });
    });

    // 1. Quét Barcode tại quầy
    document.getElementById('formQuickScan')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const barcode = scanInput?.value.trim() || '';
        const deskAction = scanAction?.value || 'AutoDetect';
        const borrowerUserId = document.getElementById('scanBorrowerUserId')?.value || null;
        const desiredDays = parseInt(document.getElementById('scanDesiredDays')?.value || '14', 10);

        const res = await apiFetch('/S21LibrarianDesk/QuickScanBarcode', {
            method: 'POST',
            body: {
                barcode,
                deskAction,
                borrowerUserId,
                desiredDays,
                returnCondition: 'Good'
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
        }
    });

    // 2. Nhập lô bản sao mới vào kho chung
    document.getElementById('formBatchImport')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookId = parseInt(document.getElementById('importBookId')?.value || '1', 10);
        const libraryId = parseInt(document.getElementById('importLibraryId')?.value || '1', 10);
        const quantity = parseInt(document.getElementById('importQuantity')?.value || '2', 10);
        const condition = document.getElementById('importCondition')?.value || 'New';
        const shelfLocation = document.getElementById('importShelfLocation')?.value.trim() || 'Kệ CNTT-A1';

        const res = await apiFetch('/S21LibrarianDesk/BatchImportCopies', {
            method: 'POST',
            body: { bookId, libraryId, quantity, condition, shelfLocation }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 750);
        }
    });

    // 3. Chuyển trạng thái bản sao Damaged / Lost / Available
    document.querySelectorAll('.js-change-state').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookCopyId = parseInt(btn.getAttribute('data-id') || '0', 10);
            const newStatus = btn.getAttribute('data-status') || 'Damaged';
            const newCondition = btn.getAttribute('data-cond') || 'Worn';

            const res = await apiFetch('/S21LibrarianDesk/UpdateCopyState', {
                method: 'POST',
                body: { bookCopyId, newStatus, newCondition },
                button: btn
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    document.querySelectorAll('.js-delete-sys-copy').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookCopyId = parseInt(btn.getAttribute('data-id') || '0', 10);
            const barcode = btn.getAttribute('data-barcode') || '';
            if (!bookCopyId) return;

            const ok = await confirmDialog({
                title: `Thanh lý / Xóa bản sao ${barcode}?`,
                text: 'Bản sao sách kho chung chưa phát sinh hợp đồng mượn này sẽ được xóa khỏi kho.',
                confirmText: 'Xác nhận xóa',
                danger: true
            });
            if (!ok) return;

            const res = await apiFetch('/S21LibrarianDesk/DeleteSystemCopy', {
                method: 'POST',
                body: { bookCopyId },
                button: btn
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });
});
