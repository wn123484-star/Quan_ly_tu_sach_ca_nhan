// =========================================================================
// TỆP: assets/js/settings.js
// Mục đích: Xử lý trang Tùy chỉnh (Giao diện, Hình nền, EmailJS, Cấu hình AI)
// =========================================================================

document.addEventListener('DOMContentLoaded', () => {
    const themeSelector = document.getElementById('themeSelector');
    const fontSelector = document.getElementById('fontSelector');
    const iconPackSelector = document.getElementById('iconPackSelector');
    const bgUpload = document.getElementById('bgUpload');
    const btnResetVisuals = document.getElementById('btnResetVisuals');
    const emailConfigForm = document.getElementById('emailConfigForm');
    const btnClearData = document.getElementById('btnClearData');
    const btnRestoreSample = document.getElementById('btnRestoreSample');

    // Các phần tử thuộc khu vực cấu hình AI
    const aiConfigForm = document.getElementById('aiConfigForm');
    const apiKeyInput = document.getElementById('gemini-api-key');
    const btnToggleKey = document.getElementById('btnToggleKeyVisibility');
    const btnFetchModels = document.getElementById('btnFetchModels');
    const modelSelect = document.getElementById('aiModelSelect');
    const modelFetchStatus = document.getElementById('modelFetchStatus');
    const tempInput = document.getElementById('aiTemperature');
    const tempLabel = document.getElementById('tempValueLabel');
    const maxTokensInput = document.getElementById('aiMaxTokens');
    const systemPromptInput = document.getElementById('aiSystemPrompt');

    // 1. Đồng bộ giá trị đang lưu lên các hộp chọn phong cách
    if (themeSelector) themeSelector.value = localStorage.getItem('app_theme') || 'default';
    if (fontSelector) fontSelector.value = localStorage.getItem('app_font') || 'priority-regular';
    const currentIconPack = localStorage.getItem('app_icon_pack') || 'bo1';
    if (iconPackSelector) {
        iconPackSelector.value = (currentIconPack === 'bo3') ? 'bo1' : currentIconPack;
    }

    // 2. Nạp cấu hình EmailJS đã lưu (hoặc điền sẵn thông số mặc định để tiện test)
    const emailConfig = typeof getEffectiveEmailConfig === 'function'
        ? getEffectiveEmailConfig()
        : JSON.parse(localStorage.getItem('emailConfig') || '{}');

    if (document.getElementById('targetEmail')) {
        document.getElementById('targetEmail').value = emailConfig.targetEmail || '';
        document.getElementById('emailPublicKey').value = emailConfig.publicKey || '';
        document.getElementById('emailServiceId').value = emailConfig.serviceId || '';
        document.getElementById('emailTemplateId').value = emailConfig.templateId || '';
    }

    // 3. [MÃ: GD-20] Nạp cấu hình AI Gemini từ localStorage khi mở trang
    const populateSavedModels = (selectedModel) => {
        try {
            const cachedModels = JSON.parse(localStorage.getItem('aiAvailableModels') || '[]');
            if (cachedModels.length > 0 && modelSelect) {
                modelSelect.innerHTML = '';
                cachedModels.forEach(m => {
                    const opt = document.createElement('option');
                    opt.value = m.name;
                    opt.textContent = m.displayName || m.name;
                    modelSelect.appendChild(opt);
                });
            }
            if (selectedModel && modelSelect) {
                const exists = Array.from(modelSelect.options).some(o => o.value === selectedModel);
                if (!exists) {
                    const customOpt = document.createElement('option');
                    customOpt.value = selectedModel;
                    customOpt.textContent = selectedModel;
                    modelSelect.appendChild(customOpt);
                }
                modelSelect.value = selectedModel;
            }
        } catch (e) {
            console.warn('Không thể đọc danh sách model đã lưu:', e);
        }
    };

    if (typeof getAIConfig === 'function' && aiConfigForm) {
        const aiCfg = getAIConfig();
        apiKeyInput.value = aiCfg.apiKey || '';
        tempInput.value = aiCfg.temperature ?? 0.7;
        tempLabel.innerText = aiCfg.temperature ?? 0.7;
        maxTokensInput.value = aiCfg.maxTokens ?? 800;
        systemPromptInput.value = aiCfg.systemPrompt || '';
        populateSavedModels(aiCfg.model);
    }

    // Hiện / ẩn mật khẩu API Key
    if (btnToggleKey && apiKeyInput) {
        btnToggleKey.addEventListener('click', () => {
            const isPass = apiKeyInput.type === 'password';
            apiKeyInput.type = isPass ? 'text' : 'password';
            btnToggleKey.innerHTML = isPass ? '<i class="bi bi-eye-slash"></i>' : '<i class="bi bi-eye"></i>';
        });
    }

    // Cập nhật nhãn hiển thị khi kéo thanh trượt Temperature
    if (tempInput && tempLabel) {
        tempInput.addEventListener('input', (e) => {
            tempLabel.innerText = e.target.value;
        });
    }

    // [MÃ: NV-02] Sự kiện bấm nút "1. Lấy DS Mô hình" từ Google Gemini API
    if (btnFetchModels) {
        btnFetchModels.addEventListener('click', async () => {
            const key = apiKeyInput.value.trim();
            if (!key) {
                hienThiThongBaoNgoaiLeHTML(400, 'Thiếu Gemini API Key (Mã 400)', 'Vui lòng dán API Key vào ô trước khi lấy danh sách mô hình.');
                Swal.fire('Thiếu khóa API (Mã 400)', 'Vui lòng nhập Gemini API Key trước.', 'warning');
                return;
            }

            const originalBtnHTML = btnFetchModels.innerHTML;
            btnFetchModels.disabled = true;
            btnFetchModels.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Đang tải...';
            modelFetchStatus.innerText = 'Đang kết nối tới Google Gemini để lấy danh sách mô hình...';
            modelFetchStatus.className = 'form-text small text-primary';

            try {
                const models = await fetchAvailableGeminiModels(key);
                modelSelect.innerHTML = '';
                models.forEach(m => {
                    const option = document.createElement('option');
                    option.value = m.name;
                    option.textContent = m.displayName ? `${m.displayName} (${m.name})` : m.name;
                    modelSelect.appendChild(option);
                });

                // Lưu luôn API Key và model đầu tiên sau khi lấy thành công
                if (models.length > 0) {
                    saveAIConfig({ apiKey: key, model: models[0].name });
                }

                modelFetchStatus.innerText = `Đã nạp thành công ${models.length} mô hình khả dụng. Hãy chọn mô hình và bấm Lưu.`;
                modelFetchStatus.className = 'form-text small text-success fw-semibold';
                hienThiThongBaoNgoaiLeHTML(200, `Tải thành công ${models.length} mô hình AI (Mã 200)`, 'Danh sách mô hình Gemini khả dụng đã được cập nhật vào hộp chọn.');
            } catch (err) {
                modelFetchStatus.innerText = `Lỗi: ${err.message}`;
                modelFetchStatus.className = 'form-text small text-danger';
                hienThiThongBaoNgoaiLeHTML(400, 'Không thể lấy danh sách mô hình (Mã 400)', err.message);
                Swal.fire('Lỗi xác thực API Key', err.message, 'error');
            } finally {
                btnFetchModels.disabled = false;
                btnFetchModels.innerHTML = originalBtnHTML;
            }
        });
    }

    // Sự kiện Lưu cấu hình AI vào localStorage
    if (aiConfigForm) {
        aiConfigForm.addEventListener('submit', (e) => {
            e.preventDefault();
            const newCfg = {
                apiKey: apiKeyInput.value.trim(),
                model: modelSelect.value || 'models/gemini-1.5-flash',
                temperature: parseFloat(tempInput.value || 0.7),
                maxTokens: parseInt(maxTokensInput.value || 800, 10),
                systemPrompt: systemPromptInput.value.trim()
            };
            saveAIConfig(newCfg);
            ghiLogF12('CẤU HÌNH AI', `Đã lưu Model: ${newCfg.model}, Temp: ${newCfg.temperature}`);
            hienThiThongBaoNgoaiLeHTML(200, 'Lưu cấu hình Gemini AI thành công (Mã 200)', `Đang sử dụng mô hình ${newCfg.model} với Temperature ${newCfg.temperature}.`);
            Swal.fire({
                title: 'Đã lưu cấu hình AI (Mã 200)',
                text: 'Bạn có thể tạo ghi chú sách hoặc bấm vào nút Trợ lý AI ở góc phải màn hình để trò chuyện ngay.',
                icon: 'success',
                timer: 2000,
                showConfirmButton: false
            });
        });
    }

    // 4. Sự kiện đổi màu giao diện, kiểu chữ, bộ biểu tượng
    if (themeSelector) {
        themeSelector.addEventListener('change', (e) => {
            const val = e.target.value;
            applyTheme(val);
            hienThiThongBaoNgoaiLeHTML(200, 'Đổi giao diện thành công (Mã 200)', `Đã áp dụng phong cách giao diện: ${val}.`);
        });
    }

    if (fontSelector) {
        fontSelector.addEventListener('change', (e) => {
            const val = e.target.value;
            applyFont(val);
            hienThiThongBaoNgoaiLeHTML(200, 'Đổi kiểu chữ thành công (Mã 200)', `Đã chuyển sang kiểu chữ: ${val}.`);
        });
    }

    if (iconPackSelector) {
        iconPackSelector.addEventListener('change', (e) => {
            const val = e.target.value;
            applyIconPack(val);
            hienThiThongBaoNgoaiLeHTML(200, 'Đổi bộ biểu tượng thành công (Mã 200)', `Đã đồng bộ bộ biểu tượng trang trí: ${val}.`);
        });
    }

    // 5. Xử lý nén ảnh nền tải lên qua thẻ Canvas để không tràn bộ nhớ 5MB của LocalStorage
    if (bgUpload) {
        bgUpload.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;

            if (!file.type.startsWith('image/')) {
                hienThiThongBaoNgoaiLeHTML(400, 'Tệp hình nền không hợp lệ (Mã 400)', 'Vui lòng chọn tệp định dạng hình ảnh (.jpg, .png, .webp).');
                Swal.fire('Sai định dạng (Mã 400)', 'Vui lòng chọn tệp hình ảnh.', 'warning');
                return;
            }

            const reader = new FileReader();
            reader.onload = function(event) {
                const img = new Image();
                img.onload = function() {
                    const canvas = document.createElement('canvas');
                    const MAX_WIDTH = 1280;
                    let width = img.width;
                    let height = img.height;

                    if (width > MAX_WIDTH) {
                        height = Math.round((height * MAX_WIDTH) / width);
                        width = MAX_WIDTH;
                    }

                    canvas.width = width;
                    canvas.height = height;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, width, height);

                    const compressedDataUrl = canvas.toDataURL('image/jpeg', 0.72);
                    try {
                        localStorage.setItem('app_bg', compressedDataUrl);
                        applySavedBackground();
                        hienThiThongBaoNgoaiLeHTML(200, 'Cập nhật hình nền thành công (Mã 200)', 'Ảnh đã được tự động thu gọn dung lượng và lưu vào trình duyệt.');
                        Swal.fire({ title: 'Đã đổi hình nền (Mã 200)', icon: 'success', timer: 1200, showConfirmButton: false });
                    } catch (err) {
                        hienThiThongBaoNgoaiLeHTML(500, 'Bộ nhớ trình duyệt quá tải (Mã 500)', 'Ảnh vẫn quá lớn so với giới hạn LocalStorage.');
                        Swal.fire('Lỗi bộ nhớ (Mã 500)', 'Không thể lưu ảnh này, vui lòng chọn ảnh nhẹ hơn.', 'error');
                    }
                };
                img.src = event.target.result;
            };
            reader.readAsDataURL(file);
        });
    }

    if (btnResetVisuals) {
        btnResetVisuals.addEventListener('click', () => {
            localStorage.removeItem('app_bg');
            applySavedBackground();
            if (bgUpload) bgUpload.value = '';
            hienThiThongBaoNgoaiLeHTML(200, 'Đã khôi phục nền mặc định (Mã 200)', 'Đã gỡ hình nền cá nhân khỏi bộ nhớ trình duyệt.');
        });
    }

    // 6. Lưu cài đặt hộp thư EmailJS
    if (emailConfigForm) {
        emailConfigForm.addEventListener('submit', (e) => {
            e.preventDefault();
            const cfg = {
                targetEmail: document.getElementById('targetEmail').value.trim(),
                publicKey: document.getElementById('emailPublicKey').value.trim(),
                serviceId: document.getElementById('emailServiceId').value.trim(),
                templateId: document.getElementById('emailTemplateId').value.trim()
            };
            localStorage.setItem('emailConfig', JSON.stringify(cfg));
            hienThiThongBaoNgoaiLeHTML(200, 'Lưu cấu hình EmailJS thành công (Mã 200)', `Sẵn sàng gửi ghi chú AI tới hộp thư ${cfg.targetEmail}.`);
            Swal.fire({ title: 'Đã lưu hộp thư (Mã 200)', icon: 'success', timer: 1500, showConfirmButton: false });
        });
    }

    // 7. Các nút mô phỏng mã trạng thái kết nối (200, 300, 400, 404, 500)
    document.querySelectorAll('[data-test-code]').forEach(btn => {
        btn.addEventListener('click', () => {
            const code = parseInt(btn.getAttribute('data-test-code'), 10);
            const samples = {
                200: ['Mô phỏng Mã 200: Kết nối thành công', 'Máy chủ Gemini AI và bộ nhớ trình duyệt phản hồi bình thường.'],
                300: ['Mô phỏng Mã 300: Chuyển hướng nguồn dữ liệu', 'Tệp cấu hình chính vắng mặt, hệ thống tự động dùng cấu hình dự phòng.'],
                400: ['Mô phỏng Mã 400: Dữ liệu đầu vào chưa hợp lệ', 'Người dùng bỏ trống trường bắt buộc hoặc nhập sai định dạng API Key.'],
                404: ['Mô phỏng Mã 404: Không tìm thấy tài nguyên', 'Không tìm thấy cuốn sách hoặc đường dẫn ảnh bìa không tồn tại.'],
                500: ['Mô phỏng Mã 500: Sự cố máy chủ hoặc mất mạng', 'Không thể kết nối tới máy chủ EmailJS / Google Gemini lúc này.']
            };
            const [title, detail] = samples[code] || samples[200];
            hienThiThongBaoNgoaiLeHTML(code, title, detail);
        });
    });

    // 8. Nạp lại 12 sách mẫu hoặc Xóa trắng tủ sách
    if (btnRestoreSample) {
        btnRestoreSample.addEventListener('click', () => {
            Swal.fire({
                title: 'Nạp lại sách mẫu ban đầu?',
                text: 'Danh sách hiện tại sẽ được thay bằng bộ sách mẫu gốc của ứng dụng.',
                icon: 'question',
                showCancelButton: true,
                confirmButtonText: 'Đồng ý nạp lại',
                cancelButtonText: 'Hủy'
            }).then(async (res) => {
                if (res.isConfirmed) {
                    localStorage.removeItem('books');
                    await initSampleData();
                    hienThiThongBaoNgoaiLeHTML(200, 'Đã khôi phục sách mẫu (Mã 200)', 'Dữ liệu mẫu ban đầu đã được nạp lại vào tủ sách.');
                    Swal.fire('Hoàn tất (Mã 200)', 'Đã khôi phục danh sách sách mẫu.', 'success');
                }
            });
        });
    }

    if (btnClearData) {
        btnClearData.addEventListener('click', () => {
            Swal.fire({
                title: 'Làm trống toàn bộ tủ sách?',
                text: 'Toàn bộ sách hiện có sẽ bị xóa khỏi trình duyệt.',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#dc3545',
                confirmButtonText: 'Xóa toàn bộ',
                cancelButtonText: 'Hủy'
            }).then((res) => {
                if (res.isConfirmed) {
                    saveBooks([]);
                    hienThiThongBaoNgoaiLeHTML(200, 'Đã làm trống tủ sách (Mã 200)', 'Hiện có 0 cuốn sách trong bộ nhớ trình duyệt.');
                    Swal.fire('Đã xóa sạch (Mã 200)', 'Tủ sách của bạn hiện đang trống.', 'success');
                }
            });
        });
    }
});