import { apiFetch } from '../../js/core-api.js';

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
            const copyTargetStatus = document.querySelector(`.js-copy-status[data-id="${disputeId}"]`)?.value || 'Damaged';
            const resolutionNote = document.querySelector(`.js-resolution-note[data-id="${disputeId}"]`)?.value || '';

            const res = await apiFetch('/S19DisputeResolution/ResolveDispute', {
                method: 'POST',
                body: {
                    disputeId,
                    decisionStatus,
                    trustScorePenalty,
                    depositDeductionAmount,
                    copyTargetStatus,
                    resolutionNote
                }
            });

            if (res.ok) {
                setTimeout(() => window.location.reload(), 750);
            }
        });
    });
});
