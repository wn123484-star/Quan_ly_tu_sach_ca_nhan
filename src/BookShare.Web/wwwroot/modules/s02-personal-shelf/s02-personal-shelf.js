/**
 * MODULE S02: PERSONAL SHELF MANAGEMENT (s02-personal-shelf.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S02
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Bật/tắt công tắc cho phép mượn P2P (IsLendable)
    document.querySelectorAll('.js-toggle-lendable').forEach(toggle => {
        toggle.addEventListener('change', async (e) => {
            const copyId = parseInt(e.target.getAttribute('data-copy-id'), 10);
            const isLendable = e.target.checked;
            const bar = document.getElementById(`lendableBar-${copyId}`);
            const label = document.getElementById(`lendableText-${copyId}`);

            const res = await apiFetch('/S02PersonalShelf/ToggleLendable', {
                method: 'POST',
                body: { bookCopyId: copyId, isLendable }
            });

            if (res.ok && res.data) {
                if (bar) {
                    bar.classList.toggle('is-locked', !isLendable);
                }
                if (label) {
                    label.className = isLendable ? 'text-success fw-bold small' : 'text-muted small';
                    label.innerText = isLendable
                        ? 'Đang mở cho cộng đồng mượn P2P'
                        : 'Đang khóa (Chỉ lưu trữ cá nhân)';
                }
                const statEl = document.getElementById('s02LendableTotal');
                if (statEl && typeof res.data.lendableCount === 'number') {
                    statEl.innerText = `${res.data.lendableCount} bản sao`;
                }
                showSystemStatus(200, 'Cập nhật trạng thái IsLendable thành công (Mã 200)', res.data.message);
            } else {
                // Hoàn tác trạng thái nút gạt nếu lỗi
                e.target.checked = !isLendable;
            }
        });
    });

    // 2. Tạo kệ sách tùy chỉnh mới
    const createShelfForm = document.getElementById('s02CreateShelfForm');
    if (createShelfForm) {
        createShelfForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('newShelfName')?.value.trim() || '';
            const description = document.getElementById('newShelfDesc')?.value.trim() || '';
            const isPublic = document.getElementById('newShelfPublic')?.checked ?? true;

            if (!name) {
                showSystemStatus(400, 'Thiếu tên kệ sách (Mã 400)', 'Vui lòng nhập tên kệ sách tùy chỉnh.');
                return;
            }

            const res = await apiFetch('/S02PersonalShelf/CreateShelf', {
                method: 'POST',
                body: { name, description, isPublic }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 3. Thêm bản sao sách mới vào tủ cá nhân & kệ sách
    const addBookToShelfForm = document.getElementById('s02AddBookCopyForm');
    if (addBookToShelfForm) {
        addBookToShelfForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookId = parseInt(document.getElementById('selectBookId')?.value || '0', 10);
            const shelfId = parseInt(document.getElementById('selectShelfId')?.value || '0', 10);
            const condition = document.getElementById('selectCondition')?.value || 'Good';
            const isLendable = document.getElementById('checkIsLendable')?.checked ?? true;

            if (!bookId) {
                showSystemStatus(400, 'Chưa chọn đầu sách (Mã 400)', 'Vui lòng chọn một đầu sách để thêm vào tủ sách cá nhân.');
                return;
            }

            const res = await apiFetch('/S02PersonalShelf/AddBookToShelf', {
                method: 'POST',
                body: { bookId, shelfId, condition, isLendable }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 4. Cập nhật tình trạng & kệ của bản sao sách (Update CRUD)
    document.querySelectorAll('.js-update-copy').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookCopyId = parseInt(btn.getAttribute('data-copy-id') || '0', 10);
            const condition = document.getElementById(`editCond-${bookCopyId}`)?.value || 'Good';
            const shelfId = parseInt(document.getElementById(`editShelf-${bookCopyId}`)?.value || '0', 10);

            const res = await apiFetch('/S02PersonalShelf/UpdateCopy', {
                method: 'POST',
                body: { bookCopyId, condition, shelfId: shelfId > 0 ? shelfId : null }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 5. Xóa bản sao sách khỏi tủ cá nhân (Delete CRUD)
    document.querySelectorAll('.js-delete-copy').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookCopyId = parseInt(btn.getAttribute('data-copy-id') || '0', 10);
            const barcode = btn.getAttribute('data-barcode') || '';
            if (!bookCopyId) return;

            const res = await apiFetch('/S02PersonalShelf/DeleteCopy', {
                method: 'POST',
                body: { bookCopyId }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    });

    // 6. Xóa kệ sách tùy chỉnh
    document.querySelectorAll('.js-delete-shelf').forEach(btn => {
        btn.addEventListener('click', async () => {
            const shelfId = parseInt(btn.getAttribute('data-shelf-id') || '0', 10);
            if (!shelfId) return;

            const res = await apiFetch('/S02PersonalShelf/DeleteShelf', {
                method: 'POST',
                body: { shelfId }
            });

            if (res.ok) {
                window.location.href = '/S02PersonalShelf';
            }
        });
    });
});
