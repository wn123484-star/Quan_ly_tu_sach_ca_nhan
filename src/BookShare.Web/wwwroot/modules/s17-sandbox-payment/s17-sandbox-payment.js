import { apiFetch, showSystemStatus, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // [BS-WALLET-PAY] Tải số dư Ví Bảo Lãnh BookShare Pay 3D & xử lý Nạp Demo / Thanh toán QR 2 bước + PIN / Đổi PIN
    const refreshWalletSummary = async () => {
        try {
            const r = await fetch('/api/mobile/wallet/summary', { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
            if (!r.ok) return;
            const w = await r.json();
            const fmt = (n) => `${Number(n || 0).toLocaleString('vi-VN')} VNĐ`;
            const elBal = document.getElementById('s17WalletBalance');
            const elEsc = document.getElementById('s17WalletEscrow');
            const elRev = document.getElementById('s17WalletRevenue');
            const elOwn = document.getElementById('s17WalletOwner');
            const elNo = document.getElementById('s17WalletCardNo');
            if (elBal) elBal.innerText = fmt(w.balance);
            if (elEsc) elEsc.innerText = fmt(w.escrowHeldTotal);
            if (elRev) elRev.innerText = fmt(w.lenderRevenueTotal);
            if (elOwn && w.userFullName) elOwn.innerText = w.userFullName;
            if (elNo && w.maskedCardNumber) elNo.innerText = w.maskedCardNumber;
        } catch { /* bỏ qua lỗi mạng tạm */ }
    };
    refreshWalletSummary();

    const doTopUp = async (amount, btn) => {
        const res = await apiFetch('/api/mobile/wallet/topup', {
            method: 'POST',
            body: { amount, note: 'Nạp tiền Demo BookShare Pay' },
            button: btn
        });
        if (res.ok) {
            await refreshWalletSummary();
            setTimeout(() => window.location.reload(), 700);
        }
    };

    document.querySelectorAll('.js-wallet-quick-topup').forEach(btn => {
        btn.addEventListener('click', () => {
            const amt = parseFloat(btn.getAttribute('data-amount') || '200000');
            doTopUp(amt, btn);
        });
    });

    document.getElementById('formWalletCustomTopUp')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const amt = parseFloat(document.getElementById('walletCustomTopUpAmt')?.value || '0');
        doTopUp(amt, e.submitter);
    });

    const qrRefInput = document.getElementById('walletQrRef');
    if (qrRefInput && !qrRefInput.value) {
        qrRefInput.value = `QRPAY-${Date.now().toString().slice(-6)}`;
    }

    document.getElementById('formWalletPayQr')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const amount = parseFloat(document.getElementById('walletQrAmount')?.value || '0');
        const pin = document.getElementById('walletQrPin')?.value.trim() || '';
        const paymentRef = document.getElementById('walletQrRef')?.value.trim() || `QRPAY-${Date.now()}`;

        const res = await apiFetch('/api/mobile/wallet/pay-qr', {
            method: 'POST',
            body: { amount, pin, paymentRef, description: 'Thanh toán QR 2 bước BookShare Pay' },
            button: e.submitter
        });
        if (res.ok) {
            if (qrRefInput) qrRefInput.value = `QRPAY-${Date.now().toString().slice(-6)}`;
            await refreshWalletSummary();
            setTimeout(() => window.location.reload(), 800);
        }
    });

    document.getElementById('formWalletChangePin')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const currentPin = document.getElementById('walletOldPin')?.value.trim() || '';
        const newPin = document.getElementById('walletNewPin')?.value.trim() || '';
        const res = await apiFetch('/api/mobile/wallet/change-pin', {
            method: 'POST',
            body: { currentPin, newPin },
            button: e.submitter
        });
        if (res.ok) {
            e.target.reset();
        }
    });

    const txTypeSelect = document.getElementById('txTypeSelect');
    const txAmountInput = document.getElementById('txAmountInput');
    const formCreateOrder = document.getElementById('formCreateSandboxOrder');
    const formVerifyCallback = document.getElementById('formVerifyCallback');
    const btnTamper = document.getElementById('btnTamperSignature');

    txTypeSelect?.addEventListener('change', () => {
        const opt = txTypeSelect.selectedOptions[0];
        const defaultAmt = opt?.getAttribute('data-amount') || '99000';
        if (txAmountInput) txAmountInput.value = defaultAmt;
    });

    // Bước 1: Tạo giao dịch Pending & lấy chữ ký HMAC-SHA512
    formCreateOrder?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const transactionType = txTypeSelect?.value || 'Membership';
        const amount = parseFloat(txAmountInput?.value || '99000');
        const paymentGateway = document.getElementById('txGatewaySelect')?.value || 'VNPaySandbox';
        const loanIdRaw = document.getElementById('txLoanIdSelect')?.value;
        const loanId = loanIdRaw ? parseInt(loanIdRaw, 10) : null;

        const res = await apiFetch('/S17SandboxPayment/CreateOrder', {
            method: 'POST',
            body: {
                transactionType,
                amount,
                paymentGateway,
                loanId
            }
        });

        if (res.ok && res.data) {
            document.getElementById('cbTransactionId').value = res.data.transactionId;
            document.getElementById('cbRawPayload').value = res.data.rawPayload;
            document.getElementById('cbHmacSignature').value = res.data.expectedHmacSignature;
        }
    });

    // Nút giả lập sửa chữ ký để kiểm thử chặn giả mạo HMAC-SHA512
    btnTamper?.addEventListener('click', () => {
        const sigEl = document.getElementById('cbHmacSignature');
        if (!sigEl) return;
        sigEl.value = 'tampered_invalid_hmac_sha512_' + Date.now();
        showSystemStatus(300, 'Đã giả lập can thiệp chữ ký HMAC-SHA512', 'Hãy bấm nút Xác thực Callback để kiểm tra cơ chế từ chối chữ ký giả mạo của Backend.');
    });

    // Bước 2: Gửi Callback kèm chữ ký HMAC-SHA512 lên Backend kiểm tra
    formVerifyCallback?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const transactionId = parseInt(document.getElementById('cbTransactionId')?.value || '0', 10);
        const rawPayload = document.getElementById('cbRawPayload')?.value || '';
        const hmacSignature = document.getElementById('cbHmacSignature')?.value || '';
        const responseCode = document.getElementById('cbResponseCode')?.value || '00';

        const res = await apiFetch('/S17SandboxPayment/VerifyCallback', {
            method: 'POST',
            body: {
                transactionId,
                rawPayload,
                hmacSignature,
                responseCode
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 800);
        }
    });

    // Hoàn trả tiền cọc mô phỏng
    document.querySelectorAll('.js-refund-deposit').forEach(btn => {
        btn.addEventListener('click', async () => {
            const transactionId = parseInt(btn.getAttribute('data-tx-id') || '0', 10);
            const res = await apiFetch('/S17SandboxPayment/RefundDeposit', {
                method: 'POST',
                body: { transactionId }
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
                            title: `Xóa giao dịch #${transactionId}?`,
                            text: 'Lệnh giao dịch chưa hoàn tất này sẽ được xóa khỏi bảng Transactions.',
                            confirmText: 'Xóa giao dịch'
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
