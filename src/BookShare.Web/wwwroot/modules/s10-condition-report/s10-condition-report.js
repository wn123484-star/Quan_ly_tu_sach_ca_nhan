/**
 * [S10] FRONTEND MODULE (s10-condition-report.js)
 * Tài liệu ánh xạ dịch vụ & phân quyền: docs/SERVICE_CODE_MAP.md
 */
/**
 * MODULE S10: CONDITION REPORT (s10-condition-report.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S10
 */
import { apiFetch, showSystemStatus, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const uploadForm = document.getElementById('s10UploadReportForm');
    const loanSelect = document.getElementById('s10LoanId');
    const reportTypeSelect = document.getElementById('s10ReportType');

    // 1. Chọn nhanh hợp đồng cần bổ sung ảnh trả
    document.querySelectorAll('.js-PrefillReturnLoan').forEach(btn => {
        btn.addEventListener('click', () => {
            const loanId = btn.getAttribute('data-loan-id') || '';
            if (loanSelect) loanSelect.value = loanId;
            if (reportTypeSelect) reportTypeSelect.value = 'Return';
            loanSelect?.focus();
        });
    });

    // 2. Upload biên bản ảnh (Multipart FormData kèm CSRF Token)
    if (uploadForm) {
        uploadForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const csrfToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content') || '';
            const formData = new FormData(uploadForm);

            try {
                const response = await fetch('/S10ConditionReport/UploadReport', {
                    method: 'POST',
                    headers: {
                        'RequestVerificationToken': csrfToken
                    },
                    body: formData
                });

                const data = await response.json();
                if (response.ok) {
                    window.location.reload();
                } else {
                                        showSystemStatus(response.status, 'Lỗi lưu biên bản kiểm định', data.message || 'Dữ liệu không hợp lệ.');
                                    }
                                } catch (err) {
                                    showSystemStatus(500, 'Lỗi kết nối máy chủ (Mã 500)', err.message);
                                }
                            });
                        }

                        document.querySelectorAll('.js-s10-edit-report').forEach(btn => {
                            btn.addEventListener('click', async () => {
                                const reportId = parseInt(btn.getAttribute('data-report-id') || '0', 10);
                                const curGrade = btn.getAttribute('data-grade') || 'Good';
                                const curNote = btn.getAttribute('data-note') || '';
                                if (!reportId || typeof window.Swal === 'undefined') return;

                                const { value: formValues } = await window.Swal.fire({
                                    title: `Cập nhật Biên bản Kiểm định #${reportId}`,
                                    html:
                                        `<div class="text-start small">` +
                                        `<label class="fw-bold mb-1">Phân loại độ mới (ConditionGrade)</label>` +
                                        `<select id="swalCondGrade" class="form-select form-select-sm mb-2">` +
                                        `<option value="New" ${curGrade === 'New' ? 'selected' : ''}>Mới nguyên (New)</option>` +
                                        `<option value="Good" ${curGrade === 'Good' ? 'selected' : ''}>Tốt / Nguyên vẹn (Good)</option>` +
                                        `<option value="Fair" ${curGrade === 'Fair' ? 'selected' : ''}>Khá / Quăn góc nhẹ (Fair)</option>` +
                                        `<option value="Worn" ${curGrade === 'Worn' ? 'selected' : ''}>Cũ / Sờn gáy (Worn)</option>` +
                                        `<option value="Damaged" ${curGrade === 'Damaged' ? 'selected' : ''}>Hư hỏng / Rách trang (Damaged)</option>` +
                                        `</select>` +
                                        `<label class="fw-bold mb-1">Ghi chú kiểm định chi tiết</label>` +
                                        `<textarea id="swalCondNote" class="form-control form-control-sm" rows="3">${curNote}</textarea>` +
                                        `</div>`,
                                    focusConfirm: false,
                                    showCancelButton: true,
                                    confirmButtonText: 'Lưu cập nhật',
                                    cancelButtonText: 'Hủy',
                                    confirmButtonColor: '#2563eb',
                                    preConfirm: () => {
                                        const conditionGrade = document.getElementById('swalCondGrade')?.value || 'Good';
                                        const note = document.getElementById('swalCondNote')?.value.trim() || '';
                                        return { reportId, conditionGrade, note };
                                    }
                                });

                                if (!formValues) return;

                                const res = await apiFetch('/S10ConditionReport/UpdateReport', {
                                    method: 'POST',
                                    body: formValues,
                                    button: btn
                                });

                                if (res.ok) {
                                    window.location.reload();
                                }
                            });
                        });

                        document.querySelectorAll('.js-s10-delete-report').forEach(btn => {
                            btn.addEventListener('click', async () => {
                                const reportId = parseInt(btn.getAttribute('data-report-id') || '0', 10);
                                if (!reportId) return;

                                const ok = await confirmDialog({
                                    title: `Xóa biên bản kiểm định #${reportId}?`,
                                    text: 'Biên bản ảnh này sẽ được xóa khỏi lịch sử kiểm định hợp đồng.',
                                    confirmText: 'Xóa biên bản'
                                });
                                if (!ok) return;

                                const res = await apiFetch('/S10ConditionReport/DeleteReport', {
                                    method: 'POST',
                                    body: { reportId },
                                    button: btn
                                });

                                if (res.ok) {
                                    window.location.reload();
                                }
                            });
                        });
                    });
