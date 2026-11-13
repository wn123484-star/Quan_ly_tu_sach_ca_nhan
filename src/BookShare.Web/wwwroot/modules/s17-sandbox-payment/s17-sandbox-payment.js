import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
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
});
