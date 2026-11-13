import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Khởi tạo 2 biểu đồ thống kê Chart.js
    const chartDataEl = document.getElementById('s22ChartData');
    if (chartDataEl && typeof window.Chart !== 'undefined') {
        const libLoans = parseInt(chartDataEl.getAttribute('data-lib-loans') || '0', 10);
        const p2pLoans = parseInt(chartDataEl.getAttribute('data-p2p-loans') || '0', 10);
        const availCopies = parseInt(chartDataEl.getAttribute('data-avail-copies') || '0', 10);
        const borrowedCopies = parseInt(chartDataEl.getAttribute('data-borrowed-copies') || '0', 10);
        const reservedCopies = parseInt(chartDataEl.getAttribute('data-reserved-copies') || '0', 10);
        const damagedCopies = parseInt(chartDataEl.getAttribute('data-damaged-copies') || '0', 10);

        const revMembership = parseFloat(chartDataEl.getAttribute('data-rev-membership') || '0');
        const revFine = parseFloat(chartDataEl.getAttribute('data-rev-fine') || '0');
        const revDeposit = parseFloat(chartDataEl.getAttribute('data-rev-deposit') || '0');

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
});
