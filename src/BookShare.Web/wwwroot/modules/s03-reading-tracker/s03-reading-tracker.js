/**
 * MODULE S03: READING TRACKER - KẾ THỪA VER07 (s03-reading-tracker.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S03
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Vẽ Biểu đồ Doughnut (Kế thừa Ver07) & Biểu đồ Cột Thống kê Trang đọc
    const chartDataEl = document.getElementById('s03ChartPayload');
    if (chartDataEl && typeof Chart !== 'undefined') {
        try {
            const payload = JSON.parse(chartDataEl.textContent || '{}');
            const doughnutCtx = document.getElementById('s03DoughnutChart');
            if (doughnutCtx) {
                new Chart(doughnutCtx, {
                    type: 'doughnut',
                    data: {
                        labels: ['Đã đọc xong', 'Đang đọc', 'Muốn đọc'],
                        datasets: [{
                            data: [
                                payload.completedCount || 0,
                                payload.readingCount || 0,
                                payload.wantToReadCount || 0
                            ],
                            backgroundColor: ['#198754', '#ffc107', '#0d6efd'],
                            borderWidth: 0
                        }]
                    },
                    options: {
                        cutout: '68%',
                        plugins: { legend: { position: 'bottom' } }
                    }
                });
            }

            const barCtx = document.getElementById('s03PagesBarChart');
            if (barCtx && Array.isArray(payload.bookProgressLabels)) {
                new Chart(barCtx, {
                    type: 'bar',
                    data: {
                        labels: payload.bookProgressLabels,
                        datasets: [{
                            label: '% Hoàn thành đọc sách',
                            data: payload.bookProgressPercents,
                            backgroundColor: '#2563eb',
                            borderRadius: 8
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {
                            y: { beginAtZero: true, max: 100 }
                        },
                        plugins: { legend: { display: false } }
                    }
                });
            }
        } catch (err) {
            showSystemStatus(500, 'Lỗi vẽ biểu đồ thống kê đọc sách (Mã 500)', err.message);
        }
    }

    // 2. Kéo thanh trượt cập nhật nhanh số trang đang đọc
    document.querySelectorAll('.js-page-slider').forEach(slider => {
        slider.addEventListener('input', (e) => {
            const progId = e.target.getAttribute('data-progress-id');
            const total = parseInt(e.target.getAttribute('max') || '1', 10);
            const cur = parseInt(e.target.value || '0', 10);
            const pct = Math.min(100, Math.round((cur / total) * 1000) / 10);

            const curLabel = document.getElementById(`curPageLabel-${progId}`);
            const pctBadge = document.getElementById(`pctBadge-${progId}`);
            const numInput = document.getElementById(`numPageInput-${progId}`);

            if (curLabel) curLabel.innerText = cur;
            if (pctBadge) pctBadge.innerText = `${pct}%`;
            if (numInput) numInput.value = cur;
        });
    });

    // 3. Nút Lưu cập nhật số trang đọc xuống SQL Server
    document.querySelectorAll('.js-save-progress-btn').forEach(btn => {
        btn.addEventListener('click', async () => {
            const progId = parseInt(btn.getAttribute('data-progress-id') || '0', 10);
            const numInput = document.getElementById(`numPageInput-${progId}`);
            const prioritySel = document.getElementById(`prioritySelect-${progId}`);
            const ratingSel = document.getElementById(`ratingSelect-${progId}`);

            const currentPage = parseInt(numInput?.value || '0', 10);
            const priority = prioritySel?.value || 'Medium';
            const personalRating = ratingSel?.value ? parseInt(ratingSel.value, 10) : null;

            btn.disabled = true;
            try {
                const res = await apiFetch('/S03ReadingTracker/UpdateProgress', {
                    method: 'POST',
                    body: { progressId: progId, currentPage, priority, personalRating }
                });

                if (res.ok && res.data) {
                    const statusBadge = document.getElementById(`statusBadge-${progId}`);
                    if (statusBadge && res.data.statusLabel) {
                        statusBadge.innerText = res.data.statusLabel;
                    }
                    showSystemStatus(200, 'Cập nhật tiến độ đọc thành công (Mã 200)', res.data.message);
                }
            } finally {
                btn.disabled = false;
            }
        });
    });

    // 4. Nút Xuất tệp sao lưu JSON (Kế thừa book-tracker-ver07)
    const btnExportJson = document.getElementById('btnExportTrackerJson');
    if (btnExportJson && chartDataEl) {
        btnExportJson.addEventListener('click', () => {
            const blob = new Blob([chartDataEl.textContent || '{}'], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `tu-sach-ca-nhan-bookshare-${new Date().toISOString().slice(0, 10)}.json`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            showSystemStatus(200, 'Xuất tệp sao lưu JSON thành công (Mã 200)', 'Đã tải tệp sao lưu tiến độ đọc sách về máy tính.');
        });
    }
});
