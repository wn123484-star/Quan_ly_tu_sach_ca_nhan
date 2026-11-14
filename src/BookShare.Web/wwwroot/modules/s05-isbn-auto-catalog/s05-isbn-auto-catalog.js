/**
 * MODULE S05: ISBN AUTO CATALOG (s05-isbn-auto-catalog.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S05
 */
import { apiFetch, showSystemStatus, openBarcodeScanner } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const isbnQueryInput = document.getElementById('isbnQueryInput');
    const btnLookupIsbn = document.getElementById('btnLookupIsbn');
    const sourceBadge = document.getElementById('s05LookupSourceBadge');

    const catIsbn = document.getElementById('catIsbn');
    const catTitle = document.getElementById('catTitle');
    const catAuthor = document.getElementById('catAuthor');
    const catPublisher = document.getElementById('catPublisher');
    const catPublishedYear = document.getElementById('catPublishedYear');
    const catPageCount = document.getElementById('catPageCount');
    const catCategoryId = document.getElementById('catCategoryId');
    const catCoverUrl = document.getElementById('catCoverUrl');
    const catSummary = document.getElementById('catSummary');

    const previewCoverImg = document.getElementById('previewCoverImg');
    const previewTitle = document.getElementById('previewTitle');
    const previewAuthor = document.getElementById('previewAuthor');
    const previewIsbn = document.getElementById('previewIsbn');
    const previewPages = document.getElementById('previewPages');

    function syncPreviewCard() {
        if (previewTitle && catTitle) previewTitle.innerText = catTitle.value || 'Chưa có tiêu đề';
        if (previewAuthor && catAuthor) previewAuthor.innerText = catAuthor.value || 'Chưa rõ tác giả';
        if (previewIsbn && catIsbn) previewIsbn.innerText = `ISBN: ${catIsbn.value}`;
        if (previewPages && catPageCount) previewPages.innerText = `${catPageCount.value || 0} trang`;
        if (previewCoverImg && catCoverUrl && catCoverUrl.value.trim()) {
            previewCoverImg.src = catCoverUrl.value.trim();
        }
    }

    [catIsbn, catTitle, catAuthor, catPageCount, catCoverUrl].forEach(el => {
        el?.addEventListener('input', syncPreviewCard);
    });

    async function performIsbnLookup(isbnValue) {
        if (!isbnValue) {
            showSystemStatus(400, 'Thiếu mã ISBN (Mã 400)', 'Vui lòng nhập mã vạch ISBN-10 hoặc ISBN-13.');
            return;
        }

        const cleanIsbn = isbnValue.replace(/[^0-9Xx]/g, '');
        if (!/^(\d{9}[\dXx]|\d{13})$/.test(cleanIsbn)) {
            showSystemStatus(400, 'Mã ISBN không hợp lệ (Mã 400)', 'ISBN phải gồm 10 ký tự (ISBN-10) hoặc 13 chữ số (ISBN-13).');
            return;
        }

        // Khóa nút Lưu trong lúc tra cứu để không lưu nhầm dữ liệu cũ còn trên form
        const btnSaveCatalog = document.querySelector('#s05SaveCatalogForm [type="submit"]');
        if (btnSaveCatalog) btnSaveCatalog.disabled = true;

        if (btnLookupIsbn) {
            btnLookupIsbn.disabled = true;
            btnLookupIsbn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang tra cứu...';
        }

        const res = await apiFetch('/S05IsbnAutoCatalog/LookupIsbn', {
            method: 'POST',
            body: { isbn: isbnValue }
        });

        if (btnSaveCatalog) btnSaveCatalog.disabled = false;
        if (btnLookupIsbn) {
            btnLookupIsbn.disabled = false;
            btnLookupIsbn.innerHTML = '<i class="bi bi-magic me-1"></i>Tra Cứu & Tự Điền';
        }

        if (res.ok && res.data?.data) {
            const info = res.data.data;
            if (catIsbn) catIsbn.value = info.isbn || isbnValue;
            if (catTitle) catTitle.value = info.title || '';
            if (catAuthor) catAuthor.value = info.author || '';
            if (catPublisher) catPublisher.value = info.publisher || '';
            if (catPublishedYear) catPublishedYear.value = info.publishedYear || 2024;
            if (catPageCount) catPageCount.value = info.pageCount || 300;
            if (catCategoryId && info.categoryId) catCategoryId.value = String(info.categoryId);
            if (catCoverUrl) catCoverUrl.value = info.coverImageUrl || '';
            if (catSummary) catSummary.value = info.summary || '';

            if (sourceBadge) {
                sourceBadge.innerText = info.alreadyExistsInDb
                    ? `Nguồn: ${info.source} (Đã có đầu sách #${info.existingBookId})`
                    : `Nguồn: ${info.source} (Đầu sách mới)`;
            }

            syncPreviewCard();
            showSystemStatus(200, res.data.title || 'Tra cứu ISBN thành công (Mã 200)', res.data.message);
        }
    }

    // 1. Nút Tra cứu ISBN
    if (btnLookupIsbn) {
        btnLookupIsbn.addEventListener('click', () => {
            const isbn = isbnQueryInput?.value.trim() || '';
            performIsbnLookup(isbn);
        });
    }

    // 1b. Quét mã ISBN bằng Camera (html5-qrcode) rồi tự tra cứu
    document.getElementById('btnScanIsbnCamera')?.addEventListener('click', async () => {
        const code = await openBarcodeScanner({ title: 'Quét mã ISBN', hint: 'Đưa mã vạch ISBN (EAN-13) ở bìa sau cuốn sách vào khung' });
        if (!code) return;
        if (isbnQueryInput) isbnQueryInput.value = code;
        performIsbnLookup(code);
    });

    // 2. Bấm vào các chip ISBN mẫu
    document.querySelectorAll('.js-isbn-preset').forEach(chip => {
        chip.addEventListener('click', () => {
            const presetIsbn = chip.getAttribute('data-isbn') || '';
            if (isbnQueryInput) isbnQueryInput.value = presetIsbn;
            performIsbnLookup(presetIsbn);
        });
    });

    // 3. Lưu đầu sách vào bảng Books + Tạo bản sao BookCopies
    const saveCatalogForm = document.getElementById('s05SaveCatalogForm');
    if (saveCatalogForm) {
        saveCatalogForm.addEventListener('submit', async (e) => {
            e.preventDefault();

            const payload = {
                isbn: catIsbn?.value.trim() || '',
                title: catTitle?.value.trim() || '',
                author: catAuthor?.value.trim() || '',
                publisher: catPublisher?.value.trim() || '',
                publishedYear: parseInt(catPublishedYear?.value || '2024', 10),
                pageCount: parseInt(catPageCount?.value || '280', 10),
                categoryId: parseInt(catCategoryId?.value || '1', 10),
                coverImageUrl: catCoverUrl?.value.trim() || '',
                summary: catSummary?.value.trim() || '',
                targetOwnerType: document.getElementById('catTargetOwnerType')?.value || 'User',
                libraryId: parseInt(document.getElementById('catLibraryId')?.value || '1', 10),
                shelfLocation: document.getElementById('catShelfLocation')?.value.trim() || 'Kệ A1',
                condition: document.getElementById('catCondition')?.value || 'New',
                isLendable: true
            };

            const res = await apiFetch('/S05IsbnAutoCatalog/SaveBookAndCopy', {
                method: 'POST',
                body: payload
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }
});
