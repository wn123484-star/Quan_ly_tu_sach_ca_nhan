import {
    apiFetch,
    showSystemStatus,
    confirmDialog,
    renderQrCode,
    showQrPaymentInvoiceModal,
    openQrInvoiceConfirmDialog,
    celebrate
} from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. Nạp Tiền Ảo Miễn Phí Thoải Mái
    const topUpInput = document.getElementById('topUpAmountInput');
    const topUpNote = document.getElementById('topUpNoteInput');
    document.querySelectorAll('.js-quick-topup-chip').forEach(btn => {
        btn.addEventListener('click', () => {
            if (topUpInput) topUpInput.value = btn.getAttribute('data-amount') || '100000';
        });
    });

    document.getElementById('formVirtualTopUp')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const amount = parseFloat(topUpInput?.value || '0');
        const note = topUpNote?.value.trim() || '';
        const res = await apiFetch('/S17SandboxPayment/TopUpVirtualWallet', {
            method: 'POST',
            body: { amount, note }
        });
        if (res.ok) {
            celebrate();
            setTimeout(() => window.location.reload(), 700);
        }
    });

    // 2. Vẽ ngay Mã QR cho hóa đơn đang chọn (?txId=...) & tự động polling đồng bộ Web <-> Web App
    const activePanel = document.getElementById('s17ActiveInvoicePanel');
    const activeQrCanvas = document.getElementById('s17SelectedQrCanvas');
    if (activePanel && activeQrCanvas) {
        const txId = activePanel.getAttribute('data-tx-id');
        const qrPayload = activePanel.getAttribute('data-qr-payload');
        if (qrPayload) {
            renderQrCode(activeQrCanvas, qrPayload, 168);
        }
        if (txId) {
            const pollTimer = setInterval(async () => {
                try {
                    const r = await fetch(`/S17SandboxPayment/InvoiceStatus?txId=${encodeURIComponent(txId)}`, {
                        headers: { Accept: 'application/json' },
                        credentials: 'same-origin'
                    });
                    if (!r.ok) return;
                    const data = await r.json();
                    if (data.status === 'Completed') {
                        clearInterval(pollTimer);
                        celebrate();
                        window.location.reload();
                    }
                } catch { /* bỏ qua */ }
            }, 2500);
            window.addEventListener('beforeunload', () => clearInterval(pollTimer));
        }
    }

    // 3. Tạo hóa đơn nội bộ & bật ngay hộp thoại Mã QR Thanh Toán
    const txTypeSelect = document.getElementById('txTypeSelect');
    const txTierSelect = document.getElementById('txTargetTierSelect');
    const txAmountInput = document.getElementById('txAmountInput');
    const tierPickGroup = document.getElementById('membershipTierPickGroup');

    const syncOrderDefaults = () => {
        const type = txTypeSelect?.value || 'Membership';
        if (tierPickGroup) tierPickGroup.classList.toggle('d-none', type !== 'Membership');
        if (type === 'Membership' && txTierSelect && txAmountInput) {
            txAmountInput.value = txTierSelect.selectedOptions[0]?.getAttribute('data-amount') || '49000';
        } else if (txTypeSelect && txAmountInput) {
            txAmountInput.value = txTypeSelect.selectedOptions[0]?.getAttribute('data-amount') || '25000';
        }
    };
    txTypeSelect?.addEventListener('change', syncOrderDefaults);
    txTierSelect?.addEventListener('change', syncOrderDefaults);

    document.getElementById('formCreateSandboxOrder')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const transactionType = txTypeSelect?.value || 'Membership';
        const amount = parseFloat(txAmountInput?.value || '49000');
        const targetTier = transactionType === 'Membership' ? (txTierSelect?.value || 'Premium') : null;
        const loanIdRaw = document.getElementById('txLoanIdSelect')?.value;
        const loanId = loanIdRaw ? parseInt(loanIdRaw, 10) : null;

        const res = await apiFetch('/S17SandboxPayment/CreateOrder', {
            method: 'POST',
            body: {
                transactionType,
                amount,
                paymentGateway: 'QrPay',
                loanId,
                targetTier,
                durationMonths: transactionType === 'Membership' ? 1 : null
            }
        });

        if (res.ok && res.data) {
            const cbId = document.getElementById('cbTransactionId');
            const cbRaw = document.getElementById('cbRawPayload');
            const cbHmac = document.getElementById('cbHmacSignature');
            if (cbId) cbId.value = res.data.transactionId;
            if (cbRaw) cbRaw.value = res.data.rawPayload;
            if (cbHmac) cbHmac.value = res.data.expectedHmacSignature;

            await showQrPaymentInvoiceModal({
                transactionId: res.data.transactionId,
                amount,
                description: res.data.message,
                qrPayload: res.data.qrPayload
            });
        }
    });

    // 4. Nút Hiện Mã QR / Thanh Toán Ngay trên từng dòng bảng lịch sử
    document.querySelectorAll('.js-s17-show-qr').forEach(btn => {
        btn.addEventListener('click', async () => {
            await showQrPaymentInvoiceModal({
                transactionId: parseInt(btn.getAttribute('data-tx-id') || '0', 10),
                amount: parseFloat(btn.getAttribute('data-amount') || '0'),
                description: btn.getAttribute('data-desc') || '',
                qrPayload: btn.getAttribute('data-qr-payload') || ''
            });
        });
    });

    document.querySelectorAll('.js-s17-pay-now').forEach(btn => {
        btn.addEventListener('click', async () => {
            const qrPayload = btn.getAttribute('data-qr-payload') || '';
            await openQrInvoiceConfirmDialog(qrPayload);
        });
    });

    // 5. Đối soát nâng cao & Hoàn cọc / Hủy hóa đơn
    document.getElementById('btnTamperSignature')?.addEventListener('click', () => {
        const sigEl = document.getElementById('cbHmacSignature');
        if (!sigEl) return;
        sigEl.value = 'tampered_invalid_signature_' + Date.now();
        showSystemStatus(300, 'Đã thử sửa mã đối soát', 'Bấm Xác nhận đối soát để kiểm tra hệ thống từ chối mã không hợp lệ.');
    });

    document.getElementById('formVerifyCallback')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const transactionId = parseInt(document.getElementById('cbTransactionId')?.value || '0', 10);
        const rawPayload = document.getElementById('cbRawPayload')?.value || '';
        const hmacSignature = document.getElementById('cbHmacSignature')?.value || '';
        const responseCode = document.getElementById('cbResponseCode')?.value || '00';

        const res = await apiFetch('/S17SandboxPayment/VerifyCallback', {
            method: 'POST',
            body: { transactionId, rawPayload, hmacSignature, responseCode }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
        }
    });

    document.querySelectorAll('.js-refund-deposit').forEach(btn => {
        btn.addEventListener('click', async () => {
            const transactionId = parseInt(btn.getAttribute('data-tx-id') || '0', 10);
            const res = await apiFetch('/S17SandboxPayment/RefundDeposit', {
                method: 'POST',
                body: { transactionId },
                button: btn
            });
            if (res.ok) {
                setTimeout(() => window.location.reload(), 650);
            }
        });
    });

    document.querySelectorAll('.js-s17-cancel-tx').forEach(btn => {
        btn.addEventListener('click', async () => {
            const transactionId = parseInt(btn.getAttribute('data-tx-id') || '0', 10);
            if (!transactionId) return;

            const ok = await confirmDialog({
                title: `Hủy hóa đơn #TX-${transactionId}?`,
                text: 'Hóa đơn chờ thanh toán này sẽ bị xóa khỏi lịch sử.',
                confirmText: 'Xóa hóa đơn',
                danger: true
            });
            if (!ok) return;

            const res = await apiFetch('/S17SandboxPayment/CancelTransaction', {
                method: 'POST',
                body: { transactionId },
                button: btn
            });
            if (res.ok) {
                window.location.reload();
            }
        });
    });
});
