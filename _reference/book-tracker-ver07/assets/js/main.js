// =========================================================================
// TỆP: assets/js/main.js
// Mục đích: Chứa các cấu hình lõi, tiện ích chung và quản lý dữ liệu LocalStorage
// =========================================================================

/* [MÃ: DL-01] Khai báo khóa lưu trữ LocalStorage, danh sách file CSS giao diện và bộ dữ liệu sách mẫu đa dạng */
const STORAGE_KEY = 'books';

const THEME_URLS = {
    'pure': '',
    'default': 'vendor/bootstrap/bootstrap.min.css',
    'flatly': 'vendor/bootswatch/flatly.min.css',
    'cyborg': 'vendor/bootswatch/cyborg.min.css',
    'sketchy': 'vendor/bootswatch/sketchy.min.css',
    'morph': 'vendor/bootswatch/morph.min.css'
};

// Hàm in nhật ký ra cửa sổ F12 (Console) có màu sắc rõ ràng để Tester kiểm tra và demo cho thầy xem luồng chạy
function ghiLogF12(nhomChucNang, hanhDong, duLieuChiTiet = '') {
    const thoiGian = new Date().toLocaleTimeString('vi-VN');
    console.log(
        `%c[TỦ SÁCH - ${thoiGian}] %c[${nhomChucNang}] %c${hanhDong}`,
        'color: #0ea5e9; font-weight: bold;',
        'color: #16a34a; font-weight: bold;',
        'color: #eab308; font-weight: 600;',
        duLieuChiTiet
    );
}

/* [MÃ: DL-08] Hàm hienThiThongBaoNgoaiLeHTML: Hiển thị mã trạng thái (200, 300, 400, 404, 500) trực tiếp lên khung HTML trên màn hình */
function hienThiThongBaoNgoaiLeHTML(maCode, tieuDe, chiTiet) {
    const statusBox = document.getElementById('systemStatusBox');
    const codeBadge = document.getElementById('statusCodeBadge');
    const titleText = document.getElementById('statusTitleText');
    const detailText = document.getElementById('statusDetailText');

    // Ghi log đồng thời vào F12 để vừa thấy trên màn hình vừa thấy trong Console
    ghiLogF12(`TRẠNG THÁI MÃ ${maCode}`, tieuDe, chiTiet);

    if (!statusBox || !codeBadge || !titleText || !detailText) return;

    // Xóa các class màu cũ rồi gắn class màu tương ứng với mã trạng thái mới
    statusBox.classList.remove('hidden-banner', 'status-200', 'status-300', 'status-400', 'status-404', 'status-500');

    let nhomClass = 'status-200';
    if (maCode >= 300 && maCode < 400) nhomClass = 'status-300';
    else if (maCode === 400) nhomClass = 'status-400';
    else if (maCode === 404) nhomClass = 'status-404';
    else if (maCode >= 500) nhomClass = 'status-500';

    statusBox.classList.add(nhomClass);
    codeBadge.innerText = `MÃ ${maCode}`;
    titleText.innerText = tieuDe;
    detailText.innerText = chiTiet;
}

// Tự động bắt mọi lỗi cú pháp hoặc ngoại lệ bất ngờ trong trang và hiện lên khung HTML trên màn hình
window.addEventListener('error', (event) => {
    if (event && event.message) {
        hienThiThongBaoNgoaiLeHTML(500, 'Phát hiện ngoại lệ hệ thống (Mã 500)', event.message);
    }
});

// Tự động phát hiện khi máy tính mất mạng hoặc có mạng trở lại để báo ngay trên giao diện HTML
window.addEventListener('offline', () => {
    hienThiThongBaoNgoaiLeHTML(500, 'Mất kết nối mạng Internet (Mã 500 - Offline)', 'Đã chuyển sang chế độ локаl (Offline). Bạn vẫn thêm, sửa, xóa sách và dùng bìa tự vẽ bình thường.');
});

window.addEventListener('online', () => {
    hienThiThongBaoNgoaiLeHTML(200, 'Đã kết nối mạng trở lại (Mã 200 - Online)', 'Mọi tính năng trực tuyến như tải ảnh bìa ngoài và gửi báo cáo qua thư đã sẵn sàng.');
});

