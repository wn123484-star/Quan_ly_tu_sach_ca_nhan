import { apiFetch } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const formGenerateCustomRec = document.getElementById('formGenerateCustomRec');
    const btnGenerateRoadmap = document.getElementById('btnGenerateRoadmap');
    const adviceBox = document.getElementById('aiCustomAdviceBox');

    // 1. Nhờ Gemini AI thiết kế lộ trình đọc cá nhân hóa
    formGenerateCustomRec?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const skillGoal = document.getElementById('customSkillGoal')?.value.trim() || '';
        const mood = document.getElementById('customMoodSelect')?.value || 'DeepFocus';

        const res = await apiFetch('/S14AiRecommendation/GenerateCustomAdvice', {
            method: 'POST',
            body: JSON.stringify({ mood, skillGoal })
        }, btnGenerateRoadmap);

        if (res.ok && res.data && adviceBox) {
            adviceBox.style.display = 'block';
            adviceBox.innerText = res.data.aiAdvice || '';
        }
    });

    // 2. Bấm gửi yêu cầu mượn ngay từ thẻ gợi ý S14 (tái sử dụng API S06)
    document.querySelectorAll('.js-s14-request-loan').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookCopyId = parseInt(btn.getAttribute('data-copy-id') || '0', 10);
            const title = btn.getAttribute('data-title') || 'Sách';
            if (!bookCopyId) return;

            const res = await apiFetch('/S06SearchDiscovery/QuickRequestLoan', {
                method: 'POST',
                body: JSON.stringify({
                    bookCopyId,
                    requestedDays: 14,
                    borrowerNote: `Đăng ký mượn từ Gợi ý AI cá nhân hóa (S14) cho sách "${title}"`
                })
            }, btn);

            if (res.ok) {
                btn.textContent = '✅ Đã tạo yêu cầu mượn';
                btn.disabled = true;
            }
        });
    });

    // 3. Bấm thêm sách vào bộ theo dõi đọc S03 ngay từ thẻ gợi ý S14
    document.querySelectorAll('.js-s14-add-tracker').forEach(btn => {
        btn.addEventListener('click', async () => {
            const bookId = parseInt(btn.getAttribute('data-book-id') || '0', 10);
            const totalPages = parseInt(btn.getAttribute('data-pages') || '300', 10);
            if (!bookId) return;

            const res = await apiFetch('/S14AiRecommendation/QuickAddToTracker', {
                method: 'POST',
                body: JSON.stringify({
                    bookId,
                    totalPages,
                    status: 'WantToRead'
                })
            }, btn);

            if (res.ok) {
                btn.textContent = '✅ Đã thêm vào mục tiêu S03';
                btn.disabled = true;
            }
        });
    });
});
