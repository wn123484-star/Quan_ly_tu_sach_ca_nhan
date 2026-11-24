import { apiFetch, showQrPaymentInvoiceModal, celebrate, confirmDialog } from '../../js/core-api.js';

function makeIdempotencyKey(prefix) {
    if (window.crypto?.randomUUID) {
        return `${prefix}-${window.crypto.randomUUID()}`;
    }
    return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function fmtVnd(val) {
    return `${Math.round(Number(val) || 0).toLocaleString('vi-VN')}đ ảo`;
}

document.addEventListener('DOMContentLoaded', () => {
    let selectedMonths = 12;

    const updateCardPrices = () => {
        document.querySelectorAll('.js-tier-price').forEach(el => {
            const p = Number(el.getAttribute(`data-p${selectedMonths}`) || '0');
            el.textContent = p <= 0 ? '0đ ảo' : `${Math.round(p).toLocaleString('vi-VN')}đ ảo`;
        });
        document.querySelectorAll('.js-tier-period').forEach(el => {
            if (el.textContent.includes('trọn đời')) return;
            el.textContent = `/ ${selectedMonths} tháng (Tiền ảo)`;
        });
    };

    document.querySelectorAll('.js-s16-duration').forEach(btn => {
        btn.addEventListener('click', () => {
            selectedMonths = parseInt(btn.getAttribute('data-months') || '12', 10);
            document.querySelectorAll('.js-s16-duration').forEach(b => {
                b.classList.toggle('btn-primary', b === btn);
                b.classList.toggle('btn-outline-primary', b !== btn);
            });
            updateCardPrices();
        });
    });

    // Đăng ký mới hoặc Chuyển gói (hiển thị báo giá minh bạch lệ phí chuyển đổi + khấu trừ ngày dư trước khi xác nhận)
    document.querySelectorAll('.js-switch-tier').forEach(btn => {
        btn.addEventListener('click', async () => {
            const targetTier = btn.getAttribute('data-tier') || 'Premium';
            const paymentMode = btn.getAttribute('data-mode') || 'QrInvoice';
            const isSwitch = btn.getAttribute('data-is-switch') === '1';

            const prevRes = await apiFetch('/S16MembershipTier/PreviewTierSwitch', {
                method: 'POST',
                body: { targetTier, durationMonths: selectedMonths },
                button: btn
            });
            if (!prevRes.ok || !prevRes.data?.preview) return;

            const pv = prevRes.data.preview;
            const modalTitle = pv.isSwitch
                ? `Xác Nhận Chuyển Gói ${pv.currentTier} → ${pv.targetTier}`
                : `Xác Nhận Đăng Ký Gói ${pv.targetTierDisplayName}`;

            const rowsHtml = pv.isSwitch
                ? `
                    <div class="d-flex justify-content-between py-1"><span>Giá gói ${pv.targetTierDisplayName} (${pv.durationMonths}T):</span><strong>${fmtVnd(pv.newPlanPrice)}</strong></div>
                    <div class="d-flex justify-content-between py-1 text-success"><span>Hoàn trừ ngày còn dư gói ${pv.currentTier} (${pv.remainingDays} ngày):</span><strong>-${fmtVnd(pv.prorationCredit)}</strong></div>
                    <div class="d-flex justify-content-between py-1 text-primary"><span>Lệ phí chuyển đổi gói (minh bạch):</span><strong>+${fmtVnd(pv.switchFee)}</strong></div>`
                : `<div class="d-flex justify-content-between py-1"><span>Phí đăng ký gói ${pv.targetTierDisplayName} (${pv.durationMonths}T):</span><strong>${fmtVnd(pv.newPlanPrice)}</strong></div>`;

            const enoughNote = paymentMode === 'InstantWallet'
                ? (pv.hasSufficientBalance
                    ? `<div class="alert alert-success py-2 px-3 small mb-0 mt-2">Số dư Ví hiện tại: <strong>${fmtVnd(pv.walletBalance)}</strong> • Đủ thanh toán ngay.</div>`
                    : `<div class="alert alert-warning py-2 px-3 small mb-0 mt-2">Số dư Ví hiện tại: <strong>${fmtVnd(pv.walletBalance)}</strong> • Còn thiếu <strong>${fmtVnd(pv.shortfallAmount)}</strong>. Hãy nạp tiền ảo miễn phí trước!</div>`)
                : `<div class="alert alert-info py-2 px-3 small mb-0 mt-2">Hệ thống sẽ sinh 1 mã QR hóa đơn duy nhất để bạn quét &amp; xác nhận thanh toán.</div>`;

            const confirmResult = await window.Swal.fire({
                title: modalTitle,
                html: `
                    <div class="text-start small border rounded-4 p-3 bg-body-tertiary">
                        ${rowsHtml}
                        <hr class="my-2" />
                        <div class="d-flex justify-content-between fs-6 fw-bold text-primary">
                            <span>Tổng thanh toán (1 lần duy nhất):</span>
                            <span>${fmtVnd(pv.totalPayable)}</span>
                        </div>
                        ${enoughNote}
                    </div>`,
                showCancelButton: true,
                confirmButtonText: paymentMode === 'InstantWallet'
                    ? (pv.hasSufficientBalance ? (isSwitch ? 'Xác Nhận Chuyển Gói' : 'Xác Nhận Đăng Ký') : 'Đi Nạp Tiền Ảo Miễn Phí')
                    : 'Tạo Mã QR Thanh Toán',
                cancelButtonText: 'Hủy',
                confirmButtonColor: paymentMode === 'InstantWallet' && !pv.hasSufficientBalance ? '#16a34a' : '#2563eb',
                reverseButtons: true
            });

            if (!confirmResult.isConfirmed) return;

            if (paymentMode === 'InstantWallet' && !pv.hasSufficientBalance) {
                window.location.href = '/S17SandboxPayment#walletTopUpCard';
                return;
            }

            const res = await apiFetch('/S16MembershipTier/SwitchMembership', {
                method: 'POST',
                body: {
                    targetTier,
                    durationMonths: selectedMonths,
                    paymentMode,
                    paymentGateway: paymentMode === 'InstantWallet' ? 'VirtualWallet' : 'QrPay',
                    idempotencyKey: makeIdempotencyKey(`tier-${targetTier}`)
                },
                button: btn
            });

            if (!res.ok) return;

            if (res.data?.createdTransactionId) {
                await showQrPaymentInvoiceModal({
                    transactionId: res.data.createdTransactionId,
                    amount: pv.totalPayable,
                    description: pv.isSwitch
                        ? `Chuyển gói ${pv.currentTier} → ${pv.targetTier} (${selectedMonths} tháng, đã gồm phí chuyển 15.000đ)`
                        : `Đăng ký gói hội viên ${pv.targetTierDisplayName} (${selectedMonths} tháng)`,
                    qrPayload: res.data.qrPayload
                });
            } else {
                celebrate();
                setTimeout(() => window.location.reload(), 700);
            }
        });
    });

    // Nút Hủy gói hội viên trả phí về gói Free
    document.querySelectorAll('.js-cancel-membership').forEach(btn => {
        btn.addEventListener('click', async () => {
            const activeTier = btn.getAttribute('data-active-tier') || 'hiện tại';
            const ok = await confirmDialog({
                title: `Hủy gói hội viên ${activeTier}?`,
                text: 'Tài khoản sẽ quay về gói Free (tối đa 2 cuốn + thưởng P2P, hạn mượn 14 ngày) và tự động hủy mọi hóa đơn chờ thanh toán gói cũ. Nếu đang mượn quá hạn mức Free bạn cần trả bớt sách trước.',
                confirmText: 'Xác nhận hủy gói',
                cancelText: 'Giữ lại gói',
                danger: true
            });
            if (!ok) return;

            const res = await apiFetch('/S16MembershipTier/CancelMembership', {
                method: 'POST',
                body: { reason: `Thành viên chủ động hủy gói ${activeTier}` },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 700);
            }
        });
    });
});
