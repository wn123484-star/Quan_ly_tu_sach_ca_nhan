import { apiFetch, loadScript, CDN, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Khởi tạo 2 biểu đồ thống kê Chart.js
    const chartDataEl = document.getElementById('s22ChartData');
    if (chartDataEl && typeof window.Chart !== 'undefined') {
        loadCharts();
    }

    async function loadCharts() {
        // Lấy số liệu thật từ API (chỉ Admin); nếu API lỗi thì dùng dữ liệu nhúng sẵn trong trang
        const res = await apiFetch('/S22AdminAnalytics/ChartData');
        const api = res.ok && res.data && typeof res.data === 'object' ? res.data : null;
        const attr = (name, parse) => parse(chartDataEl.getAttribute(name) || '0');

        const libLoans = api ? api.libraryLoans : attr('data-lib-loans', v => parseInt(v, 10));
        const p2pLoans = api ? api.p2pLoans : attr('data-p2p-loans', v => parseInt(v, 10));
        const availCopies = api ? api.availableCopies : attr('data-avail-copies', v => parseInt(v, 10));
        const borrowedCopies = api ? api.borrowedCopies : attr('data-borrowed-copies', v => parseInt(v, 10));
        const reservedCopies = api ? api.reservedCopies : attr('data-reserved-copies', v => parseInt(v, 10));
        const damagedCopies = api ? api.damagedCopies : attr('data-damaged-copies', v => parseInt(v, 10));

        const revMembership = api ? api.membershipRevenue : attr('data-rev-membership', parseFloat);
        const revFine = api ? api.fineRevenue : attr('data-rev-fine', parseFloat);
        const revDeposit = api ? api.simulatedDeposits : attr('data-rev-deposit', parseFloat);

        const ctxDist = document.getElementById('chartLoanAndCopyDistribution');
        if (ctxDist) {
            new window.Chart(ctxDist, {
                type: 'doughnut',
                data: {
                    labels: [
                        `Đơn Kho chung (${libLoans})`,
                        `Đơn P2P Cộng đồng (${p2pLoans})`,
                        `Bản sao Sẵn sàng (${availCopies})`,
                        `Đang mượn (${borrowedCopies})`,
                        `Giữ chỗ S11 (${reservedCopies})`,
                        `Hỏng/Mất (${damagedCopies})`
                    ],
                    datasets: [{
                        data: [libLoans, p2pLoans, availCopies, borrowedCopies, reservedCopies, damagedCopies],
                        backgroundColor: ['#2563eb', '#7c3aed', '#10b981', '#f59e0b', '#06b6d4', '#ef4444']
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'right' }
                    }
                }
            });
        }

        const ctxRev = document.getElementById('chartRevenueBreakdown');
        if (ctxRev) {
            new window.Chart(ctxRev, {
                type: 'bar',
                data: {
                    labels: ['Phí Gói Premium (S16)', 'Phí Phạt Trễ Hạn (S12)', 'Tiền Cọc Mô Phỏng (S17)'],
                    datasets: [{
                        label: 'Tổng giá trị giao dịch Sandbox (VNĐ)',
                        data: [revMembership, revFine, revDeposit],
                        backgroundColor: ['#2563eb', '#dc2626', '#0d9488'],
                        borderRadius: 8
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false
                }
            });
        }
    }

    // 2. Cấp quyền Role cho người dùng ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-btn-save-role').forEach(btn => {
        btn.addEventListener('click', async () => {
            const userId = btn.getAttribute('data-user-id') || '';
            const selectEl = document.querySelector(`.js-role-select[data-user-id="${userId}"]`);
            const newRole = selectEl?.value || 'User';

            const res = await apiFetch('/S22AdminAnalytics/ChangeUserRole', {
                method: 'POST',
                body: { userId, newRole }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    // 3. Khóa / Mở khóa tài khoản ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-btn-toggle-lock').forEach(btn => {
        btn.addEventListener('click', async () => {
            const userId = btn.getAttribute('data-user-id') || '';
            const lockAccount = btn.getAttribute('data-lock') === 'true';

            const res = await apiFetch('/S22AdminAnalytics/ToggleUserLock', {
                method: 'POST',
                body: { userId, lockAccount }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    // 4. Kích hoạt chạy Background Jobs hằng ngày ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-trigger-job').forEach(btn => {
        btn.addEventListener('click', async () => {
            const jobKey = btn.getAttribute('data-job') || 'AllDailyJobs';
            btn.disabled = true;
            try {
                const res = await apiFetch('/S22AdminAnalytics/TriggerDailyJob', {
                    method: 'POST',
                    body: { jobKey }
                });
                if (res.ok) {
                    setTimeout(() => window.location.reload(), 800);
                }
            } finally {
                btn.disabled = false;
            }
        });
    });

    // 5. Xuất Báo Cáo PDF (jspdf + html2canvas): chụp tiêu đề, KPI, 2 biểu đồ Chart.js và bảng tài khoản
    document.getElementById('btnExportS22Pdf')?.addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const original = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Đang tạo PDF...';
        try {
            await Promise.all([loadScript(CDN.jspdf), loadScript(CDN.html2canvas)]);
            const { jsPDF } = window.jspdf;
            const sections = [...document.querySelectorAll('[data-report-section]')];
            const userTableCard = document.querySelector('table.table-mobile-cards')?.closest('.card');
            if (userTableCard) sections.push(userTableCard);

            const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
            const pageW = pdf.internal.pageSize.getWidth();
            const pageH = pdf.internal.pageSize.getHeight();
            const margin = 10;
            const usableW = pageW - margin * 2;
            const bg = getComputedStyle(document.body).backgroundColor || '#ffffff';
            let y = margin;

            for (const section of sections) {
                const canvas = await window.html2canvas(section, {
                    scale: 2,
                    backgroundColor: bg,
                    useCORS: true,
                    ignoreElements: el => el.hasAttribute?.('data-report-ignore')
                });
                const imgH = canvas.height * usableW / canvas.width;
                if (y + imgH > pageH - margin && y > margin) {
                    pdf.addPage();
                    y = margin;
                }
                // JPEG 0.85 giữ chữ nét mà file nhẹ hơn PNG hàng chục lần
                pdf.addImage(canvas.toDataURL('image/jpeg', 0.85), 'JPEG', margin, y, usableW, Math.min(imgH, pageH - margin * 2), undefined, 'FAST');
                y += imgH + 5;
            }

            const stamp = new Date();
            const pages = pdf.getNumberOfPages();
            for (let i = 1; i <= pages; i++) {
                pdf.setPage(i);
                pdf.setFontSize(8);
                pdf.setTextColor(120);
                pdf.text(`BookShare Admin Report - ${stamp.toLocaleString('en-GB')} - Page ${i}/${pages}`, margin, pageH - 4);
            }

            const fileName = `BookShare_BaoCao_S22_${stamp.toISOString().slice(0, 10)}.pdf`;
            pdf.save(fileName);
            showSystemStatus(200, 'Đã xuất báo cáo PDF (Mã 200)', `Tệp ${fileName} đã được tải xuống.`);
        } catch (err) {
            showSystemStatus(500, 'Không xuất được báo cáo PDF (Mã 500)', err.message);
        } finally {
            btn.disabled = false;
            btn.innerHTML = original;
        }
    });
});
