/**
 * [S01] FRONTEND MODULE (s01-auth-otp-google.js)
 * Tài liệu ánh xạ dịch vụ & phân quyền: docs/SERVICE_CODE_MAP.md
 */
/**
 * XÁC THỰC, QUÊN MẬT KHẨU OTP, ĐĂNG NHẬP NHANH QR, TẢI AVATAR & HỒ SƠ CÁ NHÂN (s01-auth-otp-google.js)
 */
import { apiFetch, showSystemStatus, getRecaptchaToken, renderQrCode, openUniversalScanner, toast, getUiPrefs, applyUiPrefs } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
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

    // Chống trình duyệt tự động điền (autofill) email/mật khẩu đăng nhập cũ vào tab Đăng ký mới
    const clearRegisterAutofill = () => {
        const regForm = document.getElementById('s01RegisterForm');
        if (!regForm || regForm.dataset.userTyped === '1') return;
        const regEmail = document.getElementById('regEmail');
        const regPass = document.getElementById('regPassword');
        if (regEmail) regEmail.value = '';
        if (regPass) regPass.value = '';
    };
    const regFormEl = document.getElementById('s01RegisterForm');
    regFormEl?.addEventListener('input', () => { regFormEl.dataset.userTyped = '1'; }, { once: true });
    document.getElementById('btnTabRegister')?.addEventListener('shown.bs.tab', () => {
        clearRegisterAutofill();
        setTimeout(clearRegisterAutofill, 120);
    });
    setTimeout(clearRegisterAutofill, 250);

    // ---------- QUÊN MẬT KHẨU (EMAIL -> XÁC THỰC OTP -> ĐẶT MẬT KHẨU MỚI) ----------
    const fpPanel = document.getElementById('s01ForgotPasswordPanel');
    const btnOpenFp = document.getElementById('btnOpenForgotPassword');
    const btnCloseFp = document.getElementById('btnCloseForgotPassword');
    const fpEmailInput = document.getElementById('fpEmailInput');
    const btnFpSendOtp = document.getElementById('btnFpSendOtp');
    const fpDevBanner = document.getElementById('fpDevOtpBanner');
    const fpDevVal = document.getElementById('fpDevOtpValue');
    const fpStep2Box = document.getElementById('fpStep2Box');
    const fpOtpInput = document.getElementById('fpOtpInput');
    const btnFpVerifyOtp = document.getElementById('btnFpVerifyOtp');
    const fpStepStatus = document.getElementById('fpStepStatus');
    const fpStep3Box = document.getElementById('fpStep3Box');
    const fpNewPassInput = document.getElementById('fpNewPassInput');
    const fpConfirmPassInput = document.getElementById('fpConfirmPassInput');
    const btnFpSubmitReset = document.getElementById('btnFpSubmitReset');

    const setFpStatus = (msg, isOk) => {
        if (!fpStepStatus) return;
        fpStepStatus.textContent = msg;
        fpStepStatus.className = `small fw-semibold mb-2 ${isOk ? 'text-success' : 'text-danger'}`;
        fpStepStatus.classList.remove('d-none');
    };

    btnOpenFp?.addEventListener('click', () => {
        fpPanel?.classList.remove('d-none');
        const loginEmailVal = document.getElementById('loginEmail')?.value.trim() || '';
        if (loginEmailVal && fpEmailInput && !fpEmailInput.value) {
            fpEmailInput.value = loginEmailVal;
        }
        fpEmailInput?.focus();
    });

    btnCloseFp?.addEventListener('click', () => {
        fpPanel?.classList.add('d-none');
    });

    btnFpSendOtp?.addEventListener('click', async () => {
        const email = fpEmailInput?.value.trim() || '';
        if (!email) {
            setFpStatus('Vui lòng nhập địa chỉ Email đã đăng ký.', false);
            return;
        }
        const recaptchaToken = await getRecaptchaToken('forgot_password');
        const res = await apiFetch('/S01AuthOtpGoogle/SendPasswordResetOtp', {
            method: 'POST',
            button: btnFpSendOtp,
            body: { email, recaptchaToken }
        });
        if (res.ok && res.data) {
            fpStep2Box?.classList.remove('d-none');
            fpStep3Box?.classList.add('d-none');
            setFpStatus(res.data.message || 'Đã gửi mã OTP 6 chữ số tới Email của bạn.', true);
            if (res.data.devOtpCode && fpDevBanner && fpDevVal) {
                fpDevVal.textContent = res.data.devOtpCode;
                fpDevBanner.classList.remove('d-none');
                if (fpOtpInput) fpOtpInput.value = res.data.devOtpCode;
            }
            fpOtpInput?.focus();
        } else {
            setFpStatus(res.message || 'Không thể gửi mã OTP cho địa chỉ Email này.', false);
        }
    });

    btnFpVerifyOtp?.addEventListener('click', async () => {
        const email = fpEmailInput?.value.trim() || '';
        const otpCode = fpOtpInput?.value.trim() || '';
        if (!email || otpCode.length !== 6) {
            setFpStatus('Mã OTP nhập chưa chính xác. Vui lòng nhập đủ 6 chữ số.', false);
            return;
        }
        const res = await apiFetch('/S01AuthOtpGoogle/VerifyPasswordResetOtp', {
            method: 'POST',
            button: btnFpVerifyOtp,
            body: { email, otpCode }
        });
        if (res.ok) {
            setFpStatus(res.data?.message || 'Mã OTP chính xác! Mời bạn đặt mật khẩu mới bên dưới.', true);
            fpStep3Box?.classList.remove('d-none');
            fpNewPassInput?.focus();
        } else {
            fpStep3Box?.classList.add('d-none');
            setFpStatus(res.message || 'Mã OTP nhập chưa chính xác. Vui lòng kiểm tra lại.', false);
        }
    });

    btnFpSubmitReset?.addEventListener('click', async () => {
        const email = fpEmailInput?.value.trim() || '';
        const otpCode = fpOtpInput?.value.trim() || '';
        const newPassword = fpNewPassInput?.value || '';
        const confirmPassword = fpConfirmPassInput?.value || '';

        if (newPassword.length < 6) {
            setFpStatus('Mật khẩu mới phải có tối thiểu 6 ký tự.', false);
            return;
        }
        if (newPassword !== confirmPassword) {
            setFpStatus('Mật khẩu nhập lại không khớp với mật khẩu mới.', false);
            return;
        }

        const res = await apiFetch('/S01AuthOtpGoogle/ResetPasswordWithOtp', {
            method: 'POST',
            button: btnFpSubmitReset,
            body: { email, otpCode, newPassword, confirmPassword }
        });

        if (res.ok) {
            toast(200, 'Đặt lại mật khẩu thành công', 'Bạn có thể đăng nhập ngay bằng mật khẩu mới.');
            const loginEmail = document.getElementById('loginEmail');
            const loginPass = document.getElementById('loginPassword');
            if (loginEmail) loginEmail.value = email;
            if (loginPass) {
                loginPass.value = '';
                loginPass.focus();
            }
            fpPanel?.classList.add('d-none');
            fpStep2Box?.classList.add('d-none');
            fpStep3Box?.classList.add('d-none');
            fpDevBanner?.classList.add('d-none');
            if (fpOtpInput) fpOtpInput.value = '';
            if (fpNewPassInput) fpNewPassInput.value = '';
            if (fpConfirmPassInput) fpConfirmPassInput.value = '';
        } else {
            setFpStatus(res.message || 'Không thể đặt lại mật khẩu.', false);
        }
    });

    // ---------- TẢI ẢNH ĐẠI DIỆN (UPLOAD TRỰC TIẾP, KHÔNG NHẬP LINK) ----------
    const avatarFileInput = document.getElementById('profAvatarFileInput');
    const btnUploadAvatar = document.getElementById('btnUploadAvatar');
    const btnResetAvatar = document.getElementById('btnResetAvatar');
    const avatarPreviewImg = document.getElementById('s01AvatarPreview');

    btnUploadAvatar?.addEventListener('click', () => avatarFileInput?.click());

    avatarFileInput?.addEventListener('change', async () => {
        const file = avatarFileInput.files?.[0];
        if (!file) return;
        if (file.size > 2 * 1024 * 1024) {
            showSystemStatus(400, 'Ảnh quá dung lượng', 'Ảnh đại diện không được vượt quá 2MB.');
            avatarFileInput.value = '';
            return;
        }

        // Xem trước ảnh ngay lập tức trước khi tải xong
        const localUrl = URL.createObjectURL(file);
        if (avatarPreviewImg) {
            avatarPreviewImg.src = localUrl;
        }

        const formData = new FormData();
        formData.append('file', file);

        try {
            const res = await apiFetch('/S01AuthOtpGoogle/UploadAvatar', {
                method: 'POST',
                body: formData,
                button: btnUploadAvatar
            });
            if (res.ok && res.data?.avatarUrl) {
                const freshUrl = `${res.data.avatarUrl}?t=${Date.now()}`;
                if (avatarPreviewImg) avatarPreviewImg.src = freshUrl;
                document.querySelectorAll('.bs-header-avatar').forEach(img => { img.src = freshUrl; });
                setTimeout(() => window.location.reload(), 650);
            }
        } finally {
            avatarFileInput.value = '';
            setTimeout(() => URL.revokeObjectURL(localUrl), 5000);
        }
    });

    btnResetAvatar?.addEventListener('click', async () => {
        const res = await apiFetch('/S01AuthOtpGoogle/ResetAvatar', {
            method: 'POST',
            body: {},
            button: btnResetAvatar
        });
        if (res.ok) {
            const defUrl = res.data?.avatarUrl || '/images/avatar-default.svg';
            if (avatarPreviewImg) avatarPreviewImg.src = defUrl;
            setTimeout(() => window.location.reload(), 550);
        }
    });

    const updateProfileForm = document.getElementById('s01UpdateProfileForm');
    if (updateProfileForm) {
        updateProfileForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const fullName = document.getElementById('profFullName')?.value.trim() || '';
            const email = document.getElementById('profEmail')?.value.trim() || '';
            const phoneNumber = document.getElementById('profPhone')?.value.trim() || '';
            const address = document.getElementById('profAddress')?.value.trim() || '';

            const res = await apiFetch('/S01AuthOtpGoogle/UpdateProfile', {
                method: 'POST',
                body: { fullName, email, phoneNumber, address }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // ---------- TAB CÀI ĐẶT GIAO DIỆN, PHONG CÁCH, LOẠI NỀN, PHÔNG CHỮ & CỠ CHỮ ----------
    const prefTheme = document.getElementById('prefThemeMode');
    const prefStyle = document.getElementById('prefUiStyle');
    const prefPalette = document.getElementById('prefPalette');
    const prefBg = document.getElementById('prefBgPattern');
    const prefFont = document.getElementById('prefFontFamily');
    const prefSize = document.getElementById('prefFontSize');

    const syncPrefInputs = (p) => {
        if (prefTheme) prefTheme.value = p.theme || 'light';
        if (prefStyle) prefStyle.value = p.style || 'standard';
        if (prefPalette) prefPalette.value = p.palette || 'blue';
        if (prefBg) prefBg.value = p.background || 'solid';
        if (prefFont) prefFont.value = p.font || 'roboto';
        if (prefSize) prefSize.value = p.fontSize || 'md';
    };

    const readPrefInputs = () => ({
        theme: prefTheme?.value || 'light',
        style: prefStyle?.value || 'standard',
        palette: prefPalette?.value || 'blue',
        background: prefBg?.value || 'solid',
        font: prefFont?.value || 'roboto',
        fontSize: prefSize?.value || 'md'
    });

    if (prefStyle) {
        syncPrefInputs(getUiPrefs());
        [prefTheme, prefStyle, prefPalette, prefBg, prefFont, prefSize].forEach(sel => {
            sel?.addEventListener('change', () => {
                const next = readPrefInputs();
                if (sel === prefStyle && next.style === 'sketchy' && prefFont) {
                    next.font = 'patrick';
                    prefFont.value = 'patrick';
                } else if (sel === prefStyle && (next.style === 'paper' || next.style === 'journal') && prefFont) {
                    next.font = 'lora';
                    prefFont.value = 'lora';
                } else if (sel === prefStyle && next.style === 'terminal' && prefFont) {
                    next.font = 'jetbrains';
                    prefFont.value = 'jetbrains';
                }
                applyUiPrefs(next, { persistRemote: false, silent: true });
            });
        });

        document.getElementById('btnSaveUiPrefs')?.addEventListener('click', async () => {
            const next = readPrefInputs();
            applyUiPrefs(next, { persistRemote: false, silent: true });
            const res = await apiFetch('/S01AuthOtpGoogle/SaveUiPreferences', {
                method: 'POST',
                body: next
            });
            if (res.ok) {
                toast(200, 'Đã lưu cài đặt giao diện', 'Phong cách, bảng màu, loại nền, phông chữ & cỡ chữ đã đồng bộ vào tài khoản.');
            }
        });

        document.getElementById('btnResetUiPrefs')?.addEventListener('click', async () => {
            const def = { theme: 'light', style: 'standard', palette: 'blue', background: 'solid', font: 'roboto', fontSize: 'md' };
            syncPrefInputs(def);
            applyUiPrefs(def, { persistRemote: false, silent: true });
            await apiFetch('/S01AuthOtpGoogle/SaveUiPreferences', { method: 'POST', body: def });
            toast(200, 'Đã khôi phục mặc định', 'Giao diện đã trở về chuẩn sáng BookShare.');
        });
    }

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
                changePassForm.reset();
            }
        });
    }

    const curatorReqForm = document.getElementById('s01CuratorRequestForm');
    if (curatorReqForm) {
        curatorReqForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const organizationName = document.getElementById('curatorOrgName')?.value.trim() || '';
            const reason = document.getElementById('curatorReason')?.value.trim() || '';

            const res = await apiFetch('/S01AuthOtpGoogle/SubmitCuratorRequest', {
                method: 'POST',
                body: { organizationName, reason }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 700);
            }
        });
    }

    // ---------- ĐĂNG NHẬP NHANH BẰNG MÃ QR (THỐNG NHẤT 1 MÁY QUÉT CHUẨN) ----------
    const btnTabQrLogin = document.getElementById('btnTabQrLogin');
    const tabQrPane = document.getElementById('tabQrLogin');
    const isAuthTab = tabQrPane?.getAttribute('data-authenticated') === '1';
    const qrWebBox = document.getElementById('qrWebLoginBox');
    const qrWebTimer = document.getElementById('qrWebLoginTimer');
    const qrWebStatus = document.getElementById('qrWebLoginStatus');
    const btnRefreshWebQr = document.getElementById('btnRefreshWebQr');

    let webQrState = null;
    let webCountdownTimer = null;
    let webPollTimer = null;
    let isCreatingWebQr = false;
    let isClaimingWebQr = false;

    const stopWebQrTimers = () => {
        if (webCountdownTimer) { clearInterval(webCountdownTimer); webCountdownTimer = null; }
        if (webPollTimer) { clearInterval(webPollTimer); webPollTimer = null; }
    };

    const startWebQrSession = async () => {
        if (isAuthTab || isCreatingWebQr || !qrWebBox) return;
        isCreatingWebQr = true;
        isClaimingWebQr = false;
        stopWebQrTimers();

        qrWebBox.innerHTML = '<div class="spinner-border spinner-border-sm text-primary" role="status"></div>';
        if (qrWebStatus) qrWebStatus.innerText = 'Đang khởi tạo mã mới...';

        try {
            const res = await apiFetch('/api/qr-login/create', { method: 'POST', body: {}, button: null });
            if (!res.ok || !res.data?.data) {
                qrWebBox.innerHTML = '<span class="small text-danger">Không thể tạo mã lúc này.</span>';
                return;
            }

            const data = res.data.data;
            webQrState = {
                sessionId: data.sessionId,
                token: data.token,
                remainingSeconds: Number(data.expiresInSeconds) || 120
            };
            await renderQrCode(qrWebBox, data.qrPayload, 220);
            if (qrWebStatus) qrWebStatus.innerText = 'Hãy dùng thiết bị đã đăng nhập để quét mã này...';
            if (qrWebTimer) qrWebTimer.innerText = `Hiệu lực: ${webQrState.remainingSeconds}s`;

            webCountdownTimer = setInterval(() => {
                if (!webQrState) return;
                webQrState.remainingSeconds = Math.max(0, webQrState.remainingSeconds - 1);
                if (qrWebTimer) qrWebTimer.innerText = `Hiệu lực: ${webQrState.remainingSeconds}s`;
                if (webQrState.remainingSeconds <= 0) {
                    stopWebQrTimers();
                    if (qrWebStatus) qrWebStatus.innerText = 'Mã đã hết hạn. Vui lòng bấm "Tạo Mã Mới".';
                }
            }, 1000);

            webPollTimer = setInterval(async () => {
                if (!webQrState || isClaimingWebQr || !tabQrPane?.classList.contains('active')) return;
                try {
                    const statusRes = await fetch(`/api/qr-login/status/${webQrState.sessionId}`, {
                        headers: { Accept: 'application/json' },
                        credentials: 'same-origin'
                    });
                    if (!statusRes.ok) return;
                    const st = await statusRes.json();
                    if (st.status === 'Approved' && !isClaimingWebQr) {
                        isClaimingWebQr = true;
                        stopWebQrTimers();
                        if (qrWebStatus) qrWebStatus.innerText = 'Đã xác nhận! Đang hoàn tất đăng nhập...';
                        const claim = await apiFetch('/api/qr-login/claim', {
                            method: 'POST',
                            body: { sessionId: webQrState.sessionId, token: webQrState.token },
                            button: null
                        });
                        if (claim.ok) {
                            setTimeout(() => { window.location.href = claim.data?.redirectUrl || '/'; }, 500);
                        } else {
                            isClaimingWebQr = false;
                        }
                    } else if (st.status === 'Rejected') {
                        stopWebQrTimers();
                        if (qrWebStatus) qrWebStatus.innerText = 'Thiết bị xác nhận đã từ chối yêu cầu đăng nhập.';
                    } else if (st.status === 'Expired') {
                        stopWebQrTimers();
                        if (qrWebStatus) qrWebStatus.innerText = 'Mã đã hết hạn. Vui lòng tạo mã mới.';
                    }
                } catch {
                    // Bỏ qua lỗi mạng tạm thời giữa chu kỳ thăm dò
                }
            }, 2500);
        } finally {
            isCreatingWebQr = false;
        }
    };

    if (btnTabQrLogin && !isAuthTab) {
        btnTabQrLogin.addEventListener('shown.bs.tab', () => {
            if (!webQrState || webQrState.remainingSeconds <= 5) {
                startWebQrSession();
            }
        });
    }
    btnRefreshWebQr?.addEventListener('click', startWebQrSession);

    // Thống nhất mọi nút quét QR đăng nhập dùng đúng openUniversalScanner() (tự nhận diện cả QRLOGIN lẫn MOBILELINK)
    document.getElementById('btnScanMobileLinkQr')?.addEventListener('click', async () => {
        await openUniversalScanner();
    });

    document.getElementById('btnApproveDesktopQr')?.addEventListener('click', async () => {
        await openUniversalScanner();
    });

    const btnGenMobileLink = document.getElementById('btnGenerateMobileLinkQr');
    const mobileLinkContainer = document.getElementById('mobileLinkQrContainer');
    const mobileLinkBox = document.getElementById('mobileLinkQrBox');
    const mobileLinkTimer = document.getElementById('mobileLinkQrTimer');
    let mobileLinkTimerId = null;

    btnGenMobileLink?.addEventListener('click', async () => {
        if (mobileLinkTimerId) { clearInterval(mobileLinkTimerId); mobileLinkTimerId = null; }
        const res = await apiFetch('/api/qr-login/create-mobile-link', { method: 'POST', body: {} });
        if (!res.ok || !res.data?.data) return;

        const data = res.data.data;
        mobileLinkContainer?.classList.remove('d-none');
        await renderQrCode(mobileLinkBox, data.qrPayload, 200);

        let left = Number(data.expiresInSeconds) || 60;
        if (mobileLinkTimer) mobileLinkTimer.innerText = `Hiệu lực: ${left}s (chỉ dùng 1 lần)`;
        mobileLinkTimerId = setInterval(() => {
            left = Math.max(0, left - 1);
            if (mobileLinkTimer) {
                mobileLinkTimer.innerText = left > 0 ? `Hiệu lực: ${left}s (chỉ dùng 1 lần)` : 'Mã đã hết hạn — bấm nút để tạo mã mới.';
            }
            if (left <= 0 && mobileLinkTimerId) {
                clearInterval(mobileLinkTimerId);
                mobileLinkTimerId = null;
            }
        }, 1000);
    });

    // Tự động mở đúng tab Giao Diện (?tab=appearance) hoặc Đăng Nhập Nhanh (?tab=qr)
    const tabParam = (new URLSearchParams(window.location.search).get('tab') || '').toLowerCase();
    if (tabParam === 'appearance') {
        const tabBtn = document.getElementById('btnTabAppearance');
        if (tabBtn && window.bootstrap?.Tab) window.bootstrap.Tab.getOrCreateInstance(tabBtn).show();
    } else if (tabParam === 'qr') {
        const tabBtn = document.getElementById('btnTabQrLogin');
        if (tabBtn && window.bootstrap?.Tab) {
            window.bootstrap.Tab.getOrCreateInstance(tabBtn).show();
            if (!isAuthTab) startWebQrSession();
        }
    }
});
