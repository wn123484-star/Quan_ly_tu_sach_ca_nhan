import { apiFetch, loadScript, CDN, showSystemStatus, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Khởi tạo 2 biểu đồ thống kê Chart.js (hỗ trợ lọc động Thời gian / Kênh / Thể loại)
    const chartDataEl = document.getElementById('s22ChartData');
    let distChart = null;
    let revChart = null;

    if (chartDataEl && typeof window.Chart !== 'undefined') {
        loadCharts();
    }

    document.getElementById('btnRefreshChartsAjax')?.addEventListener('click', () => {
        loadCharts();
    });

    async function loadCharts() {
        const range = document.getElementById('s22RangeFilter')?.value || 'all';
        const channel = document.getElementById('s22ChannelFilter')?.value || 'all';
        const categoryId = document.getElementById('s22CategoryFilter')?.value || '0';
        const qs = `?range=${encodeURIComponent(range)}&channel=${encodeURIComponent(channel)}&categoryId=${encodeURIComponent(categoryId)}`;

        const res = await apiFetch(`/S22AdminAnalytics/ChartData${qs}`, { button: null });
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

        if (api) {
            const setTxt = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
            setTxt('kpiMembershipRev', `${Math.round(revMembership).toLocaleString('vi-VN')} đ`);
            setTxt('kpiDepositRev', Math.round(revDeposit).toLocaleString('vi-VN'));
            setTxt('kpiFineRev', `${Math.round(revFine).toLocaleString('vi-VN')} đ`);
            setTxt('kpiOpenDisputes', String(api.openDisputes ?? 0));
            setTxt('kpiLoanSplit', `${libLoans} Kho / ${p2pLoans} Cộng đồng`);
            setTxt('kpiTotalLoans', String(api.totalLoans ?? 0));
            setTxt('kpiAiMessages', String(api.aiMessages ?? 0));
        }

        const ctxDist = document.getElementById('chartLoanAndCopyDistribution');
        if (ctxDist) {
            if (distChart) { distChart.destroy(); distChart = null; }
            distChart = new window.Chart(ctxDist, {
                type: 'doughnut',
                data: {
                    labels: [
                        `Đơn Kho chung (${libLoans})`,
                        `Đơn P2P Cộng đồng (${p2pLoans})`,
                        `Bản sao Sẵn sàng (${availCopies})`,
                        `Đang mượn (${borrowedCopies})`,
                        `Đang giữ chỗ (${reservedCopies})`,
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
            if (revChart) { revChart.destroy(); revChart = null; }
            revChart = new window.Chart(ctxRev, {
                type: 'bar',
                data: {
                    labels: ['Phí Gói Hội Viên', 'Phí Phạt Trễ Hạn', 'Tiền Cọc Bảo Lãnh'],
                    datasets: [{
                        label: 'Tổng giá trị giao dịch Tiền ảo (VNĐ)',
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

    // 3b. Xóa vĩnh viễn tài khoản thành viên ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-btn-delete-user').forEach(btn => {
        btn.addEventListener('click', async () => {
            const userId = btn.getAttribute('data-user-id') || '';
            const fullName = btn.getAttribute('data-user-name') || userId;
            const email = btn.getAttribute('data-user-email') || '';

            const ok = await confirmDialog({
                title: `Xóa vĩnh viễn tài khoản "${fullName}"?`,
                text: `Tài khoản ${email} và toàn bộ dữ liệu cá nhân (kệ sách, ghi chú, ví tiền ảo, thông báo) sẽ bị xóa không thể hoàn tác. Tài khoản đang mượn sách hoặc còn tranh chấp mở sẽ tự động được bảo vệ chặn xóa.`,
                confirmText: 'Xác nhận xóa vĩnh viễn',
                danger: true
            });
            if (!ok) return;

            const res = await apiFetch('/S22AdminAnalytics/DeleteUser', {
                method: 'POST',
                body: { userId },
                button: btn
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 700);
            }
        });
    });

    document.querySelectorAll('.js-btn-review-upgrade').forEach(btn => {
        btn.addEventListener('click', async () => {
            const requestId = parseInt(btn.getAttribute('data-id') || '0', 10);
            const approve = btn.getAttribute('data-approve') === 'true';
            const res = await apiFetch('/S22AdminAnalytics/ReviewRoleUpgrade', {
                method: 'POST',
                body: { requestId, approve },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    // 4. Kích hoạt chạy Background Jobs hằng ngày ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-manage-sample-data').forEach(btn => {
        btn.addEventListener('click', async () => {
            const actionMode = btn.getAttribute('data-mode') || 'SeedSampleData';
            const title = btn.getAttribute('data-confirm-title') || 'Xác nhận thao tác CSDL?';
            const text = btn.getAttribute('data-confirm-text') || '';

            const ok = await confirmDialog({
                title,
                text,
                confirmText: 'Thực thi ngay',
                danger: actionMode === 'FactoryClean'
            });
            if (!ok) return;

            const res = await apiFetch('/S22AdminAnalytics/ManageSampleData', {
                method: 'POST',
                body: { actionMode },
                button: btn
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 800);
            }
        });
    });

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

            const fileName = `BookShare_BaoCao_QuanTri_${stamp.toISOString().slice(0, 10)}.pdf`;
            pdf.save(fileName);
            showSystemStatus(200, 'Đã xuất báo cáo PDF (Mã 200)', `Tệp ${fileName} đã được tải xuống.`);
        } catch (err) {
            showSystemStatus(500, 'Không xuất được báo cáo PDF (Mã 500)', err.message);
        } finally {
            btn.disabled = false;
            btn.innerHTML = original;
        }
    });

    // 6. Tab Quản Lý & Tùy Chỉnh Biểu Tượng Toàn Trang (Sprint 3.5)
    const iconGrid = document.getElementById('s22IconGrid');
    if (iconGrid) {
        const escapeAttr = (v) => String(v ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

        const updateIconCardUi = (card, item, localPreviewUrl = null) => {
            if (!card || !item) return;
            const stage = card.querySelector('.js-icon-preview-stage');
            const toggle = card.querySelector('.js-icon-toggle');
            const badge = card.querySelector('.js-icon-mode-badge');
            const fileHint = card.querySelector('.js-icon-file-hint');
            const useCustom = Boolean(item.useCustomImage && (item.customImageUrl || localPreviewUrl));

            if (stage) {
                if (useCustom) {
                    const cacheBust = `t=${Date.now()}`;
                    const rawPrimary = item.customImageUrl
                        ? `${item.customImageUrl}${item.customImageUrl.includes('?') ? '&' : '?'}${cacheBust}`
                        : localPreviewUrl;
                    const dbFallback = `/assets/system-icons/${encodeURIComponent(item.iconKey)}?${cacheBust}`;
                    const safeUrl = escapeAttr(rawPrimary);
                    const safeDbFallback = escapeAttr(dbFallback);
                    const safeLocalFallback = escapeAttr(localPreviewUrl || '');
                    const safeAlt = escapeAttr(item.displayName || item.iconKey);
                    const safeCls = escapeAttr(item.defaultIconClass || 'bi-app');
                    const onErrorCode = safeLocalFallback
                        ? `if(!this.dataset.dbTried){this.dataset.dbTried='1';this.src='${safeDbFallback}';return;}if(!this.dataset.localTried){this.dataset.localTried='1';this.src='${safeLocalFallback}';return;}this.style.display='none';if(this.nextElementSibling)this.nextElementSibling.classList.remove('d-none');`
                        : `if(!this.dataset.dbTried){this.dataset.dbTried='1';this.src='${safeDbFallback}';return;}this.style.display='none';if(this.nextElementSibling)this.nextElementSibling.classList.remove('d-none');`;
                    stage.innerHTML = `<img src="${safeUrl}" alt="${safeAlt}" class="s22-icon-preview-img" onerror="${onErrorCode}" /><i class="bi ${safeCls} fs-3 text-primary d-none" aria-hidden="true"></i>`;
                } else {
                    const safeCls = escapeAttr(item.defaultIconClass || 'bi-app');
                    stage.innerHTML = `<i class="bi ${safeCls} fs-3 text-primary" aria-hidden="true"></i>`;
                }
            }

            if (toggle) {
                toggle.checked = useCustom;
            }

            if (badge) {
                badge.className = `badge js-icon-mode-badge ${useCustom ? 'bg-success' : 'bg-secondary'}`;
                badge.textContent = useCustom ? 'Ảnh tùy chỉnh' : 'Mặc định';
            }

            if (fileHint) {
                fileHint.textContent = item.customImageUrl
                    ? 'Đã lưu & hiển thị xem trước ảnh tùy chỉnh'
                    : 'Chưa có ảnh tùy chỉnh';
            }
        };

        document.querySelectorAll('.js-icon-cat-filter').forEach(btn => {
            btn.addEventListener('click', () => {
                const selectedCat = btn.getAttribute('data-cat') || 'ALL';
                document.querySelectorAll('.js-icon-cat-filter').forEach(b => {
                    b.classList.toggle('btn-primary', b === btn);
                    b.classList.toggle('btn-outline-secondary', b !== btn);
                });
                iconGrid.querySelectorAll('.s22-icon-col').forEach(col => {
                    const colCat = col.getAttribute('data-category') || '';
                    col.classList.toggle('d-none', selectedCat !== 'ALL' && colCat !== selectedCat);
                });
            });
        });

        iconGrid.querySelectorAll('.js-icon-upload-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const key = btn.getAttribute('data-icon-key');
                const input = iconGrid.querySelector(`.js-icon-file-input[data-icon-key="${key}"]`);
                input?.click();
            });
        });

        iconGrid.querySelectorAll('.js-icon-file-input').forEach(input => {
            input.addEventListener('change', async () => {
                const file = input.files?.[0];
                if (!file) return;
                const key = input.getAttribute('data-icon-key') || '';
                const card = input.closest('.s22-icon-card');
                const uploadBtn = card?.querySelector('.js-icon-upload-btn');
                const stage = card?.querySelector('.js-icon-preview-stage');
                const fileHint = card?.querySelector('.js-icon-file-hint');

                if (file.size > 2 * 1024 * 1024) {
                    showSystemStatus(400, 'Tệp quá dung lượng cho phép', 'Ảnh biểu tượng không được vượt quá 2MB.');
                    input.value = '';
                    return;
                }

                // Hiển thị ngay hình ảnh xem trước cục bộ ngay khi Admin vừa chọn ảnh
                const previousStageHtml = stage ? stage.innerHTML : '';
                const localPreviewUrl = URL.createObjectURL(file);
                if (stage) {
                    stage.innerHTML = `<img src="${escapeAttr(localPreviewUrl)}" alt="Xem trước biểu tượng" class="s22-icon-preview-img" />`;
                }
                if (fileHint) {
                    fileHint.textContent = `Đang tải lên: ${file.name}...`;
                }

                const formData = new FormData();
                formData.append('iconKey', key);
                formData.append('file', file);

                const res = await apiFetch('/S22AdminAnalytics/UploadCustomIcon', {
                    method: 'POST',
                    body: formData,
                    button: uploadBtn
                });
                input.value = '';

                if (res.ok && res.data?.item) {
                    updateIconCardUi(card, res.data.item, localPreviewUrl);
                } else {
                    if (stage) stage.innerHTML = previousStageHtml;
                    if (fileHint) fileHint.textContent = 'Tải ảnh thất bại, giữ biểu tượng cũ';
                    URL.revokeObjectURL(localPreviewUrl);
                }
            });
        });

        iconGrid.querySelectorAll('.js-icon-toggle').forEach(toggle => {
            toggle.addEventListener('change', async () => {
                const key = toggle.getAttribute('data-icon-key') || '';
                const card = toggle.closest('.s22-icon-card');
                const desired = toggle.checked;
                toggle.disabled = true;

                try {
                    const res = await apiFetch('/S22AdminAnalytics/ToggleIconMode', {
                        method: 'POST',
                        body: { iconKey: key, useCustomImage: desired }
                    });

                    if (res.ok && res.data?.item) {
                        updateIconCardUi(card, res.data.item);
                    } else {
                        toggle.checked = !desired;
                    }
                } finally {
                    toggle.disabled = false;
                }
            });
        });

        iconGrid.querySelectorAll('.js-icon-reset-btn').forEach(btn => {
            btn.addEventListener('click', async () => {
                const key = btn.getAttribute('data-icon-key') || '';
                const name = btn.getAttribute('data-icon-name') || key;
                const card = btn.closest('.s22-icon-card');

                const ok = await confirmDialog({
                    title: `Khôi phục "${name}" về mặc định?`,
                    text: 'Biểu tượng sẽ quay về thiết kế chuẩn của hệ thống và xóa ảnh tùy chỉnh đã tải lên.',
                    confirmText: 'Khôi phục',
                    danger: true
                });
                if (!ok) return;

                const res = await apiFetch('/S22AdminAnalytics/ResetIconToDefault', {
                    method: 'POST',
                    body: { iconKey: key },
                    button: btn
                });

                if (res.ok && card) {
                    const defaultCls = card.getAttribute('data-default-class') || 'bi-app';
                    updateIconCardUi(card, {
                        iconKey: key,
                        displayName: name,
                        defaultIconClass: defaultCls,
                        customImageUrl: null,
                        useCustomImage: false
                    });
                }
            });
        });

        document.getElementById('btnResetAllIcons')?.addEventListener('click', async (e) => {
            const btn = e.currentTarget;
            const ok = await confirmDialog({
                title: 'Khôi phục tất cả 18 biểu tượng về mặc định?',
                text: 'Toàn bộ ảnh biểu tượng tùy chỉnh sẽ được dọn sạch và trả về giao diện tiêu chuẩn BookShare.',
                confirmText: 'Khôi phục tất cả',
                danger: true
            });
            if (!ok) return;

            const res = await apiFetch('/S22AdminAnalytics/ResetIconToDefault', {
                method: 'POST',
                body: { iconKey: 'ALL' },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    }
});
