import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const targetTypeSelect = document.getElementById('reviewTargetType');
    const userTargetGroup = document.getElementById('userTargetGroup');
    const bookTargetGroup = document.getElementById('bookTargetGroup');
    const formSubmitReview = document.getElementById('formSubmitReview');
    const btnSubmitReview = document.getElementById('btnSubmitReview');

    // 1. Chuyển đổi đối tượng đánh giá (User vs Book)
    targetTypeSelect?.addEventListener('change', () => {
        const isUser = targetTypeSelect.value === 'User';
        if (userTargetGroup) userTargetGroup.style.display = isUser ? 'block' : 'none';
        if (bookTargetGroup) bookTargetGroup.style.display = isUser ? 'none' : 'block';
    });

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
            body: JSON.stringify({
                targetType,
                targetId: String(targetId),
                rating,
                comment
            })
        }, btnSubmitReview);

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
});
