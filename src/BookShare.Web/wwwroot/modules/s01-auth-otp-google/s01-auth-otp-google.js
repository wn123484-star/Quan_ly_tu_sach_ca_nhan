/**
 * MODULE S01: AUTH + OTP RESEND + GOOGLE OAUTH (s01-auth-otp-google.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S01
 */
import { apiFetch, showSystemStatus, getRecaptchaToken } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // [BOT-GUARD] Tự động gắn token Google reCAPTCHA v3 trước khi submit Form Đăng nhập / Đăng ký
    const bindRecaptchaForm = (formId, tokenInputId, actionName) => {
        const form = document.getElementById(formId);
        const tokenInput = document.getElementById(tokenInputId);
        if (!form || !tokenInput) return;

        form.addEventListener('submit', async (e) => {
            const siteKey = document.querySelector('meta[name="recaptcha-site-key"]')?.getAttribute('content')?.trim();
            if (!siteKey || form.dataset.recaptchaSubmitted === '1') {
                return;
            }
            e.preventDefault();
            tokenInput.value = await getRecaptchaToken(actionName);
            form.dataset.recaptchaSubmitted = '1';
            form.submit();
        });
    };

    bindRecaptchaForm('s01LoginForm', 'loginRecaptchaToken', 'login');
    bindRecaptchaForm('s01RegisterForm', 'registerRecaptchaToken', 'register');
    // 1. Điền nhanh tài khoản mẫu 4 Roles để test ngay
    document.querySelectorAll('[data-demo-email]').forEach(btn => {
        btn.addEventListener('click', () => {
            const email = btn.getAttribute('data-demo-email');
            const roleName = btn.getAttribute('data-demo-role');
            const emailInput = document.getElementById('loginEmail');
            const passInput = document.getElementById('loginPassword');
            if (emailInput && passInput) {
                emailInput.value = email;
                passInput.value = 'BookShare@123';
                showSystemStatus(200, `Đã chọn tài khoản vai trò [${roleName}]`, `Tài khoản: ${email}. Bấm "Đăng Nhập Vào Hệ Thống" để tiếp tục!`);
            }
        });
    });

    // 2. Xử lý 6 ô nhập mã OTP tự động nhảy chữ số
    const otpDigits = Array.from(document.querySelectorAll('.s01-otp-digit'));
    const hiddenOtpCode = document.getElementById('otpCodeFull');

    const syncFullOtp = () => {
        if (hiddenOtpCode) {
            hiddenOtpCode.value = otpDigits.map(i => i.value.trim()).join('');
        }
    };

    otpDigits.forEach((input, idx) => {
        input.addEventListener('input', (e) => {
            const val = e.target.value.replace(/\D/g, '');
            e.target.value = val ? val[0] : '';
            syncFullOtp();
            if (val && idx < otpDigits.length - 1) {
                otpDigits[idx + 1].focus();
            }
        });

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Backspace' && !input.value && idx > 0) {
                otpDigits[idx - 1].focus();
            }
        });

        input.addEventListener('paste', (e) => {
            e.preventDefault();
            const pasted = (e.clipboardData || window.clipboardData).getData('text').replace(/\D/g, '').slice(0, 6);
            pasted.split('').forEach((ch, i) => {
                if (otpDigits[i]) otpDigits[i].value = ch;
            });
            syncFullOtp();
            if (pasted.length > 0) {
                const focusIdx = Math.min(pasted.length, otpDigits.length - 1);
                otpDigits[focusIdx].focus();
            }
        });
    });

    // 3. Nút Gửi mã OTP 6 số qua Resend API
    const btnSendOtp = document.getElementById('btnSendOtp');
    if (btnSendOtp) {
        btnSendOtp.addEventListener('click', async () => {
            const emailEl = document.getElementById('otpEmail');
            const purposeEl = document.getElementById('otpPurpose');
            const email = emailEl ? emailEl.value.trim() : '';
            const purpose = purposeEl ? purposeEl.value : 'VerifyLoan';

            if (!email) {
                showSystemStatus(400, 'Thiếu địa chỉ Email nhận OTP (Mã 400)', 'Vui lòng nhập email để nhận mã xác thực 6 chữ số.');
                return;
            }

            btnSendOtp.disabled = true;
            const originalHtml = btnSendOtp.innerHTML;
            btnSendOtp.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang gửi OTP...';

            try {
                // [BOT-GUARD] Đính kèm token reCAPTCHA v3 khi gửi mã OTP
                const recaptchaToken = await getRecaptchaToken('send_otp');
                const res = await apiFetch('/S01AuthOtpGoogle/SendOtp', {
                    method: 'POST',
                    body: { email, purpose, recaptchaToken }
                });

                if (res.ok && res.data) {
                    const devBox = document.getElementById('devOtpPreviewBox');
                    const devCodeSpan = document.getElementById('devOtpCodeValue');
                    if (res.data.devOtpCode && devBox && devCodeSpan) {
                        devCodeSpan.innerText = res.data.devOtpCode;
                        devBox.classList.remove('d-none');
                        // Tự động điền vào 6 ô để thuận tiện kiểm thử
                        res.data.devOtpCode.toString().split('').forEach((ch, i) => {
                            if (otpDigits[i]) otpDigits[i].value = ch;
                        });
                        syncFullOtp();
                    }
                    showSystemStatus(200, 'Đã sinh và gửi mã OTP 6 chữ số (Mã 200)', res.data.message);
                }
            } finally {
                btnSendOtp.disabled = false;
                btnSendOtp.innerHTML = originalHtml;
            }
        });
    }

    // 4. Form Xác thực mã OTP 6 số
    const verifyOtpForm = document.getElementById('verifyOtpForm');
    if (verifyOtpForm) {
        verifyOtpForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            syncFullOtp();
            const email = document.getElementById('otpEmail')?.value.trim() || '';
            const purpose = document.getElementById('otpPurpose')?.value || 'VerifyLoan';
            const otpCode = hiddenOtpCode?.value || '';

            if (otpCode.length !== 6) {
                showSystemStatus(400, 'Mã OTP chưa đủ 6 chữ số (Mã 400)', 'Vui lòng nhập đủ 6 chữ số OTP.');
                return;
            }

            const res = await apiFetch('/S01AuthOtpGoogle/VerifyOtp', {
                method: 'POST',
                body: { email, otpCode, purpose }
            });

            if (res.ok && res.data) {
                showSystemStatus(200, 'Xác thực OTP thành công (Mã 200)', res.data.message);
                if (typeof Swal !== 'undefined') {
                    Swal.fire('Xác thực OTP Hợp lệ!', res.data.message, 'success');
                }
            }
        });
    }

    // 5. Cập nhật Hồ sơ Cá nhân (Mục 2.4 Rubric)
    const updateProfileForm = document.getElementById('s01UpdateProfileForm');
    if (updateProfileForm) {
        updateProfileForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const fullName = document.getElementById('profFullName')?.value.trim() || '';
            const phoneNumber = document.getElementById('profPhone')?.value.trim() || '';
            const address = document.getElementById('profAddress')?.value.trim() || '';
            const avatarUrl = document.getElementById('profAvatarUrl')?.value.trim() || '';

            const res = await apiFetch('/S01AuthOtpGoogle/UpdateProfile', {
                method: 'POST',
                body: { fullName, phoneNumber, address, avatarUrl }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // 6. Đổi mật khẩu tài khoản
    const changePassForm = document.getElementById('s01ChangePasswordForm');
    if (changePassForm) {
        changePassForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const currentPassword = document.getElementById('profCurrentPass')?.value || '';
            const newPassword = document.getElementById('profNewPass')?.value || '';

            const res = await apiFetch('/S01AuthOtpGoogle/ChangePassword', {
                method: 'POST',
                body: { currentPassword, newPassword }
            });

            if (res.ok && res.data) {
                showSystemStatus(200, res.data.title || 'Đổi mật khẩu thành công', res.data.message);
            }
        });
    }

    // 7. [BS-QRLOGIN-BRIDGE] Quét QR Đăng Nhập Nhanh (Init -> Countdown 180s -> Polling 2s -> ClaimQrLogin)
    const btnRefreshQr = document.getElementById('btnRefreshQrLogin');
    const qrImg = document.getElementById('s01QrImage');
    const qrPlaceholder = document.getElementById('s01QrPlaceholder');
    const qrTokenDisplay = document.getElementById('s01QrTokenDisplay');
    const qrCountdown = document.getElementById('s01QrCountdown');
    const qrStatusText = document.getElementById('s01QrStatusText');

    let qrPollTimer = null;
    let qrTickTimer = null;
    let currentQrToken = '';
    let qrSecondsLeft = 0;

    const stopQrTimers = () => {
        if (qrPollTimer) { clearInterval(qrPollTimer); qrPollTimer = null; }
        if (qrTickTimer) { clearInterval(qrTickTimer); qrTickTimer = null; }
    };

    const startQrLoginSession = async () => {
        if (!btnRefreshQr) return;
        stopQrTimers();
        if (qrStatusText) qrStatusText.innerText = 'Đang tạo phiên QR đăng nhập an toàn (hiệu lực 180 giây)...';

        const res = await apiFetch('/api/mobile/qr-login/init', { method: 'POST', button: btnRefreshQr });
        if (!res.ok || !res.data?.token) return;

        currentQrToken = res.data.token;
        qrSecondsLeft = res.data.expiresInSeconds || 180;
        if (qrTokenDisplay) qrTokenDisplay.innerText = currentQrToken;
        if (qrCountdown) {
            qrCountdown.classList.remove('d-none', 'bg-danger');
            qrCountdown.classList.add('bg-primary');
            qrCountdown.innerText = `${qrSecondsLeft}s`;
        }
        if (qrImg) {
            qrImg.src = `https://api.qrserver.com/v1/create-qr-code/?size=160x160&data=${encodeURIComponent(currentQrToken)}`;
            qrImg.classList.remove('d-none');
        }
        if (qrPlaceholder) qrPlaceholder.classList.add('d-none');
        if (qrStatusText) qrStatusText.innerText = 'Đang chờ thiết bị Mobile BookShare quét & xác nhận duyệt...';

        qrTickTimer = setInterval(() => {
            qrSecondsLeft = Math.max(0, qrSecondsLeft - 1);
            if (qrCountdown) qrCountdown.innerText = `${qrSecondsLeft}s`;
            if (qrSecondsLeft <= 0) {
                stopQrTimers();
                if (qrCountdown) {
                    qrCountdown.classList.remove('bg-primary');
                    qrCountdown.classList.add('bg-danger');
                    qrCountdown.innerText = 'Hết hạn';
                }
                if (qrStatusText) qrStatusText.innerText = 'Mã QR đã hết hiệu lực. Bấm "Tạo / Làm mới QR" để lấy mã mới.';
            }
        }, 1000);

        qrPollTimer = setInterval(async () => {
            if (!currentQrToken) return;
            try {
                const r = await fetch(`/api/mobile/qr-login/status?token=${encodeURIComponent(currentQrToken)}`, {
                    headers: { Accept: 'application/json' },
                    credentials: 'same-origin'
                });
                if (!r.ok) return;
                const st = await r.json();
                if (st.status === 'Approved') {
                    stopQrTimers();
                    if (qrStatusText) qrStatusText.innerText = `Đã duyệt bởi ${st.approvedUserName || 'Mobile App'}! Đang đăng nhập Web PC...`;
                    const claim = await apiFetch('/S01AuthOtpGoogle/ClaimQrLogin', {
                        method: 'POST',
                        body: { token: currentQrToken },
                        button: null
                    });
                    if (claim.ok && claim.data?.redirectUrl) {
                        window.location.href = claim.data.redirectUrl;
                    }
                } else if (st.status === 'Expired') {
                    stopQrTimers();
                    if (qrStatusText) qrStatusText.innerText = 'Phiên QR đã hết hạn. Vui lòng bấm làm mới mã QR.';
                }
            } catch { /* thử lại ở vòng poll tiếp theo */ }
        }, 2000);
    };

    if (btnRefreshQr) {
        btnRefreshQr.addEventListener('click', startQrLoginSession);
        startQrLoginSession();
    }
});