// =========================================================================
// [BẢN CẬP NHẬT] KHAI BÁO CÁC HÀM XỬ LÝ GIAO DIỆN TOÀN CỤC (GLOBAL SCOPE)
// Định nghĩa trên đối tượng window để các tệp settings.js, gemini-service.js đều có thể gọi tới
// =========================================================================

/* [MÃ: DL-02] Hàm applyTheme: Đổi giao diện và ghi đè lên stylesheet */
window.applyTheme = function(themeName) {
    localStorage.setItem('app_theme', themeName);
    const linkElement = document.getElementById('themeStylesheet');
    if (!linkElement) return;

    if (themeName === 'pure') {
        linkElement.removeAttribute('href');
        document.documentElement.classList.add('pure-mode');
    } else {
        linkElement.href = THEME_URLS[themeName] || THEME_URLS['default'];
        document.documentElement.classList.remove('pure-mode');
    }
    ghiLogF12('GIAO DIỆN', 'Đã áp dụng bộ giao diện:', { maGiaoDien: themeName, cheDoCodeThuan: themeName === 'pure' });
};

// Hàm applySavedTheme gọi lại applyTheme để tự khởi chạy lúc mở trang
window.applySavedTheme = function() {
    const savedTheme = localStorage.getItem('app_theme') || 'default';
    window.applyTheme(savedTheme);
};

/* [MÃ: DL-03] Hàm applyFont: Đổi font chữ hiển thị */
window.applyFont = function(fontName) {
    localStorage.setItem('app_font', fontName);
    document.documentElement.setAttribute('data-font', fontName);
    ghiLogF12('FONT CHỮ', 'Đã nạp kiểu chữ:', { maFont: fontName });
};

// Hàm applySavedFont gọi lại applyFont để tự khởi chạy lúc mở trang
window.applySavedFont = function() {
    const savedFont = localStorage.getItem('app_font') || 'priority-regular';
    window.applyFont(savedFont);
};

/* [MÃ: DL-04] Hàm applyIconPack: Đồng bộ bộ biểu tượng trên trang */
window.applyIconPack = function(packName) {
    if (packName) {
        localStorage.setItem('app_icon_pack', packName);
    }
    const currentPack = localStorage.getItem('app_icon_pack') || 'bo1';

    if (currentPack === 'bo4') {
        document.documentElement.classList.add('use-default-icons');
    } else {
        document.documentElement.classList.remove('use-default-icons');
        document.querySelectorAll('.dynamic-icon').forEach(img => {
            const iconName = img.getAttribute('data-icon');
            if (iconName) {
                img.src = `assets/img/${currentPack}/${iconName}`;
                img.onerror = function() {
                    this.style.display = 'none';
                    const fallbackIcon = this.previousElementSibling;
                    if (fallbackIcon && fallbackIcon.classList.contains('default-icon')) {
                        fallbackIcon.style.display = 'inline-block';
                    }
                    hienThiThongBaoNgoaiLeHTML(300, `Chuyển hướng biểu tượng dự phòng (Mã 300)`, `Không tìm thấy ảnh ${iconName}, đã tự động bật biểu tượng mặc định của web.`);
                };
            }
        });
    }
    ghiLogF12('BIỂU TƯỢNG', 'Đã đồng bộ gói biểu tượng:', { boIconDangDung: currentPack });
};

/* [MÃ: DL-05] Hàm applyCustomVisuals & applySavedBackground: Đọc ảnh từ localStorage làm nền */
window.applySavedBackground = function() {
    const bgData = localStorage.getItem('app_bg');
    if (bgData) {
        document.body.style.backgroundImage = `url(${bgData})`;
        document.body.style.backgroundSize = 'cover';
        document.body.style.backgroundPosition = 'center';
        document.body.style.backgroundAttachment = 'fixed';
        document.body.classList.add('has-bg');
        ghiLogF12('HÌNH NỀN', 'Đang sử dụng hình nền cá nhân từ LocalStorage');
    } else {
        document.body.style.backgroundImage = 'none';
        document.body.classList.remove('has-bg');
        ghiLogF12('HÌNH NỀN', 'Đang sử dụng nền mặc định của giao diện');
    }
};

window.applyCustomVisuals = function() {
    window.applySavedBackground();
};

