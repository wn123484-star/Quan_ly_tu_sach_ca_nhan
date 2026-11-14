import { apiFetch, openBarcodeScanner, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const scanInput = document.getElementById('scanBarcodeInput');
    const scanAction = document.getElementById('scanDeskAction');

    // Quét Barcode bản sao bằng Camera (html5-qrcode) và tự điền vào ô nhập
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
                }
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

        const res = await apiFetch('/S21LibrarianDesk/QuickScanBarcode', {
            method: 'POST',
            body: {
                barcode,
                deskAction,
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
            body: {
                bookId,
                libraryId,
                quantity,
                condition,
                shelfLocation
            }
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
                body: {
                    bookCopyId,
                    newStatus,
                    newCondition
                }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });
});
