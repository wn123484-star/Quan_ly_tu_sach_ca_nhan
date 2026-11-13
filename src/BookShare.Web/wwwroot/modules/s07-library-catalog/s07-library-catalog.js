/**
 * MODULE S07: LIBRARY CATALOG (s07-library-catalog.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S07
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const modalEl = document.getElementById('s07ShelfLocatorModal');
    const locCopyIdInput = document.getElementById('locCopyIdInput');
    const locNewShelfInput = document.getElementById('locNewShelfInput');
    const locBookTitle = document.getElementById('locBookTitle');
    const locShelfText = document.getElementById('locShelfText');
    const locLibraryText = document.getElementById('locLibraryText');
    const locBarcodeText = document.getElementById('locBarcodeText');
    const updateForm = document.getElementById('s07UpdateShelfForm');

    let bsModal = null;
    if (modalEl && window.bootstrap) {
        bsModal = new window.bootstrap.Modal(modalEl);
    }

    document.querySelectorAll('.js-locate-shelf').forEach(btn => {
        btn.addEventListener('click', () => {
            const copyId = btn.getAttribute('data-copy-id') || '';
            const barcode = btn.getAttribute('data-barcode') || '';
            const title = btn.getAttribute('data-title') || '';
            const library = btn.getAttribute('data-library') || '';
            const shelf = btn.getAttribute('data-shelf') || '';
            const status = btn.getAttribute('data-status') || 'Available';

            if (locCopyIdInput) locCopyIdInput.value = copyId;
            if (locNewShelfInput) locNewShelfInput.value = shelf;
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

    if (updateForm) {
        updateForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(locCopyIdInput?.value || '0', 10);
            const shelfLocation = locNewShelfInput?.value.trim() || '';

            const res = await apiFetch('/S07LibraryCatalog/UpdateShelfLocation', {
                method: 'POST',
                body: { bookCopyId, shelfLocation }
            });

            if (res.ok) {
                if (bsModal) bsModal.hide();
                window.location.reload();
            } else if (res.status === 403 && bsModal) {
                bsModal.hide();
            }
        });
    }
});