window.compressImage = function(file, maxWidth, maxHeight, quality, callback) {
    ghiLogF12('NÉN ẢNH', 'Bắt đầu nén tệp ảnh tải lên:', { tenTep: file.name, dungLuongGocKB: Math.round(file.size / 1024) });
    const reader = new FileReader();
    reader.onerror = function() {
        hienThiThongBaoNgoaiLeHTML(400, 'Lỗi đọc tệp hình ảnh (Mã 400)', 'Không thể đọc tệp ảnh bạn vừa chọn.');
    };
    reader.onload = function(e) {
        const img = new Image();
        img.onerror = function() {
            hienThiThongBaoNgoaiLeHTML(400, 'Tệp không phải hình ảnh hợp lệ (Mã 400)', 'Vui lòng chọn đúng tệp ảnh đuôi .jpg, .png hoặc .webp.');
        };
        img.onload = function() {
            const canvas = document.createElement('canvas');
            let width = img.width;
            let height = img.height;
            if (width > height) {
                if (width > maxWidth) {
                    height = Math.round(height *= maxWidth / width);
                    width = maxWidth;
                }
            } else {
                if (height > maxHeight) {
                    width = Math.round(width *= maxHeight / height);
                    height = maxHeight;
                }
            }
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(img, 0, 0, width, height);
            const base64Data = canvas.toDataURL('image/jpeg', quality);
            ghiLogF12('NÉN ẢNH', 'Nén ảnh hoàn tất:', { chieuRongMoi: width, chieuCaoMoi: height });
            callback(base64Data);
        };
        img.src = e.target.result;
    };
    reader.readAsDataURL(file);
};

/* [MÃ: DL-06] Hàm getBooks & saveBooks: Đọc và ghi danh sách sách vào LocalStorage có bắt ngoại lệ tràn bộ nhớ */
function getBooks() {
    try {
        const rawData = localStorage.getItem(STORAGE_KEY);
        if (!rawData) {
            // Không nạp dữ liệu mẫu nữa, khởi tạo mảng rỗng
            localStorage.setItem(STORAGE_KEY, JSON.stringify([]));
            return []; // Trả về mảng rỗng
        }
        return JSON.parse(rawData);
    } catch (e) {
        hienThiThongBaoNgoaiLeHTML(500, 'Lỗi đọc bộ nhớ (Mã 500)', e.message);
        // Nếu lỗi, để web không sập, trả về mảng rỗng
        return [];
    }
}

function saveBooks(booksArray) {
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(booksArray));
        ghiLogF12('DỮ LIỆU', 'Đã lưu danh sách sách mới vào LocalStorage:', { tongSoSachHienTai: booksArray.length });
        return true;
    } catch (e) {
        hienThiThongBaoNgoaiLeHTML(500, 'Bộ nhớ trình duyệt đã đầy (Mã 500 - QuotaExceeded)', 'Dung lượng LocalStorage vượt quá giới hạn 5MB. Hãy xóa bớt hình nền hoặc tải tệp sao lưu.');
        if(typeof Swal !== 'undefined') {
            Swal.fire('Tràn bộ nhớ (Mã 500)', 'Bộ nhớ trình duyệt đã đầy, vui lòng gỡ hình nền cá nhân hoặc xóa bớt dữ liệu cũ.', 'error');
        }
        return false;
    }
}

