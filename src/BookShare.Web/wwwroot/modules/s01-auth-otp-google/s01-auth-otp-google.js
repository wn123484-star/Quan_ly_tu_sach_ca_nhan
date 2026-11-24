/**
 * XÁC THỰC, ĐĂNG NHẬP QR & HỒ SƠ CÁ NHÂN (s01-auth-otp-google.js)
 */
import { apiFetch, showSystemStatus, getRecaptchaToken, renderQrCode, openBarcodeScanner, openUniversalScanner, toast, confirmDialog, getUiPrefs, applyUiPrefs } from '../../js/core-api.js';

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

    const updateProfileForm = document.getElementById('s01UpdateProfileForm');
    if (updateProfileForm) {
        updateProfileForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const fullName = document.getElementById('profFullName')?.value.trim() || '';
            const email = document.getElementById('profEmail')?.value.trim() || '';
            const phoneNumber = document.getElementById('profPhone')?.value.trim() || '';
            const address = document.getElementById('profAddress')?.value.trim() || '';
            const avatarUrl = document.getElementById('profAvatarUrl')?.value.trim() || '';

            const res = await apiFetch('/S01AuthOtpGoogle/UpdateProfile', {
                method: 'POST',
                body: { fullName, email, phoneNumber, address, avatarUrl }
            });

            if (res.ok) {
                window.location.reload();
            }
        });
    }

    // ---------- TAB CÀI ĐẶT GIAO DIỆN, PHONG CÁCH, PHÔNG CHỮ & CỠ CHỮ ----------
    const prefTheme = document.getElementById('prefThemeMode');
    const prefStyle = document.getElementById('prefUiStyle');
    const prefPalette = document.getElementById('prefPalette');
    const prefFont = document.getElementById('prefFontFamily');
    const prefSize = document.getElementById('prefFontSize');

    const syncPrefInputs = (p) => {
        if (prefTheme) prefTheme.value = p.theme || 'light';
        if (prefStyle) prefStyle.value = p.style || 'standard';
        if (prefPalette) prefPalette.value = p.palette || 'blue';
        if (prefFont) prefFont.value = p.font || 'roboto';
        if (prefSize) prefSize.value = p.fontSize || 'md';
    };

    const readPrefInputs = () => ({
        theme: prefTheme?.value || 'light',
        style: prefStyle?.value || 'standard',
        palette: prefPalette?.value || 'blue',
        font: prefFont?.value || 'roboto',
        fontSize: prefSize?.value || 'md'
    });

    if (prefStyle) {
        syncPrefInputs(getUiPrefs());
        [prefTheme, prefStyle, prefPalette, prefFont, prefSize].forEach(sel => {
            sel?.addEventListener('change', () => {
                const next = readPrefInputs();
                if (sel === prefStyle && next.style === 'sketchy' && prefFont) {
                    next.font = 'patrick';
                    prefFont.value = 'patrick';
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
                toast(200, 'Đã lưu cài đặt giao diện', 'Phong cách, màu sắc, phông chữ & cỡ chữ đã đồng bộ vào tài khoản.');
            }
        });

        document.getElementById('btnResetUiPrefs')?.addEventListener('click', async () => {
            const def = { theme: 'light', style: 'standard', palette: 'blue', font: 'roboto', fontSize: 'md' };
            syncPrefInputs(def);
            applyUiPrefs(def, { persistRemote: false, silent: true });
            await apiFetch('/S01AuthOtpGoogle/SaveUiPreferences', { method: 'POST', body: def });
            toast(200, 'Đã khôi phục mặc định', 'Giao diện đã trở về chuẩn sáng BookShare.');
        });
    }

    // Tự động mở đúng tab Giao Diện khi điều hướng từ /S01AuthOtpGoogle?tab=appearance
    if (new URLSearchParams(window.location.search).get('tab') === 'appearance') {
        const tabBtn = document.getElementById('btnTabAppearance');
        if (tabBtn && window.bootstrap?.Tab) {
            window.bootstrap.Tab.getOrCreateInstance(tabBtn).show();
        }
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

    // ---------- ĐĂNG NHẬP BẰNG MÃ QR 2 CHIỀU ----------
    const btnTabQrLogin = document.getElementById('btnTabQrLogin');
    const qrWebBox = document.getElementById('qrWebLoginBox');
    const qrWebTimer = document.getElementById('qrWebLoginTimer');
    const qrWebStatus = document.getElementById('qrWebLoginStatus');
    const btnRefreshWebQr = document.getElementById('btnRefreshWebQr');

    let webQrState = null; // { sessionId, token, remainingSeconds }
    let webCountdownTimer = null;
    let webPollTimer = null;
    let isCreatingWebQr = false;
    let isClaimingWebQr = false;

    const stopWebQrTimers = () => {
        if (webCountdownTimer) { clearInterval(webCountdownTimer); webCountdownTimer = null; }
        if (webPollTimer) { clearInterval(webPollTimer); webPollTimer = null; }
    };

    const startWebQrSession = async () => {
        if (isCreatingWebQr || !qrWebBox) return;
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
                const pane = document.getElementById('tabQrLogin');
                if (!webQrState || isClaimingWebQr || !pane?.classList.contains('active')) return;
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

    if (btnTabQrLogin) {
        btnTabQrLogin.addEventListener('shown.bs.tab', () => {
            if (!webQrState || webQrState.remainingSeconds <= 5) {
                startWebQrSession();
            }
        });
    }
    btnRefreshWebQr?.addEventListener('click', startWebQrSession);

    document.getElementById('btnScanMobileLinkQr')?.addEventListener('click', async () => {
        const scanned = await openBarcodeScanner({
            title: 'Quét Mã Đăng Nhập Từ Thiết Bị Đã Đăng Nhập',
            hint: 'Quét mã hiển thị trên màn hình Hồ Sơ của thiết bị kia hoặc nhập chuỗi liên kết'
        });
        if (!scanned) return;
        const res = await apiFetch('/api/qr-login/mobile-claim', {
            method: 'POST',
            body: { qrPayload: scanned.trim() }
        });
        if (res.ok) {
            setTimeout(() => { window.location.href = res.data?.redirectUrl || '/'; }, 600);
        }
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
        await renderQrCode(mobileLinkBox, data.qrPayload, 180);

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
});
