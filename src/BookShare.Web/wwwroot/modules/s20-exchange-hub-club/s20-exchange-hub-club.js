import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. User đăng ký ký gửi sách P2P tại Trạm Hub
    document.getElementById('formRegisterDropoff')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('dropoffCopyId')?.value || '0', 10);
        const hubId = parseInt(document.getElementById('dropoffHubId')?.value || '3', 10);

        const res = await apiFetch('/S20ExchangeHubClub/RegisterDropoff', {
            method: 'POST',
            body: { bookCopyId, hubId }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 2. Curator xác nhận kiểm định sách thật ngoài đời ([Authorize(Roles = "Curator,Admin")])
    document.getElementById('formCuratorVerify')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookCopyId = parseInt(document.getElementById('verifyCopyId')?.value || '0', 10);
        const verifiedCondition = document.getElementById('verifyCondition')?.value || 'Good';
        const shelfLocation = document.getElementById('verifyShelfLocation')?.value || 'Trạm CLB-K1';

        const res = await apiFetch('/S20ExchangeHubClub/VerifyPhysicalBook', {
            method: 'POST',
            body: {
                bookCopyId,
                hubId: 3,
                verifiedCondition,
                shelfLocation
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 3. Curator xác nhận ký gửi / bàn giao / thu hồi trung gian tại Hub
    document.querySelectorAll('.js-hub-action').forEach(btn => {
        btn.addEventListener('click', async () => {
            const loanRequestId = parseInt(btn.getAttribute('data-req-id') || '0', 10);
            const actionType = btn.getAttribute('data-action') || 'CheckInDropoff';

            const res = await apiFetch('/S20ExchangeHubClub/ProcessHubHandover', {
                method: 'POST',
                body: { loanRequestId, actionType }
            });

                        if (res.ok) {
                            setTimeout(() => window.location.reload(), 650);
                        }
                    });
                });

                document.querySelectorAll('.js-s20-edit-hub').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const hubId = parseInt(btn.getAttribute('data-hub-id') || '0', 10);
                        const curName = btn.getAttribute('data-hub-name') || '';
                        const curAddress = btn.getAttribute('data-hub-address') || '';
                        const curHours = btn.getAttribute('data-hub-hours') || '08:00 - 18:00';
                        const curPhone = btn.getAttribute('data-hub-phone') || '';
                        if (!hubId || typeof window.Swal === 'undefined') return;

                        const { value: formValues } = await window.Swal.fire({
                            title: `Cập nhật Điểm trạm CLB / Hub #${hubId}`,
                            html:
                                `<div class="text-start small">` +
                                `<label class="fw-bold mb-1">Tên Điểm trạm / Thư viện *</label>` +
                                `<input id="swalHubName" class="form-control form-control-sm mb-2" value="${curName.replace(/"/g, '&quot;')}">` +
                                `<label class="fw-bold mb-1">Địa chỉ hoạt động *</label>` +
                                `<input id="swalHubAddr" class="form-control form-control-sm mb-2" value="${curAddress.replace(/"/g, '&quot;')}">` +
                                `<div class="row g-2">` +
                                `<div class="col-6"><label class="fw-bold mb-1">Giờ mở cửa</label><input id="swalHubHours" class="form-control form-control-sm" value="${curHours.replace(/"/g, '&quot;')}"></div>` +
                                `<div class="col-6"><label class="fw-bold mb-1">Hotline liên hệ</label><input id="swalHubPhone" class="form-control form-control-sm" value="${curPhone.replace(/"/g, '&quot;')}"></div>` +
                                `</div></div>`,
                            focusConfirm: false,
                            showCancelButton: true,
                            confirmButtonText: 'Lưu cập nhật',
                            cancelButtonText: 'Hủy',
                            confirmButtonColor: '#2563eb',
                            preConfirm: () => {
                                const name = document.getElementById('swalHubName')?.value.trim() || '';
                                const address = document.getElementById('swalHubAddr')?.value.trim() || '';
                                const openHours = document.getElementById('swalHubHours')?.value.trim() || '08:00 - 18:00';
                                const phone = document.getElementById('swalHubPhone')?.value.trim() || '';
                                if (!name || !address) {
                                    window.Swal.showValidationMessage('Vui lòng nhập đầy đủ Tên trạm và Địa chỉ');
                                    return null;
                                }
                                return { hubId, name, address, openHours, phone };
                            }
                        });

                        if (!formValues) return;

                        const res = await apiFetch('/S20ExchangeHubClub/UpdateHubStation', {
                            method: 'POST',
                            body: formValues,
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });
            });
