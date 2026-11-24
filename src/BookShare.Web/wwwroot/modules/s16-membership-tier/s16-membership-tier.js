import { apiFetch, showQrPaymentInvoiceModal, celebrate } from '../../js/core-api.js';

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

    document.querySelectorAll('.js-switch-tier').forEach(btn => {
        btn.addEventListener('click', async () => {
            const targetTier = btn.getAttribute('data-tier') || 'Premium';
            const paymentMode = btn.getAttribute('data-mode') || 'QrInvoice';
            const priceEl = btn.closest('.s16-tier-card')?.querySelector('.js-tier-price');
            const expectedAmount = Number(priceEl?.getAttribute(`data-p${selectedMonths}`) || '0');

            const res = await apiFetch('/S16MembershipTier/SwitchMembership', {
                method: 'POST',
                body: {
                    targetTier,
                    durationMonths: selectedMonths,
                    paymentMode,
                    paymentGateway: paymentMode === 'InstantWallet' ? 'VirtualWallet' : 'QrPay'
                },
                button: btn
            });

            if (!res.ok) return;

            if (res.data?.createdTransactionId) {
                await showQrPaymentInvoiceModal({
                    transactionId: res.data.createdTransactionId,
                    amount: expectedAmount,
                    description: `Nâng cấp gói hội viên ${targetTier} (${selectedMonths} tháng)`,
                    qrPayload: res.data.qrPayload
                });
            } else {
                celebrate();
                setTimeout(() => window.location.reload(), 700);
            }
        });
    });
});
