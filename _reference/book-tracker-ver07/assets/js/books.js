// =========================================================================
// TỆP: assets/js/books.js
// Mục đích: Quản lý Thêm, Sửa, Xóa, Tìm kiếm, Lọc sách và Tạo Ghi chú AI gửi Mail
// =========================================================================

// Tự động vẽ ảnh bìa SVG nếu người dùng để trống link ảnh hoặc link ảnh bị lỗi (404)
function createFallbackCover(title, author) {
    const safeTitle = (title || 'Sách hay').substring(0, 26);
    const safeAuthor = (author || 'Tác giả').substring(0, 22);
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="260" viewBox="0 0 400 260">
        <defs>
            <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#2b5876"/>
                <stop offset="100%" stop-color="#4e4376"/>
            </linearGradient>
        </defs>
        <rect width="400" height="260" fill="url(#bg)"/>
        <rect x="20" y="20" width="360" height="220" fill="none" stroke="rgba(255,255,255,0.3)" stroke-width="2" rx="8"/>
        <text x="50%" y="46%" dominant-baseline="middle" text-anchor="middle" fill="#ffffff" font-family="sans-serif" font-size="20" font-weight="bold">${safeTitle}</text>
        <text x="50%" y="64%" dominant-baseline="middle" text-anchor="middle" fill="#e0e0e0" font-family="sans-serif" font-size="14">${safeAuthor}</text>
    </svg>`;
    return 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg);
}

// [MÃ: NV-03] Kết hợp 2 quy trình bất đồng bộ: Gọi Gemini tạo ghi chú -> Gửi tự động qua EmailJS
window.handleSendAINote = async function(bookId, btnElement) {
    const books = getBooks();
    const book = books.find(b => String(b.id) === String(bookId));
    if (!book) {
        hienThiThongBaoNgoaiLeHTML(404, 'Không tìm thấy sách (Mã 404)', 'Cuốn sách này không còn tồn tại trong bộ nhớ.');
        return;
    }

    const aiConfig = typeof getAIConfig === 'function' ? getAIConfig() : {};
    if (!aiConfig.apiKey) {
        hienThiThongBaoNgoaiLeHTML(300, 'Cần nhập Gemini API Key trước (Mã 300)', 'Hệ thống gợi ý bạn chuyển sang trang Tùy chỉnh để lưu khóa API.');
        Swal.fire({
            title: 'Chưa có Gemini API Key',
            text: 'Vui lòng vào trang Tùy chỉnh để nhập Gemini API Key trước khi tạo ghi chú AI.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Đến trang Tùy chỉnh',
            cancelButtonText: 'Đóng'
        }).then(res => {
            if (res.isConfirmed) window.location.href = 'settings.html#aiSettingsSection';
        });
        return;
    }

    // Khóa nút bấm và hiện Loading Spinner để tránh người dùng bấm nhiều lần
    const originalHTML = btnElement ? btnElement.innerHTML : '';
    if (btnElement) {
        btnElement.disabled = true;
        btnElement.classList.add('btn-loading-locked');
        btnElement.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang tạo & gửi...';
    }

    try {
        hienThiThongBaoNgoaiLeHTML(200, `AI đang phân tích sách "${book.title}"...`, 'Vui lòng chờ vài giây để Gemini tóm tắt nội dung và gửi thư.');

        // Bước 1: Gọi Gemini API sinh nội dung ghi chú cho cuốn sách
        const aiNoteText = await generateBookNote(book);

        // Lưu bản ghi chú AI vừa tạo vào thuộc tính aiNote của sách để xem lại khi cần
        book.aiNote = aiNoteText;
        saveBooks(books);

        // Bước 2: Gửi nội dung qua EmailJS nếu đã có cấu hình hợp lệ
        const emailCfg = typeof getEffectiveEmailConfig === 'function'
            ? getEffectiveEmailConfig()
            : JSON.parse(localStorage.getItem('emailConfig') || '{}');

        const hasValidEmailJS = typeof emailjs !== 'undefined' &&
            emailCfg.publicKey &&
            !emailCfg.publicKey.includes('YOUR_DEFAULT') &&
            emailCfg.serviceId &&
            emailCfg.templateId;

        if (hasValidEmailJS) {
            emailjs.init(emailCfg.publicKey);
            await emailjs.send(emailCfg.serviceId, emailCfg.templateId, {
                to_email: emailCfg.targetEmail,
                book_title: book.title,
                book_author: book.author,
                book_status: book.status,
                message: aiNoteText,
                ai_generated_note: aiNoteText,
                time: new Date().toLocaleString('vi-VN')
            });

            hienThiThongBaoNgoaiLeHTML(200, 'Đã tạo ghi chú AI & gửi thư thành công (Mã 200)', `Đã gửi ghi chú cuốn "${book.title}" tới ${emailCfg.targetEmail}.`);
            Swal.fire({
                title: 'Đã gửi thành công!',
                html: `<div class="text-start small">
                    <p class="mb-2 text-success fw-bold"><i class="bi bi-check-circle me-1"></i>Đã gửi ngầm tới hộp thư: ${emailCfg.targetEmail}</p>
                    <div class="p-3 bg-body-tertiary rounded border" style="white-space: pre-wrap; max-height: 260px; overflow-y: auto;">${aiNoteText}</div>
                </div>`,
                icon: 'success',
                confirmButtonText: 'Đóng'
            });
        } else {
            // Trường hợp người dùng chưa điền mã EmailJS thật, vẫn hiển thị trọn vẹn ghi chú AI và hướng dẫn cấu hình
            hienThiThongBaoNgoaiLeHTML(200, 'AI đã tạo ghi chú thành công (Mã 200)', 'Đã sinh xong nội dung ghi chú. Hãy điền mã EmailJS ở mục Tùy chỉnh nếu muốn gửi mail thật.');
            Swal.fire({
                title: `Ghi chú AI: ${book.title}`,
                html: `<div class="text-start small">
                    <p class="mb-2 text-muted">Nội dung do Gemini tạo (Điền thêm mã EmailJS trong trang Tùy chỉnh để tự động gửi về hộp thư):</p>
                    <div class="p-3 bg-body-tertiary rounded border" style="white-space: pre-wrap; max-height: 280px; overflow-y: auto;">${aiNoteText}</div>
                </div>`,
                icon: 'info',
                confirmButtonText: 'Đã xem'
            });
        }
    } catch (err) {
        hienThiThongBaoNgoaiLeHTML(500, 'Lỗi khi tạo ghi chú AI hoặc gửi mail (Mã 500)', err.message || JSON.stringify(err));
        Swal.fire('Không thể hoàn tất', err.message || 'Lỗi kết nối máy chủ AI hoặc EmailJS.', 'error');
    } finally {
        if (btnElement) {
            btnElement.disabled = false;
            btnElement.classList.remove('btn-loading-locked');
            btnElement.innerHTML = originalHTML;
        }
    }
};

document.addEventListener('DOMContentLoaded', () => {
    const bookForm = document.getElementById('bookForm');
    const bookGrid = document.getElementById('bookGrid');
    const searchInput = document.getElementById('searchInput');
    const filterStatus = document.getElementById('filterStatus');
    const filterPriority = document.getElementById('filterPriority');
    const sortDeadline = document.getElementById('sortDeadline');
    const btnCancelEdit = document.getElementById('btnCancelEdit');
    const btnBatchImport = document.getElementById('btnBatchImport');
    const bookCountBadge = document.getElementById('bookCountBadge');

    // Hàm kiểm tra lỗi nhập liệu trên biểu mẫu thêm/sửa sách
    function validateBookForm() {
        let isValid = true;
        const title = document.getElementById('title').value.trim();
        const author = document.getElementById('author').value.trim();
        const deadline = document.getElementById('deadline').value;
        const desc = document.getElementById('description').value.trim();

        document.getElementById('errorTitle').innerText = '';
        document.getElementById('errorAuthor').innerText = '';
        document.getElementById('errorDeadline').innerText = '';
        document.getElementById('errorDescription').innerText = '';

        if (!title) {
            document.getElementById('errorTitle').innerText = 'Vui lòng nhập tên cuốn sách.';
            isValid = false;
        }
        if (!author) {
            document.getElementById('errorAuthor').innerText = 'Vui lòng nhập tên tác giả.';
            isValid = false;
        }
        if (!deadline) {
            document.getElementById('errorDeadline').innerText = 'Vui lòng chọn ngày dự kiến đọc xong.';
            isValid = false;
        }
        if (desc.length < 10) {
            document.getElementById('errorDescription').innerText = 'Mô tả hoặc cảm nhận cần tối thiểu 10 ký tự.';
            isValid = false;
        }

        if (!isValid) {
            hienThiThongBaoNgoaiLeHTML(400, 'Dữ liệu biểu mẫu chưa hợp lệ (Mã 400)', 'Vui lòng kiểm tra các dòng chữ báo đỏ bên dưới từng ô nhập.');
        }
        return isValid;
    }

    // Hàm vẽ danh sách thẻ sách ra màn hình
    function renderBooks() {
        const allBooks = getBooks();
        const keyword = (searchInput.value || '').trim().toLowerCase();
        const statusVal = filterStatus.value;
        const priorityVal = filterPriority.value;
        const sortVal = sortDeadline.value;

        let filtered = allBooks.filter(b => {
            const matchKeyword = !keyword ||
                (b.title && b.title.toLowerCase().includes(keyword)) ||
                (b.author && b.author.toLowerCase().includes(keyword)) ||
                (b.description && b.description.toLowerCase().includes(keyword));
            const matchStatus = (statusVal === 'all') || (b.status === statusVal);
            const matchPriority = (priorityVal === 'all') || (b.priority === priorityVal);
            return matchKeyword && matchStatus && matchPriority;
        });

        if (sortVal === 'asc') {
            filtered.sort((a, b) => new Date(a.deadline || '2099-12-31') - new Date(b.deadline || '2099-12-31'));
        } else if (sortVal === 'desc') {
            filtered.sort((a, b) => new Date(b.deadline || '1970-01-01') - new Date(a.deadline || '1970-01-01'));
        }

        if (bookCountBadge) {
            bookCountBadge.innerText = `${filtered.length} cuốn`;
        }

        if (filtered.length === 0) {
            bookGrid.innerHTML = `
                <div class="col-12">
                    <div class="empty-state shadow-sm">
                        <i class="bi bi-journal-x fs-1 d-block mb-2 text-muted default-icon"></i>
                        <img data-icon="icon-empty.png" class="empty-icon mb-2 dynamic-icon" alt="">
                        <p class="mb-0 text-muted">Không tìm thấy cuốn sách nào khớp với bộ lọc hiện tại.</p>
                    </div>
                </div>`;
            applyIconPack();
            if (allBooks.length > 0) {
                hienThiThongBaoNgoaiLeHTML(404, 'Không tìm thấy kết quả phù hợp (Mã 404)', 'Không có cuốn sách nào khớp với từ khóa hoặc tiêu chí lọc.');
            }
            return;
        }

        bookGrid.innerHTML = filtered.map(book => {
            const statusClass = book.status === 'Đã đọc'
                ? 'bg-success'
                : (book.status === 'Đang đọc' ? 'bg-warning text-dark' : 'bg-secondary');

            const priorityClass = book.priority === 'Cao'
                ? 'text-danger fw-bold'
                : (book.priority === 'Thấp' ? 'text-secondary' : 'text-primary');

            const fallbackImg = createFallbackCover(book.title, book.author);
            const coverSrc = (book.coverUrl && book.coverUrl.trim() !== '') ? book.coverUrl : fallbackImg;

            return `
            <div class="col-md-6 col-lg-4">
                <div class="card book-card h-100 shadow-sm border-0 rounded-4 overflow-hidden bg-body">
                    <div class="position-relative">
                        <img src="${coverSrc}" class="book-cover w-100" alt="${book.title}"
                             onerror="this.onerror=null; this.src='${fallbackImg}';">
                        <span class="badge ${statusClass} badge-status position-absolute top-0 end-0 m-3 shadow-sm">
                            ${book.status || 'Chưa đọc'}
                        </span>
                    </div>
                    <div class="card-body d-flex flex-column p-4">
                        <h5 class="card-title fw-bold mb-1 text-truncate" title="${book.title}">${book.title}</h5>
                        <p class="text-muted small mb-2"><i class="bi bi-person me-1"></i>${book.author || 'Chưa rõ tác giả'}</p>
                        <p class="card-text small text-secondary flex-grow-1 mb-3">${book.description || ''}</p>

                        <div class="d-flex justify-content-between align-items-center small text-muted border-top pt-2 mb-3">
                            <span><i class="bi bi-calendar-event me-1"></i>${book.deadline || 'Chưa đặt'}</span>
                            <span class="${priorityClass}"><i class="bi bi-flag me-1"></i>${book.priority || 'Trung bình'}</span>
                        </div>

                        <div class="d-grid gap-2">
                            <button type="button" class="btn btn-sm btn-ai-note w-100" onclick="handleSendAINote('${book.id}', this)">
                                <i class="bi bi-stars me-1"></i>Tạo ghi chú AI & Gửi mail
                            </button>
                            <div class="d-flex gap-2">
                                <button type="button" class="btn btn-sm btn-outline-success flex-grow-1" onclick="toggleBookStatus('${book.id}')" title="Đổi nhanh trạng thái đọc">
                                    <i class="bi bi-arrow-repeat me-1"></i>Đổi trạng thái
                                </button>
                                <button type="button" class="btn btn-sm btn-outline-primary" onclick="startEditBook('${book.id}')" title="Sửa thông tin">
                                    <i class="bi bi-pencil"></i>
                                </button>
                                <button type="button" class="btn btn-sm btn-outline-danger" onclick="deleteBook('${book.id}')" title="Xóa sách">
                                    <i class="bi bi-trash"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>`;
        }).join('');

        applyIconPack();
    }

    // Xử lý thêm mới hoặc lưu cập nhật sách
    if (bookForm) {
        bookForm.addEventListener('submit', (e) => {
            e.preventDefault();
            if (!validateBookForm()) return;

            const books = getBooks();
            const editingId = document.getElementById('bookId').value;

            const bookData = {
                id: editingId || String(Date.now()),
                title: document.getElementById('title').value.trim(),
                author: document.getElementById('author').value.trim(),
                deadline: document.getElementById('deadline').value,
                priority: document.getElementById('priority').value,
                status: document.getElementById('status').value,
                coverUrl: document.getElementById('coverUrl').value.trim(),
                description: document.getElementById('description').value.trim()
            };

            if (editingId) {
                const idx = books.findIndex(b => String(b.id) === String(editingId));
                if (idx !== -1) books[idx] = { ...books[idx], ...bookData };
                hienThiThongBaoNgoaiLeHTML(200, 'Cập nhật sách thành công (Mã 200)', `Đã lưu thay đổi cho cuốn "${bookData.title}".`);
            } else {
                books.unshift(bookData);
                hienThiThongBaoNgoaiLeHTML(200, 'Thêm sách mới thành công (Mã 200)', `Đã đưa cuốn "${bookData.title}" vào kệ sách.`);
            }

            saveBooks(books);
            resetBookForm();
            renderBooks();
        });
    }

    function resetBookForm() {
        bookForm.reset();
        document.getElementById('bookId').value = '';
        document.getElementById('formHeading').innerHTML = `<i class="bi bi-plus-circle text-primary me-2 default-icon"></i><img data-icon="icon-add.png" class="title-icon me-2 dynamic-icon" alt="">Thêm sách mới vào tủ`;
        document.getElementById('btnSubmitBook').innerHTML = `<i class="bi bi-save me-1"></i>Lưu vào tủ sách`;
        btnCancelEdit.classList.add('d-none');
        applyIconPack();
    }

    if (btnCancelEdit) {
        btnCancelEdit.addEventListener('click', resetBookForm);
    }

    // Đưa thông tin sách lên biểu mẫu để sửa
    window.startEditBook = function(id) {
        const book = getBooks().find(b => String(b.id) === String(id));
        if (!book) return;

        document.getElementById('bookId').value = book.id;
        document.getElementById('title').value = book.title || '';
        document.getElementById('author').value = book.author || '';
        document.getElementById('deadline').value = book.deadline || '';
        document.getElementById('priority').value = book.priority || 'Trung bình';
        document.getElementById('status').value = book.status || 'Chưa đọc';
        document.getElementById('coverUrl').value = book.coverUrl || '';
        document.getElementById('description').value = book.description || '';

        document.getElementById('formHeading').innerHTML = `<i class="bi bi-pencil-square text-warning me-2"></i>Cập nhật: ${book.title}`;
        document.getElementById('btnSubmitBook').innerHTML = `<i class="bi bi-check2-circle me-1"></i>Cập nhật ngay`;
        btnCancelEdit.classList.remove('d-none');
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    // Đổi nhanh trạng thái đọc (Chưa đọc -> Đang đọc -> Đã đọc)
    window.toggleBookStatus = function(id) {
        const books = getBooks();
        const idx = books.findIndex(b => String(b.id) === String(id));
        if (idx === -1) return;

        const order = ['Chưa đọc', 'Đang đọc', 'Đã đọc'];
        const currentIdx = order.indexOf(books[idx].status);
        books[idx].status = order[(currentIdx + 1) % order.length];

        saveBooks(books);
        renderBooks();
        hienThiThongBaoNgoaiLeHTML(200, 'Đã đổi trạng thái đọc (Mã 200)', `Cuốn "${books[idx].title}" đã chuyển sang "${books[idx].status}".`);
    };

    // Xóa sách khỏi tủ
    window.deleteBook = function(id) {
        const books = getBooks();
        const target = books.find(b => String(b.id) === String(id));
        if (!target) return;

        Swal.fire({
            title: `Xóa "${target.title}"?`,
            text: 'Cuốn sách này sẽ được gỡ khỏi kệ sách của bạn.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#dc3545',
            confirmButtonText: 'Xóa sách',
            cancelButtonText: 'Hủy'
        }).then((result) => {
            if (result.isConfirmed) {
                const updated = books.filter(b => String(b.id) !== String(id));
                saveBooks(updated);
                renderBooks();
                hienThiThongBaoNgoaiLeHTML(200, 'Đã xóa sách khỏi kệ (Mã 200)', `Đã gỡ cuốn "${target.title}".`);
            }
        });
    };

    // Gộp thêm danh sách sách từ tệp JSON bên ngoài
    if (btnBatchImport) {
        btnBatchImport.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;

            const reader = new FileReader();
            reader.onload = function(evt) {
                try {
                    const imported = JSON.parse(evt.target.result);
                    if (!Array.isArray(imported)) throw new Error('Tệp JSON phải là một mảng danh sách sách.');

                    const currentBooks = getBooks();
                    const normalized = imported.map((item, index) => ({
                        id: `${item.id || Date.now()}_${index}`,
                        title: item.title || 'Sách chưa đặt tên',
                        author: item.author || 'Chưa rõ tác giả',
                        deadline: item.deadline || '2026-12-31',
                        priority: item.priority || 'Trung bình',
                        status: item.status || 'Chưa đọc',
                        coverUrl: item.coverUrl || '',
                        description: item.description || 'Được gộp thêm từ tệp lưu trữ cá nhân.'
                    }));

                    const merged = [...normalized, ...currentBooks];
                    saveBooks(merged);
                    renderBooks();
                    hienThiThongBaoNgoaiLeHTML(200, `Đã gộp thêm ${normalized.length} cuốn sách (Mã 200)`, `Tổng số sách trong tủ hiện tại là ${merged.length} cuốn.`);
                    Swal.fire('Gộp thành công (Mã 200)', `Đã thêm ${normalized.length} cuốn sách vào kệ.`, 'success');
                } catch (err) {
                    hienThiThongBaoNgoaiLeHTML(400, 'Lỗi đọc tệp gộp thêm (Mã 400)', err.message);
                    Swal.fire('Tệp không hợp lệ (Mã 400)', err.message, 'error');
                }
                btnBatchImport.value = '';
            };
            reader.readAsText(file);
        });
    }

    // Lắng nghe sự kiện tìm kiếm, lọc và sắp xếp
    [searchInput, filterStatus, filterPriority, sortDeadline].forEach(el => {
        if (el) el.addEventListener('input', renderBooks);
    });

    renderBooks();
});