/**
 * [S15] FRONTEND MODULE (s15-trust-score-review.js)
 * Tài liệu ánh xạ dịch vụ & phân quyền: docs/SERVICE_CODE_MAP.md
 */
import { apiFetch, showSystemStatus, confirmDialog } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const targetTypeSelect = document.getElementById('reviewTargetType');
    const userTargetGroup = document.getElementById('userTargetGroup');
    const bookTargetGroup = document.getElementById('bookTargetGroup');
    const formSubmitReview = document.getElementById('formSubmitReview');
    const btnSubmitReview = document.getElementById('btnSubmitReview');

    // [FEAT-LOGIC] Tự động nạp ngữ cảnh từ nút "⭐ Đánh giá lượt mượn này" (S08/S09 -> S15)
    const queryParams = new URLSearchParams(window.location.search);
    const linkedLoanIdRaw = queryParams.get('loanId');
    const linkedLoanId = linkedLoanIdRaw && /^\d+$/.test(linkedLoanIdRaw) ? parseInt(linkedLoanIdRaw, 10) : null;
    const preTargetType = queryParams.get('targetType');
    const preTargetId = queryParams.get('targetId');

    const syncTargetVisibility = () => {
        const isUser = (targetTypeSelect?.value || 'User') === 'User';
        if (userTargetGroup) userTargetGroup.style.display = isUser ? 'block' : 'none';
        if (bookTargetGroup) bookTargetGroup.style.display = isUser ? 'none' : 'block';
    };

    if (preTargetType === 'Book' || preTargetType === 'User') {
        if (targetTypeSelect) targetTypeSelect.value = preTargetType;
    }
    syncTargetVisibility();

    if (preTargetId) {
        const selectEl = (targetTypeSelect?.value || 'User') === 'User'
            ? document.getElementById('reviewTargetUserId')
            : document.getElementById('reviewTargetBookId');
        if (selectEl && [...selectEl.options].some(opt => opt.value === preTargetId)) {
            selectEl.value = preTargetId;
        }
    }

    if (linkedLoanId && formSubmitReview) {
        const notice = document.createElement('div');
        notice.className = 'bs-badge bs-badge-info';
        notice.innerText = `🔗 Đang đánh giá gắn với Hợp đồng mượn #${linkedLoanId}`;
        formSubmitReview.prepend(notice);
        document.getElementById('reviewComment')?.focus();
    }

    // 1. Chuyển đổi đối tượng đánh giá (User vs Book)
    targetTypeSelect?.addEventListener('change', syncTargetVisibility);

    // 2. Gửi đánh giá 2 chiều lên Server
    formSubmitReview?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const targetType = targetTypeSelect?.value || 'User';
        const targetId = targetType === 'User'
            ? document.getElementById('reviewTargetUserId')?.value
            : document.getElementById('reviewTargetBookId')?.value;
        const rating = parseInt(document.getElementById('reviewRating')?.value || '5', 10);
        const comment = document.getElementById('reviewComment')?.value.trim() || '';

        if (!targetId || !comment) {
            showSystemStatus(400, 'Thiếu thông tin đánh giá', 'Vui lòng chọn đối tượng và nhập nội dung nhận xét.');
            return;
        }

        const res = await apiFetch('/S15TrustScoreReview/SubmitReview', {
            method: 'POST',
            body: {
                targetType,
                targetId: String(targetId),
                loanId: linkedLoanId,
                rating,
                comment
            },
            button: btnSubmitReview
        });

        if (res.ok) {
            setTimeout(() => window.location.reload(), 650);
        }
    });

    // 3. Lọc nhanh danh sách Review trên giao diện
    document.querySelectorAll('.js-filter-review').forEach(pill => {
        pill.addEventListener('click', () => {
            document.querySelectorAll('.js-filter-review').forEach(p => p.classList.remove('active'));
            pill.classList.add('active');

            const filterType = pill.getAttribute('data-type') || 'all';
                        document.querySelectorAll('.s15-review-item').forEach(card => {
                            const cardType = card.getAttribute('data-target-type');
                            card.style.display = (filterType === 'all' || cardType === filterType) ? 'block' : 'none';
                        });
                    });
                });

                document.querySelectorAll('.js-s15-edit-review').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const reviewId = parseInt(btn.getAttribute('data-review-id') || '0', 10);
                        const curRating = parseInt(btn.getAttribute('data-rating') || '5', 10);
                        const curComment = btn.getAttribute('data-comment') || '';
                        if (!reviewId || typeof window.Swal === 'undefined') return;

                        const { value: formValues } = await window.Swal.fire({
                            title: `Chỉnh sửa Đánh giá #${reviewId}`,
                            html:
                                `<div class="text-start small">` +
                                `<label class="fw-bold mb-1">Số sao đánh giá (1 -> 5 sao)</label>` +
                                `<select id="swalRevRating" class="form-select form-select-sm mb-2">` +
                                `<option value="5" ${curRating === 5 ? 'selected' : ''}>⭐⭐⭐⭐⭐ (5 sao)</option>` +
                                `<option value="4" ${curRating === 4 ? 'selected' : ''}>⭐⭐⭐⭐ (4 sao)</option>` +
                                `<option value="3" ${curRating === 3 ? 'selected' : ''}>⭐⭐⭐ (3 sao)</option>` +
                                `<option value="2" ${curRating === 2 ? 'selected' : ''}>⭐⭐ (2 sao)</option>` +
                                `<option value="1" ${curRating === 1 ? 'selected' : ''}>⭐ (1 sao)</option>` +
                                `</select>` +
                                `<label class="fw-bold mb-1">Nội dung nhận xét *</label>` +
                                `<textarea id="swalRevComment" class="form-control form-control-sm" rows="3">${curComment}</textarea>` +
                                `</div>`,
                            focusConfirm: false,
                            showCancelButton: true,
                            confirmButtonText: 'Lưu thay đổi',
                            cancelButtonText: 'Hủy',
                            confirmButtonColor: '#2563eb',
                            preConfirm: () => {
                                const rating = parseInt(document.getElementById('swalRevRating')?.value || '5', 10);
                                const comment = document.getElementById('swalRevComment')?.value.trim() || '';
                                if (!comment) {
                                    window.Swal.showValidationMessage('Vui lòng nhập nội dung nhận xét');
                                    return null;
                                }
                                return { reviewId, rating, comment };
                            }
                        });

                        if (!formValues) return;

                        const res = await apiFetch('/S15TrustScoreReview/UpdateReview', {
                            method: 'POST',
                            body: formValues,
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });

                document.querySelectorAll('.js-s15-delete-review').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const reviewId = parseInt(btn.getAttribute('data-review-id') || '0', 10);
                        if (!reviewId) return;

                        const ok = await confirmDialog({
                            title: `Xóa đánh giá #${reviewId}?`,
                            text: 'Đánh giá này sẽ bị xóa và điểm uy tín liên quan sẽ được hoàn tác.',
                            confirmText: 'Xóa đánh giá'
                        });
                        if (!ok) return;

                        const res = await apiFetch('/S15TrustScoreReview/DeleteReview', {
                            method: 'POST',
                            body: { reviewId },
                            button: btn
                        });

                        if (res.ok) {
                            window.location.reload();
                        }
                    });
                });
            });
