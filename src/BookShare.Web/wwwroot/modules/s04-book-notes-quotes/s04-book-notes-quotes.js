/**
 * MODULE S04: BOOK NOTES & QUOTES (s04-book-notes-quotes.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S04
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const saveForm = document.getElementById('s04SaveNoteForm');
    const noteIdInput = document.getElementById('noteIdInput');
    const noteBookId = document.getElementById('noteBookId');
    const notePageNumber = document.getElementById('notePageNumber');
    const noteQuoteText = document.getElementById('noteQuoteText');
    const noteContentText = document.getElementById('noteContentText');
    const noteIsPublic = document.getElementById('noteIsPublic');
    const formTitle = document.getElementById('s04FormTitle');
    const btnReset = document.getElementById('btnResetNoteForm');
    const btnSubmit = document.getElementById('btnSubmitNote');

    // 1. Lưu mới hoặc cập nhật ghi chú & trích dẫn
    if (saveForm) {
        saveForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const rawNoteId = noteIdInput?.value.trim() || '';
            const noteId = rawNoteId ? parseInt(rawNoteId, 10) : null;
            const bookId = parseInt(noteBookId?.value || '0', 10);
            const pageNumber = parseInt(notePageNumber?.value || '1', 10);
            const quoteText = noteQuoteText?.value.trim() || '';
            const noteContent = noteContentText?.value.trim() || '';
            const isPublic = noteIsPublic?.checked ?? true;

            if (!bookId || !noteContent) {
                showSystemStatus(400, 'Thiếu nội dung ghi chú (Mã 400)', 'Vui lòng chọn sách và nhập nội dung ghi chú cá nhân.');
                return;
            }

            const res = await apiFetch('/S04BookNotesQuotes/SaveNote', {
                method: 'POST',
                body: {
                    noteId,
                    bookId,
                    pageNumber,
                    quoteText,
                    noteContent,
                    isPublic
                }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 2. Nạp dữ liệu ghi chú lên form để chỉnh sửa
    document.querySelectorAll('.js-edit-note').forEach(btn => {
        btn.addEventListener('click', () => {
            const id = btn.getAttribute('data-note-id') || '';
            const bookId = btn.getAttribute('data-book-id') || '';
            const page = btn.getAttribute('data-page') || '1';
            const quote = btn.getAttribute('data-quote') || '';
            const content = btn.getAttribute('data-content') || '';
            const isPublic = btn.getAttribute('data-public') === 'true';

            if (noteIdInput) noteIdInput.value = id;
            if (noteBookId) noteBookId.value = bookId;
            if (notePageNumber) notePageNumber.value = page;
            if (noteQuoteText) noteQuoteText.value = quote;
            if (noteContentText) noteContentText.value = content;
            if (noteIsPublic) noteIsPublic.checked = isPublic;

            if (formTitle) {
                formTitle.innerHTML = `<i class="bi bi-pencil-square text-warning me-2"></i>Đang Sửa Ghi Chú #${id} (Trang ${page})`;
            }
            if (btnSubmit) {
                btnSubmit.innerHTML = `<i class="bi bi-check2-circle me-1"></i>Cập Nhật Ghi Chú #${id}`;
            }
            if (btnReset) {
                btnReset.classList.remove('d-none');
            }
            noteContentText?.focus();
        });
    });

    if (btnReset) {
        btnReset.addEventListener('click', () => {
            if (noteIdInput) noteIdInput.value = '';
            if (noteQuoteText) noteQuoteText.value = '';
            if (noteContentText) noteContentText.value = '';
            if (notePageNumber) notePageNumber.value = '45';
            if (noteIsPublic) noteIsPublic.checked = true;

            if (formTitle) {
                formTitle.innerHTML = `<i class="bi bi-pen-fill text-primary me-2"></i>Viết Ghi Chú & Trích Dẫn Mới`;
            }
            if (btnSubmit) {
                btnSubmit.innerHTML = `<i class="bi bi-journal-check me-1"></i>Lưu Vào Sổ Tay Ghi Chú`;
            }
            btnReset.classList.add('d-none');
        });
    }

    // 3. Bật / tắt chia sẻ công khai (IsPublic)
    document.querySelectorAll('.js-toggle-note-public').forEach(toggle => {
        toggle.addEventListener('change', async (e) => {
            const noteId = parseInt(e.target.getAttribute('data-note-id'), 10);
            const isPublic = e.target.checked;
            const badge = document.getElementById(`noteBadgePublic-${noteId}`);

            const res = await apiFetch('/S04BookNotesQuotes/TogglePublic', {
                method: 'POST',
                body: { noteId, isPublic }
            });

            if (res.ok && res.data) {
                if (badge) {
                    badge.className = isPublic
                        ? 'badge bg-success bg-opacity-10 text-success'
                        : 'badge bg-secondary bg-opacity-10 text-secondary';
                    badge.innerHTML = `<i class="bi ${isPublic ? 'bi-globe2' : 'bi-lock-fill'} me-1"></i>${isPublic ? 'Công khai' : 'Riêng tư'}`;
                }
                showSystemStatus(200, 'Cập nhật chia sẻ ghi chú (Mã 200)', res.data.message);
            } else {
                e.target.checked = !isPublic;
            }
        });
    });

    // 4. Xóa ghi chú
    document.querySelectorAll('.js-delete-note').forEach(btn => {
        btn.addEventListener('click', async () => {
            const noteId = parseInt(btn.getAttribute('data-note-id') || '0', 10);
            if (!noteId) return;
            if (!confirm(`Xóa ghi chú #${noteId} khỏi sổ tay? Thao tác này không thể hoàn tác.`)) return;

            const res = await apiFetch('/S04BookNotesQuotes/DeleteNote', {
                method: 'POST',
                body: { noteId }
            });

            if (res.ok) {
                const card = document.getElementById(`noteCard-${noteId}`);
                if (card) {
                    card.remove();
                }
                showSystemStatus(200, 'Đã xóa ghi chú (Mã 200)', res.data?.message || 'Đã xóa ghi chú khỏi sổ tay.');
            }
        });
    });
});
