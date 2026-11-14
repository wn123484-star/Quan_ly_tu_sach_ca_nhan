/**
 * MODULE S06: SEARCH & DISCOVERY (s06-search-discovery.js)
 * Đóng gói ES6 Module độc lập cho dịch vụ S06
 */
import { apiFetch, showSystemStatus } from '../../js/core-api.js';

document.addEventListener('DOMContentLoaded', () => {
    const modalEl = document.getElementById('s06QuickLoanModal');
    const loanForm = document.getElementById('s06QuickLoanForm');
    const copyIdInput = document.getElementById('loanCopyIdInput');
    const modalBookTitle = document.getElementById('modalBookTitle');
    const modalBarcode = document.getElementById('modalBarcode');
    const modalOwnerName = document.getElementById('modalOwnerName');
    const loanDesiredDays = document.getElementById('loanDesiredDays');
    const loanPickupHubId = document.getElementById('loanPickupHubId');
    const loanRequestNote = document.getElementById('loanRequestNote');

    let bsModal = null;
    if (modalEl && window.bootstrap) {
        bsModal = new window.bootstrap.Modal(modalEl);
    }

    // 1. Mở hộp thoại đăng ký mượn sách vật lý
    document.querySelectorAll('.js-open-loan-modal').forEach(btn => {
        btn.addEventListener('click', () => {
            const copyId = btn.getAttribute('data-copy-id') || '';
            const barcode = btn.getAttribute('data-barcode') || '';
            const title = btn.getAttribute('data-title') || '';
            const owner = btn.getAttribute('data-owner') || '';

            if (copyIdInput) copyIdInput.value = copyId;
            if (modalBookTitle) modalBookTitle.innerText = title;
            if (modalBarcode) modalBarcode.innerText = barcode;
            if (modalOwnerName) modalOwnerName.innerText = owner;
            if (loanRequestNote) {
                loanRequestNote.value = `Đăng ký mượn bản sao [${barcode}] - ${title}. Cam kết bảo quản sách tốt và trả đúng hạn.`;
            }

            if (bsModal) {
                bsModal.show();
            }
        });
    });

    // 2. Gửi phiếu yêu cầu mượn sách (LoanRequests)
    if (loanForm) {
        loanForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const bookCopyId = parseInt(copyIdInput?.value || '0', 10);
            const desiredDays = parseInt(loanDesiredDays?.value || '14', 10);
            const pickupHubId = parseInt(loanPickupHubId?.value || '3', 10);
            const requestNote = loanRequestNote?.value.trim() || '';

            if (!bookCopyId) {
                showSystemStatus(400, 'Thiếu mã bản sao (Mã 400)', 'Vui lòng chọn một bản sao sách hợp lệ.');
                return;
            }

            const res = await apiFetch('/S06SearchDiscovery/QuickRequestLoan', {
                method: 'POST',
                body: {
                    bookCopyId,
                    desiredDays,
                    pickupHubId,
                    requestNote
                }
            });

            if (res.ok && res.data) {
                if (bsModal) {
                    bsModal.hide();
                }
                showSystemStatus(200, res.data.title || 'Đã gửi yêu cầu mượn sách (Mã 200)', res.data.message);
            }
        });
    }

    // 3. Gợi ý tức thì (Autocomplete) + Debounce 300ms cho ô từ khóa
    const keywordInput = document.querySelector('.s06-filter-box input[name="keyword"]');
    if (keywordInput) {
        initAutocomplete(keywordInput);
    }
});

function debounce(fn, delay = 300) {
    let timer = null;
    return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), delay);
    };
}

function escapeHtml(text) {
    return String(text ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
}

function highlight(text, query) {
    const safe = escapeHtml(text);
    const q = escapeHtml(query).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return q ? safe.replace(new RegExp(`(${q})`, 'ig'), '<mark>$1</mark>') : safe;
}

function initAutocomplete(input) {
    const form = input.closest('form');
    const wrap = input.closest('.col-lg-4, .col-md-6') || input.parentElement;
    wrap.classList.add('autocomplete-wrap');

    const list = document.createElement('ul');
    list.id = 's06SuggestList';
    list.className = 'autocomplete-list d-none';
    list.setAttribute('role', 'listbox');
    list.setAttribute('aria-label', 'Gợi ý sách');
    wrap.appendChild(list);

    input.setAttribute('autocomplete', 'off');
    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-controls', list.id);
    input.setAttribute('aria-expanded', 'false');

    let items = [];
    let activeIndex = -1;
    let requestSeq = 0;

    const close = () => {
        list.classList.add('d-none');
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
        activeIndex = -1;
    };

    const setActive = (index) => {
        const options = [...list.querySelectorAll('li')];
        options.forEach((li, i) => li.setAttribute('aria-selected', String(i === index)));
        activeIndex = index;
        if (options[index]) {
            input.setAttribute('aria-activedescendant', options[index].id);
            options[index].scrollIntoView({ block: 'nearest' });
        }
    };

    const choose = (index) => {
        const item = items[index];
        if (!item) return;
        input.value = item.title;
        close();
        form?.requestSubmit();
    };

    const render = (query) => {
        if (!items.length) {
            list.innerHTML = `<li class="text-muted small" aria-disabled="true">Không có sách nào khớp "${escapeHtml(query)}"</li>`;
        } else {
            list.innerHTML = items.map((b, i) => `
                <li id="s06Suggest-${i}" role="option" aria-selected="false" data-index="${i}">
                    <img src="${escapeHtml(b.coverImageUrl || 'https://images.unsplash.com/photo-1544947950-fa07a98d237f?w=80')}" alt="" onerror="this.style.visibility='hidden'" />
                    <div class="flex-grow-1 small">
                        <div class="fw-bold">${highlight(b.title, query)}</div>
                        <div class="text-muted">${highlight(b.author, query)} • ISBN ${escapeHtml(b.isbn)}</div>
                    </div>
                    <span class="badge ${b.availableCopies > 0 ? 'bg-success' : 'bg-secondary'}">${b.availableCopies} sẵn</span>
                </li>`).join('');
        }
        list.classList.remove('d-none');
        input.setAttribute('aria-expanded', 'true');
        activeIndex = -1;
    };

    const fetchSuggestions = debounce(async (query) => {
        const seq = ++requestSeq;
        try {
            const res = await fetch(`/S06SearchDiscovery/Suggest?q=${encodeURIComponent(query)}`, {
                headers: { Accept: 'application/json' },
                credentials: 'same-origin'
            });
            if (!res.ok || seq !== requestSeq) return;
            items = await res.json();
            if (seq === requestSeq && input.value.trim() === query) render(query);
        } catch {
            close();
        }
    }, 300);

    input.addEventListener('input', () => {
        const query = input.value.trim();
        if (query.length < 2) {
            requestSeq++;
            close();
            return;
        }
        fetchSuggestions(query);
    });

    input.addEventListener('keydown', (e) => {
        if (list.classList.contains('d-none') || !items.length) return;
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            setActive((activeIndex + 1) % items.length);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            setActive((activeIndex - 1 + items.length) % items.length);
        } else if (e.key === 'Enter' && activeIndex >= 0) {
            e.preventDefault();
            choose(activeIndex);
        } else if (e.key === 'Escape') {
            close();
        }
    });

    list.addEventListener('mousedown', (e) => {
        const li = e.target.closest('li[data-index]');
        if (!li) return;
        e.preventDefault();
        choose(parseInt(li.dataset.index, 10));
    });

    input.addEventListener('blur', () => setTimeout(close, 120));
}
