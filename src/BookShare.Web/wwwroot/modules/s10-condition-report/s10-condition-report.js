/**
 * MODULE S10: CONDITION REPORT (s10-condition-report.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S10
 */
import { showSystemStatus } from '../../js/core-api.js';

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
                    showSystemStatus(response.status, 'Lỗi lưu biên bản ảnh (S10)', data.message || 'Dữ liệu không hợp lệ.');
                }
            } catch (err) {
                showSystemStatus(500, 'Lỗi kết nối máy chủ (Mã 500)', err.message);
            }
        });
    }
});
