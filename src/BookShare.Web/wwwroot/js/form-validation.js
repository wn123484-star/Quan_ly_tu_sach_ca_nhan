/* Shared HTML5 client validation for every BookShare form. */
(() => {
    const feedbackNodes = new WeakMap();
    let feedbackSequence = 0;

    const messageFor = (control) => {
        if (control.dataset.validationMessage) return control.dataset.validationMessage;
        const validity = control.validity;
        if (!validity) return 'Dữ liệu không hợp lệ.';
        if (validity.valueMissing) return 'Vui lòng nhập trường này.';
        if (validity.typeMismatch) {
            if (control.type === 'email') return 'Vui lòng nhập địa chỉ email đúng định dạng.';
            if (control.type === 'url') return 'Vui lòng nhập đường dẫn URL hợp lệ.';
            return 'Dữ liệu không đúng định dạng.';
        }
        if (validity.patternMismatch) {
            if (control.matches('.s01-otp-digit')) return 'Mỗi ô chỉ được chứa một chữ số từ 0 đến 9.';
            return 'Dữ liệu chưa đúng định dạng yêu cầu.';
        }
        if (validity.tooShort) return `Vui lòng nhập ít nhất ${control.minLength} ký tự.`;
        if (validity.tooLong) return `Vui lòng nhập không quá ${control.maxLength} ký tự.`;
        if (validity.rangeUnderflow) return `Giá trị nhỏ nhất là ${control.min}.`;
        if (validity.rangeOverflow) return `Giá trị lớn nhất là ${control.max}.`;
        if (validity.stepMismatch) return 'Vui lòng nhập giá trị đúng bước cho phép.';
        if (validity.badInput) return 'Vui lòng kiểm tra lại dữ liệu đã nhập.';
        return 'Dữ liệu không hợp lệ.';
    };

    const feedbackFor = (control) => {
        let feedback = feedbackNodes.get(control);
        if (feedback?.isConnected) return feedback;

        const otpGroup = control.closest('.s01-otp-box');
        if (otpGroup) {
            feedback = otpGroup.parentElement.querySelector('.s01-otp-validation-message');
            if (!feedback) {
                feedback = document.createElement('div');
                feedback.className = 'form-text text-danger small mt-1 s01-otp-validation-message';
                feedback.id = `form-validation-message-${++feedbackSequence}`;
                feedback.setAttribute('aria-live', 'polite');
                feedback.hidden = true;
                otpGroup.insertAdjacentElement('afterend', feedback);
            }
        } else {
            feedback = document.createElement('div');
            feedback.className = 'form-text text-danger small mt-1';
            feedback.id = `form-validation-message-${++feedbackSequence}`;
            feedback.setAttribute('aria-live', 'polite');
            feedback.hidden = true;
            const inputGroup = control.closest('.input-group');
            if (inputGroup) inputGroup.insertAdjacentElement('afterend', feedback);
            else control.insertAdjacentElement('afterend', feedback);
        }

        feedbackNodes.set(control, feedback);
        const currentDescription = control.getAttribute('aria-describedby');
        const ids = new Set((currentDescription || '').split(/\s+/).filter(Boolean));
        ids.add(feedback.id);
        control.setAttribute('aria-describedby', [...ids].join(' '));
        return feedback;
    };

    const syncCustomValidity = (control) => {
        if (!(control instanceof HTMLInputElement || control instanceof HTMLTextAreaElement || control instanceof HTMLSelectElement)) return;
        if (!control.required || control.disabled || control.type === 'hidden') {
            control.setCustomValidity('');
            return;
        }
        const textLike = control instanceof HTMLTextAreaElement ||
            (control instanceof HTMLInputElement && ['text', 'email', 'url', 'tel', 'search'].includes(control.type));
        if (textLike && control.value.length > 0 && control.value.trim().length === 0) {
            control.setCustomValidity('Không được chỉ nhập khoảng trắng.');
        } else {
            control.setCustomValidity('');
        }
    };

    const showError = (control) => {
        if (!(control instanceof HTMLElement) || !('validity' in control) || control.disabled || control.type === 'hidden') return;
        syncCustomValidity(control);
        const invalid = !control.validity.valid;
        control.classList.toggle('is-invalid', invalid);
        control.setAttribute('aria-invalid', invalid ? 'true' : 'false');

        if (invalid) {
            const feedback = feedbackFor(control);
            feedback.textContent = messageFor(control);
            feedback.hidden = false;
        } else {
            const feedback = feedbackNodes.get(control);
            if (feedback) {
                if (control.matches('.s01-otp-digit')) {
                    const otpInputs = Array.from(control.closest('form')?.querySelectorAll('.s01-otp-digit') || []);
                    const firstInvalid = otpInputs.find((item) => !item.validity.valid);
                    if (firstInvalid) {
                        feedback.textContent = messageFor(firstInvalid);
                        feedback.hidden = false;
                    } else {
                        feedback.textContent = '';
                        feedback.hidden = true;
                    }
                } else {
                    feedback.textContent = '';
                    feedback.hidden = true;
                }
            }
        }
    };

    // Give every server-rendered table its own scroll container. This keeps all 22
    // module pages usable on 375px phones without clipping table columns.
    const makeTablesScrollable = () => {
        document.querySelectorAll('main table').forEach((table) => {
            if (table.closest('.table-responsive')) return;
            const wrapper = document.createElement('div');
            wrapper.className = 'table-responsive';
            wrapper.tabIndex = 0;
            wrapper.setAttribute('role', 'region');
            wrapper.setAttribute('aria-label', 'Bảng dữ liệu có thể cuộn ngang');
            table.parentNode?.insertBefore(wrapper, table);
            wrapper.append(table);
        });
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', makeTablesScrollable, { once: true });
    } else {
        makeTablesScrollable();
    }

    // Native constraint validation fires before submit handlers (including AJAX handlers).
    document.addEventListener('invalid', (event) => {
        if (event.target instanceof HTMLElement) showError(event.target);
    }, true);

    document.addEventListener('input', (event) => {
        if (event.target instanceof HTMLElement && 'validity' in event.target) showError(event.target);
    });

    document.addEventListener('change', (event) => {
        if (event.target instanceof HTMLElement && 'validity' in event.target) showError(event.target);
    });

    document.addEventListener('submit', (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        const controls = Array.from(form.elements).filter((item) =>
            item instanceof HTMLElement && 'validity' in item && !item.disabled && item.type !== 'hidden');
        let firstInvalid = null;
        controls.forEach((control) => {
            showError(control);
            if (!control.validity.valid && !firstInvalid) firstInvalid = control;
        });

        if (firstInvalid) {
            event.preventDefault();
            // Prevent page/module submit handlers from sending an invalid AJAX request.
            event.stopImmediatePropagation();
            firstInvalid.focus();
        }
    }, true);
})();
