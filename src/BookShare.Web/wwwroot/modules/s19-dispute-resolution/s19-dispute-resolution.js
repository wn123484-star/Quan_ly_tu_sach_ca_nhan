/**
 * [S19] FRONTEND MODULE (s19-dispute-resolution.js)
 * Tài liệu ánh xạ dịch vụ & phân quyền: docs/SERVICE_CODE_MAP.md
 */
import { apiFetch, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    // 1. User tạo hồ sơ khiếu nại mới
    document.getElementById('formCreateDispute')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const loanId = parseInt(document.getElementById('disputeLoanId')?.value || '0', 10);
        const reason = document.getElementById('disputeReason')?.value.trim() || '';
        const evidenceImageUrl = document.getElementById('disputeEvidenceUrl')?.value.trim() || '';

        const res = await apiFetch('/S19DisputeResolution/CreateDispute', {
            method: 'POST',
            body: {
                loanId,
                reason,
                evidenceImageUrl
            }
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 700);
        }
    });

    // 2. Admin ban hành phán quyết ([Authorize(Roles = "Admin")])
    document.querySelectorAll('.js-btn-resolve').forEach(btn => {
        btn.addEventListener('click', async () => {
            const disputeId = parseInt(btn.getAttribute('data-id') || '0', 10);
            const decisionStatus = document.querySelector(`.js-decision-status[data-id="${disputeId}"]`)?.value || 'Resolved';
            const trustScorePenalty = parseInt(document.querySelector(`.js-penalty-score[data-id="${disputeId}"]`)?.value || '10', 10);
            const depositDeductionAmount = parseFloat(document.querySelector(`.js-deduct-deposit[data-id="${disputeId}"]`)?.value || '0');
            const compensationAmount = parseFloat(document.querySelector(`.js-compensation-amount[data-id="${disputeId}"]`)?.value || '0');
            const copyTargetStatus = document.querySelector(`.js-copy-status[data-id="${disputeId}"]`)?.value || 'Damaged';
            const resolutionNote = document.querySelector(`.js-resolution-note[data-id="${disputeId}"]`)?.value || '';

            const res = await apiFetch('/S19DisputeResolution/ResolveDispute', {
                method: 'POST',
                body: {
                    disputeId,
                    decisionStatus,
                    trustScorePenalty,
                    depositDeductionAmount,
                    compensationAmount,
                    copyTargetStatus,
                    resolutionNote
                }
            });

                        if (res.ok) {
                            setTimeout(() => window.location.reload(), 750);
                        }
                    });
                });

                document.querySelectorAll('.js-s19-edit-dispute').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const disputeId = parseInt(btn.getAttribute('data-id') || '0', 10);
                        const curReason = btn.getAttribute('data-reason') || '';
                        if (!disputeId || typeof window.Swal === 'undefined') return;

                        const { value: formValues } = await window.Swal.fire({
                            title: `Cập nhật Hồ sơ Khiếu nại #DSP-${String(disputeId).padStart(4, '0')}`,
                            html:
                                `<div class="text-start small">` +
                                `<label class="fw-bold mb-1">Nội dung lý do khiếu nại *</label>` +
                                `<textarea id="swalDisputeReason" class="form-control form-control-sm" rows="3">${curReason}</textarea>` +
                                `</div>`,
                            focusConfirm: false,
                            showCancelButton: true,
                            confirmButtonText: 'Lưu thay đổi',
                            cancelButtonText: 'Hủy',
                            confirmButtonColor: '#2563eb',
                            preConfirm: () => {
                                const reason = document.getElementById('swalDisputeReason')?.value.trim() || '';
                                if (!reason) {
                                    window.Swal.showValidationMessage('Vui lòng nhập lý do khiếu nại');
                                    return null;
                                }
                                return { disputeId, reason };
                            }
                        });

                        if (!formValues) return;

                        const res = await apiFetch('/S19DisputeResolution/UpdateDispute', {
                            method: 'POST',
                            body: formValues,
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });

                document.querySelectorAll('.js-s19-delete-dispute').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const disputeId = parseInt(btn.getAttribute('data-id') || '0', 10);
                        if (!disputeId) return;

                        const ok = await confirmDialog({
                            title: `Rút / Xóa hồ sơ khiếu nại #DSP-${String(disputeId).padStart(4, '0')}?`,
                            text: 'Hồ sơ tranh chấp này sẽ được rút và xóa khỏi danh sách chờ phân xử.',
                            confirmText: 'Xác nhận rút đơn'
                        });
                        if (!ok) return;

                        const res = await apiFetch('/S19DisputeResolution/DeleteDispute', {
                            method: 'POST',
                            body: { disputeId },
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });
            });
