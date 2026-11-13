/**
 * MODULE S07: LIBRARY CATALOG (s07-library-catalog.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S07
 */
import { showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const modalEl = document.getElementById('s07ShelfLocatorModal');
    const locBookTitle = document.getElementById('locBookTitle');
    const locShelfText = document.getElementById('locShelfText');
    const locLibraryText = document.getElementById('locLibraryText');
    const locBarcodeText = document.getElementById('locBarcodeText');

    let bsModal = null;
    if (modalEl && window.bootstrap) {
        bsModal = new window.bootstrap.Modal(modalEl);
    }

    document.querySelectorAll('.js-locate-shelf').forEach(btn => {
        btn.addEventListener('click', () => {
            const barcode = btn.getAttribute('data-barcode') || '';
            const title = btn.getAttribute('data-title') || '';
            const library = btn.getAttribute('data-library') || '';
            const shelf = btn.getAttribute('data-shelf') || '';
            const status = btn.getAttribute('data-status') || 'Available';

            if (locBookTitle) locBookTitle.innerText = title;
            if (locShelfText) locShelfText.innerText = shelf;
            if (locLibraryText) locLibraryText.innerText = library;
            if (locBarcodeText) locBarcodeText.innerText = `${barcode} • Trạng thái: ${status}`;

            if (bsModal) {
                bsModal.show();
            }

            showSystemStatus(
                200,
                `Định vị kệ sách [${barcode}] thành công (S07)`,
                `Sách "${title}" nằm tại [${shelf}] thuộc ${library} (Trạng thái: ${status}).`
            );
        });
    });
});
