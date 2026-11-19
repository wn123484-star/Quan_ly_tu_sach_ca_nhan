/**
 * [BS-READING-STUDIO] BOOKSHARE READING STUDIO (IM LẶNG 100% — TUYỆT ĐỐI KHÔNG CÓ ÂM THANH)
 * - Đồng hồ tập trung Pomodoro im lặng (25p / 50p / Tùy chỉnh) lưu vào ReadingSessions
 * - Gia hạn nhanh +7 ngày kịp hạn thuê sách
 * - Voice-to-Quote (SpeechRecognition vi-VN) lưu vào BookNotes
 * - Quote Flashcards lật thẻ 3D + Nền Sepia/Sáng/Đêm + Cỡ chữ A-/A/A+
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const shell = document.getElementById('readingStudioShell');

    // 1. Nền dịu mắt (Sepia / Sáng / Đêm) & Cỡ chữ (A- / A / A+)
    document.querySelectorAll('.js-rs-surface').forEach(btn => {
        btn.addEventListener('click', () => {
            const mode = btn.getAttribute('data-surface') || 'light';
            shell?.classList.remove('rs-surface-light', 'rs-surface-sepia', 'rs-surface-night');
            shell?.classList.add(`rs-surface-${mode}`);
            document.querySelectorAll('.js-rs-surface').forEach(b => b.classList.toggle('active', b === btn));
        });
    });

    document.querySelectorAll('.js-rs-font').forEach(btn => {
        btn.addEventListener('click', () => {
            const size = btn.getAttribute('data-size') || 'md';
            shell?.classList.remove('rs-font-sm', 'rs-font-md', 'rs-font-lg');
            shell?.classList.add(`rs-font-${size}`);
            document.querySelectorAll('.js-rs-font').forEach(b => b.classList.toggle('active', b === btn));
        });
    });

    // 2. Đồng hồ Pomodoro Im Lặng hoàn toàn (KHÔNG phát âm thanh)
    let presetMinutes = 25;
    let remainingSeconds = 25 * 60;
    let timerId = null;

    const clockEl = document.getElementById('pomoClockDisplay');
    const statusEl = document.getElementById('pomoClockStatus');
    const sessionMinInput = document.getElementById('rsSessionMinutes');

    const renderClock = () => {
        const m = String(Math.floor(remainingSeconds / 60)).padStart(2, '0');
        const s = String(remainingSeconds % 60).padStart(2, '0');
        if (clockEl) clockEl.innerText = `${m}:${s}`;
    };

    const stopTimer = () => {
        if (timerId) {
            clearInterval(timerId);
            timerId = null;
        }
    };

    const setPreset = (mins) => {
        stopTimer();
        presetMinutes = Math.max(1, Math.min(180, mins));
        remainingSeconds = presetMinutes * 60;
        if (sessionMinInput) sessionMinInput.value = String(presetMinutes);
        if (statusEl) statusEl.innerText = `Chế độ tập trung im lặng ${presetMinutes} phút`;
        renderClock();
    };

    document.querySelectorAll('.js-pomo-preset').forEach(btn => {
        btn.addEventListener('click', () => {
            setPreset(parseInt(btn.getAttribute('data-mins') || '25', 10));
        });
    });

    document.getElementById('btnSetCustomPomo')?.addEventListener('click', () => {
        const mins = parseInt(document.getElementById('pomoCustomMins')?.value || '15', 10);
        setPreset(mins);
    });

    document.getElementById('btnPomoStart')?.addEventListener('click', () => {
        if (timerId) return;
        if (statusEl) statusEl.innerText = 'Đang tập trung đọc sách (Im lặng)...';
        timerId = setInterval(() => {
            remainingSeconds = Math.max(0, remainingSeconds - 1);
            renderClock();
            if (remainingSeconds === 0) {
                stopTimer();
                if (statusEl) statusEl.innerText = '✅ Đã hoàn tất phiên tập trung! Hãy nhập số trang đã đọc và bấm Lưu.';
                showSystemStatus(200, 'Hoàn tất phiên Pomodoro im lặng!', `Bạn đã hoàn thành ${presetMinutes} phút tập trung đọc sách. Hãy ghi nhận số trang đã đọc!`);
            }
        }, 1000);
    });

    document.getElementById('btnPomoPause')?.addEventListener('click', () => {
        stopTimer();
        if (statusEl) statusEl.innerText = 'Đã tạm dừng phiên đọc.';
    });

    document.getElementById('btnPomoReset')?.addEventListener('click', () => {
        setPreset(presetMinutes);
    });

    renderClock();

    // 3. Lưu phiên đọc vào ReadingSessions & tự động cộng trang vào ReadingProgresses
    document.getElementById('formLogReadingSession')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookId = parseInt(document.getElementById('rsSelectedBookId')?.value || '0', 10);
        const durationMinutes = parseInt(document.getElementById('rsSessionMinutes')?.value || '25', 10);
        const pagesRead = parseInt(document.getElementById('rsSessionPages')?.value || '0', 10);

        const res = await apiFetch('/api/mobile/reading/log-session', {
            method: 'POST',
            body: { bookId, durationMinutes, pagesRead },
            button: document.getElementById('btnSaveReadingSession')
        });

        if (res.ok && res.data) {
            const elPage = document.getElementById('rsCurrentPageText');
            const elPct = document.getElementById('rsPercentText');
            const elBar = document.getElementById('rsProgressBar');
            const elPph = document.getElementById('rsPagesPerHour');
            const elStreak = document.getElementById('rsStreakDays');
            if (elPage) elPage.innerText = String(res.data.currentPage);
            if (elPct) elPct.innerText = String(res.data.percentCompleted);
            if (elBar) elBar.style.width = `${res.data.percentCompleted}%`;
            if (elPph) elPph.innerHTML = `${res.data.pagesPerHour} <span class="fs-6 fw-semibold">trang/giờ</span>`;
            if (elStreak) elStreak.innerHTML = `🔥 ${res.data.readingStreakDays} <span class="fs-6 fw-semibold">ngày</span>`;
        }
    });

    // 4. Gia hạn thuê nhanh (+7 ngày) trực tiếp từ Reading Studio
    document.getElementById('btnRsQuickRenew')?.addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        const loanId = parseInt(btn.getAttribute('data-loan-id') || '0', 10);
        if (!loanId) return;
        const res = await apiFetch('/api/mobile/reading/quick-renew', {
            method: 'POST',
            body: { loanId },
            button: btn
        });
        if (res.ok) {
            setTimeout(() => window.location.reload(), 800);
        }
    });

    // 5. Voice-to-Quote (SpeechRecognition vi-VN)
    const btnVoice = document.getElementById('btnStartVoiceQuote');
    const micStatus = document.getElementById('vqMicStatus');
    const quoteInput = document.getElementById('vqQuoteText');

    btnVoice?.addEventListener('click', () => {
        const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRec) {
            if (micStatus) micStatus.innerText = 'Trình duyệt chưa hỗ trợ SpeechRecognition. Bạn có thể nhập trích dẫn trực tiếp bên dưới.';
            showSystemStatus(400, 'Trình duyệt không hỗ trợ ghi giọng nói', 'Hãy dùng Chrome / Edge hoặc nhập tay câu trích dẫn.');
            return;
        }

        const rec = new SpeechRec();
        rec.lang = 'vi-VN';
        rec.interimResults = false;
        rec.maxAlternatives = 1;

        rec.onstart = () => {
            if (micStatus) micStatus.innerText = '🔴 Đang lắng nghe giọng đọc tiếng Việt (vi-VN)... Hãy đọc câu trích dẫn!';
            btnVoice.disabled = true;
        };
        rec.onresult = (ev) => {
            const text = ev.results?.[0]?.[0]?.transcript || '';
            if (quoteInput && text) {
                quoteInput.value = quoteInput.value ? `${quoteInput.value.trim()} ${text}` : text;
            }
            if (micStatus) micStatus.innerText = '✅ Đã chuyển giọng nói thành văn bản trích dẫn!';
        };
        rec.onerror = (ev) => {
            if (micStatus) micStatus.innerText = `⚠️ Không nhận diện được giọng nói (${ev.error}). Hãy thử lại hoặc gõ tay.`;
        };
        rec.onend = () => {
            btnVoice.disabled = false;
        };

        rec.start();
    });

    document.getElementById('formSaveVoiceQuote')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const bookId = parseInt(document.getElementById('rsSelectedBookId')?.value || '0', 10);
        const pageNumber = parseInt(document.getElementById('vqPageNumber')?.value || '1', 10);
        const quoteText = quoteInput?.value.trim() || '';
        const noteContent = document.getElementById('vqNoteContent')?.value.trim() || '';

        const res = await apiFetch('/api/mobile/reading/voice-quote', {
            method: 'POST',
            body: { bookId, pageNumber, quoteText, noteContent },
            button: document.getElementById('btnSaveVoiceQuote')
        });

        if (res.ok) {
            flashcards.unshift({ PageNumber: pageNumber, QuoteText: quoteText, NoteContent: noteContent || 'Trích dẫn từ Reading Studio' });
            fcIdx = 0;
            renderFlashcard();
            if (quoteInput) quoteInput.value = '';
        }
    });

    // 6. Thẻ lật Quote Flashcards
    let flashcards = [];
    try {
        flashcards = JSON.parse(document.getElementById('rsFlashcardsData')?.textContent || '[]') || [];
    } catch {
        flashcards = [];
    }
    let fcIdx = 0;
    const fcBox = document.getElementById('rsFlashcardBox');

    const renderFlashcard = () => {
        const counter = document.getElementById('flashCounter');
        const badge = document.getElementById('fcFrontBadge');
        const quote = document.getElementById('fcFrontQuote');
        const note = document.getElementById('fcBackNote');
        fcBox?.classList.remove('is-flipped');

        if (!flashcards.length) {
            if (counter) counter.innerText = '0 / 0';
            return;
        }
        fcIdx = ((fcIdx % flashcards.length) + flashcards.length) % flashcards.length;
        const item = flashcards[fcIdx];
        if (counter) counter.innerText = `${fcIdx + 1} / ${flashcards.length}`;
        if (badge) badge.innerText = `Trang ${item.PageNumber ?? item.pageNumber ?? 1}`;
        if (quote) quote.innerText = `“${item.QuoteText || item.quoteText || item.NoteContent || item.noteContent || ''}”`;
        if (note) note.innerText = item.NoteContent || item.noteContent || 'Trích dẫn tâm đắc đã lưu trong sổ tay.';
    };

    fcBox?.addEventListener('click', () => fcBox.classList.toggle('is-flipped'));
    fcBox?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            fcBox.classList.toggle('is-flipped');
        }
    });
    document.getElementById('btnFlashPrev')?.addEventListener('click', () => { fcIdx--; renderFlashcard(); });
    document.getElementById('btnFlashNext')?.addEventListener('click', () => { fcIdx++; renderFlashcard(); });
    renderFlashcard();
});