/* [MÃ: DL-07] Gắn sự kiện cho các nút trên thanh điều hướng và nút đóng khung thông báo trạng thái */
document.addEventListener('DOMContentLoaded', () => {
    ghiLogF12('KHỞI ĐỘNG TRANG', `Đang mở trang: ${document.title}`, { duongDan: window.location.pathname });

    window.applySavedTheme();
    window.applySavedFont();
    window.applyIconPack();
    window.applyCustomVisuals();

    // Nút đóng thu gọn khung thông báo trạng thái trên màn hình
    const btnHideStatusBox = document.getElementById('btnHideStatusBox');
    if (btnHideStatusBox) {
        btnHideStatusBox.addEventListener('click', () => {
            document.getElementById('systemStatusBox').classList.add('hidden-banner');
            ghiLogF12('KHUNG TRẠNG THÁI', 'Người dùng bấm ẩn thanh thông báo trạng thái trên màn hình');
        });
    }

    // Nếu mở trang lúc đang mất mạng thì báo ngay trên thanh trạng thái
    if (!navigator.onLine) {
        hienThiThongBaoNgoaiLeHTML(300, 'Đang chạy ở chế độ Ngoại tuyến (Mã 300 - Offline Mode)', 'Không có Internet nhưng toàn bộ tính năng quản lý sách, font chữ và bìa SVG vẫn hoạt động 100%.');
    }

    document.querySelectorAll('.nav-link, .navbar-brand').forEach(link => {
        link.addEventListener('click', () => {
            ghiLogF12('ĐIỀU HƯỚNG', `Người dùng bấm chuyển sang trang: ${link.innerText.trim()}`, { href: link.getAttribute('href') });
        });
    });

    const mobileMenuBtn = document.getElementById('mobileMenuBtn');
    const navbarNav = document.getElementById('navbarNav');
    if (mobileMenuBtn && navbarNav) {
        mobileMenuBtn.addEventListener('click', () => {
            if (document.documentElement.classList.contains('pure-mode')) {
                navbarNav.classList.toggle('show');
            }
            ghiLogF12('MENU ĐIỆN THOẠI', 'Bấm nút đóng/mở menu trên màn hình nhỏ');
        });
    }

    // Nút Tải tệp sao lưu (.json)
    const btnExport = document.getElementById('btnExportJSON');
    if (btnExport) {
        btnExport.addEventListener('click', () => {
            const books = getBooks();
            ghiLogF12('SAO LƯU DỮ LIỆU', 'Bấm nút Tải tệp sao lưu JSON', { soLuongSachSaoLuu: books.length });

            if (books.length === 0) {
                hienThiThongBaoNgoaiLeHTML(400, 'Không thể sao lưu tủ sách trống (Mã 400)', 'Hiện chưa có cuốn sách nào trong tủ để xuất ra tệp JSON.');
                if(typeof Swal !== 'undefined') Swal.fire('Thông báo (Mã 400)', 'Tủ sách đang trống, chưa có dữ liệu để sao lưu.', 'info');
                return;
            }
            const dataStr = JSON.stringify(books, null, 2);
            const blob = new Blob([dataStr], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const fileName = `tu-sach-ca-nhan-${new Date().toISOString().slice(0, 10)}.json`;

            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);

            hienThiThongBaoNgoaiLeHTML(200, 'Xuất tệp sao lưu thành công (Mã 200)', `Đã tải tệp ${fileName} gồm ${books.length} cuốn sách về máy tính.`);
            if(typeof Swal !== 'undefined') Swal.fire('Thành công (Mã 200)', 'Đã tải xuống tệp sao lưu tủ sách.', 'success');
        });
    }

    // Nút Phục hồi dữ liệu từ tệp (.json)
    const btnImport = document.getElementById('btnImportJSON');
    if (btnImport) {
        btnImport.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;
            ghiLogF12('PHỤC HỒI DỮ LIỆU', 'Đã chọn tệp JSON để khôi phục:', { tenTep: file.name, kichThuocBytes: file.size });

            const reader = new FileReader();
            reader.onload = (event) => {
                try {
                    const jsonData = JSON.parse(event.target.result);
                    if (Array.isArray(jsonData)) {
                        saveBooks(jsonData);
                        hienThiThongBaoNgoaiLeHTML(200, 'Phục hồi dữ liệu thành công (Mã 200)', `Đã nạp lại ${jsonData.length} cuốn sách từ tệp ${file.name}.`);
                        if(typeof Swal !== 'undefined') Swal.fire('Thành công (Mã 200)', `Đã khôi phục ${jsonData.length} cuốn sách!`, 'success').then(() => location.reload());
                    } else {
                        throw new Error('Dữ liệu trong tệp không phải là danh sách mảng');
                    }
                } catch (err) {
                    hienThiThongBaoNgoaiLeHTML(400, 'Tệp sao lưu bị lỗi cấu trúc (Mã 400)', `Tệp ${file.name} không đúng chuẩn JSON của Tủ Sách Cá Nhân.`);
                    if(typeof Swal !== 'undefined') Swal.fire('Lỗi định dạng (Mã 400)', 'Tệp sao lưu không đúng định dạng.', 'error');
                }
            };
            reader.readAsText(file);
            e.target.value = '';
        });
    }
});