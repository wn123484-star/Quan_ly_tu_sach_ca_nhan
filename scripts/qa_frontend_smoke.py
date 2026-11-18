#!/usr/bin/env python3
"""Browser smoke tests for shared BookShare form/API/responsive UI helpers.

This is a component-level harness. It does not boot ASP.NET Core or replace
runtime/database integration tests.

Run from the repository root:
    python scripts/qa_frontend_smoke.py

Requirements: Python 3, Playwright (`python -m pip install playwright`) and
Chromium (system Chromium or `python -m playwright install chromium`).
"""
from __future__ import annotations

import json
import re
import shutil
import sys
from pathlib import Path
from typing import Any

try:
    from playwright.sync_api import sync_playwright
except ImportError as exc:  # pragma: no cover - external dependency
    raise SystemExit(
        "Thiếu Playwright. Cài bằng: python -m pip install playwright && "
        "python -m playwright install chromium"
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src" / "BookShare.Web"


def assert_true(name: str, condition: bool, details: Any = None) -> dict[str, Any]:
    result = {"name": name, "result": "PASS" if condition else "FAIL"}
    if details is not None:
        result["details"] = details
    if not condition:
        raise AssertionError(json.dumps(result, ensure_ascii=False))
    return result


def make_harness_html() -> str:
    links = "".join(f'<a href="#">S{i:02d} Dịch vụ {i}</a><span>|</span>' for i in range(1, 23))
    return f"""<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
<body>
<header class="portal-header-wrapper">
  <div class="portal-utility-container">
    <nav class="portal-service-nav" aria-label="Điều hướng 22 dịch vụ BookShare">
      <div class="portal-service-links">{links}</div>
    </nav>
    <div class="portal-utility-actions"><button type="button">Tài khoản</button></div>
  </div>
</header>
<div class="container mt-3">
  <div id="systemStatusBox" class="status-banner" role="status" aria-live="polite">
    <div class="status-banner-left"><span id="statusCodeBadge"></span>
      <div class="status-text-group"><strong id="statusTitleText"></strong><span id="statusDetailText"></span></div>
    </div><button type="button" id="btnHideStatusBox">×</button>
  </div>
</div>
<main class="container">
  <form id="testForm"><label for="email">Email</label><input id="email" type="email" required><button type="submit">Gửi</button></form>
  <form id="otpForm"><div class="s01-otp-box">
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
    <input class="s01-otp-digit" type="text" maxlength="1" pattern="[0-9]" required>
  </div><button type="submit">Xác thực OTP</button></form>
  <table><tbody><tr><td>Nội dung bảng cần cuộn ngang trên điện thoại</td></tr></tbody></table>
  <div class="modal-dialog"><div class="modal-content">Modal kiểm thử</div></div>
  <div class="ai-chat-widget"><button type="button" class="ai-chat-fab">Chat</button></div>
</main>
</body></html>"""


def main() -> int:
    if not (WEB / "wwwroot/js/form-validation.js").exists():
        raise SystemExit(f"Không tìm thấy source trong {WEB}")

    validation_js = (WEB / "wwwroot/js/form-validation.js").read_text(encoding="utf-8")
    core_api_js = (WEB / "wwwroot/js/core-api.js").read_text(encoding="utf-8")
    # Expose module exports to a classic-script browser test harness.
    core_api_js = re.sub(r"\bexport\s+", "", core_api_js)
    core_api_js += "\nwindow.__testApiFetch = apiFetch; window.__testShowStatus = showSystemStatus;"

    results: list[dict[str, Any]] = []
    with sync_playwright() as playwright:
        chromium_path = (
            shutil.which("chromium")
            or shutil.which("chromium-browser")
            or shutil.which("google-chrome")
        )
        launch_args: dict[str, Any] = {"headless": True, "args": ["--no-sandbox"]}
        if chromium_path:
            launch_args["executable_path"] = chromium_path
        browser = playwright.chromium.launch(**launch_args)
        page = browser.new_page(viewport={"width": 375, "height": 812})
        page.set_content(make_harness_html(), wait_until="domcontentloaded")
        page.add_style_tag(path=str(WEB / "wwwroot/css/core-theme.css"))
        page.add_script_tag(content=validation_js)

        page.evaluate("""() => {
            window.submitObserved = 0;
            document.getElementById('testForm').addEventListener('submit', event => {
                window.submitObserved += 1;
                event.preventDefault();
            });
        }""")
        page.locator("#testForm button").click()
        invalid_blocked = page.evaluate("""() =>
            window.submitObserved === 0 &&
            document.getElementById('email').getAttribute('aria-invalid') === 'true' &&
            !!document.querySelector('#email + .form-text.text-danger')
        """)
        results.append(assert_true("Client validation chặn trường bắt buộc rỗng và hiển thị lỗi inline", invalid_blocked))

        page.locator("#email").fill("khong-phai-email")
        mismatch = page.evaluate("() => document.getElementById('email').validity.typeMismatch")
        results.append(assert_true("Client validation nhận diện email sai định dạng", mismatch))

        page.locator("#email").fill("tester@example.com")
        page.locator("#testForm button").click()
        valid_submits = page.evaluate("() => window.submitObserved === 1")
        results.append(assert_true("Form hợp lệ được phép submit", valid_submits))

        page.evaluate("() => { window.otpSubmitObserved = 0; document.getElementById('otpForm').addEventListener('submit', e => { window.otpSubmitObserved++; e.preventDefault(); }); }")
        page.locator('#otpForm button').click()
        otp_empty_blocked = page.evaluate("() => window.otpSubmitObserved === 0 && document.querySelectorAll('#otpForm .s01-otp-digit[aria-invalid=\"true\"]').length === 6")
        results.append(assert_true("OTP bắt buộc đủ sáu ô trước khi submit", otp_empty_blocked))
        page.locator('#otpForm .s01-otp-digit').nth(0).fill('x')
        otp_non_digit_blocked = page.evaluate("() => document.querySelector('#otpForm .s01-otp-digit').validity.patternMismatch")
        results.append(assert_true("OTP từ chối ký tự không phải chữ số", otp_non_digit_blocked))
        for index, digit in enumerate('123456'):
            page.locator('#otpForm .s01-otp-digit').nth(index).fill(digit)
        page.locator('#otpForm button').click()
        otp_valid_submits = page.evaluate("() => window.otpSubmitObserved === 1")
        results.append(assert_true("OTP sáu chữ số hợp lệ được phép submit", otp_valid_submits))

        wrapped = page.locator("main .table-responsive table").count() == 1
        results.append(assert_true("Bảng dữ liệu tự được bọc bằng vùng cuộn ngang", wrapped))

        viewport_results: dict[str, Any] = {}
        no_document_overflow = True
        for width, height in ((375, 812), (768, 1024), (1366, 768)):
            page.set_viewport_size({"width": width, "height": height})
            page.wait_for_timeout(40)
            metrics = page.evaluate("""() => ({
                viewport: innerWidth,
                documentWidth: document.documentElement.scrollWidth,
                navClientWidth: document.querySelector('.portal-service-nav').clientWidth,
                navScrollWidth: document.querySelector('.portal-service-nav').scrollWidth
            })""")
            viewport_results[str(width)] = metrics
            no_document_overflow &= metrics["documentWidth"] <= width
        results.append(assert_true(
            "Không tràn ngang document tại 375px, 768px và 1366px",
            no_document_overflow,
            viewport_results,
        ))

        page.add_script_tag(content=core_api_js)
        page.evaluate("""() => {
            window.fetch = async () => new Response(JSON.stringify({title:'Saved', message:'Đã lưu dữ liệu'}), {
                status: 200, headers: {'Content-Type':'application/json'}
            });
        }""")
        status_200 = page.evaluate("""async () => {
            const result = await window.__testApiFetch('/test', {method:'POST', body:{x:1}});
            return {ok:result.ok, status:result.status, className:document.getElementById('systemStatusBox').className};
        }""")
        results.append(assert_true("apiFetch hiển thị HTTP 200 thành công", status_200["ok"] and status_200["status"] == 200 and "status-200" in status_200["className"], status_200))

        page.evaluate("""() => {
            window.fetch = async () => new Response(JSON.stringify({message:'Invalid input'}), {
                status: 400, headers: {'Content-Type':'application/json'}
            });
        }""")
        status_400 = page.evaluate("""async () => {
            const result = await window.__testApiFetch('/test', {method:'POST', body:{x:1}});
            return {ok:result.ok, status:result.status, className:document.getElementById('systemStatusBox').className};
        }""")
        results.append(assert_true("apiFetch hiển thị HTTP 400 lỗi dữ liệu", not status_400["ok"] and status_400["status"] == 400 and "status-400" in status_400["className"], status_400))

        page.evaluate("""() => {
            window.fetch = async () => new Response(JSON.stringify({message:'No permission'}), {
                status: 403, headers: {'Content-Type':'application/json'}
            });
        }""")
        status_403 = page.evaluate("""async () => {
            const result = await window.__testApiFetch('/test', {method:'POST', body:{x:1}});
            return {
                ok: result.ok,
                status: result.status,
                className: document.getElementById('systemStatusBox').className,
                badge: document.getElementById('statusCodeBadge').innerText
            };
        }""")
        results.append(assert_true(
            "apiFetch hiển thị HTTP 403 bằng trạng thái riêng",
            status_403["status"] == 403 and "status-403" in status_403["className"] and not status_403["ok"],
            status_403,
        ))

        page.evaluate("() => { window.fetch = async () => new Response(null, {status:204}); }")
        no_content = page.evaluate("""async () => {
            const result = await window.__testApiFetch('/test');
            return {ok: result.ok, status: result.status, data: result.data};
        }""")
        results.append(assert_true("apiFetch xử lý response 204 không có body", no_content["ok"] and no_content["data"] is None, no_content))

        page.evaluate("() => { window.fetch = async () => new Response('oops', {status:500}); }")
        status_500 = page.evaluate("""async () => {
            const result = await window.__testApiFetch('/test');
            return {
                ok: result.ok,
                status: result.status,
                className: document.getElementById('systemStatusBox').className,
                detail: document.getElementById('statusDetailText').innerText
            };
        }""")
        results.append(assert_true(
            "apiFetch hiển thị HTTP 500 với thông báo an toàn",
            status_500["status"] == 500 and "status-500" in status_500["className"] and "exception" not in status_500["detail"].lower(),
            status_500,
        ))

        browser.close()

    # Server-side security guardrails: source-level assertions only, not HTTP integration tests.
    s01_controller = (WEB / "Controllers/S01AuthOtpGoogleController.cs").read_text(encoding="utf-8")
    s21_controller = (WEB / "Controllers/S21LibrarianDeskController.cs").read_text(encoding="utf-8")
    s22_controller = (WEB / "Controllers/S22AdminAnalyticsController.cs").read_text(encoding="utf-8")
    auth_service = (WEB / "Services/Implementations/S01AuthOtpService.cs").read_text(encoding="utf-8")
    viewmodel = (WEB / "Models/ViewModels/S01AuthViewModel.cs").read_text(encoding="utf-8")
    s01_view = (WEB / "Views/Modules/S01_AuthOtpGoogle/Index.cshtml").read_text(encoding="utf-8")
    results.append(assert_true("Server-side OTP DTO yêu cầu đúng sáu chữ số", bool(re.search(r'RegularExpression\(\"\^\\\\d\{6\}\$\"', viewmodel))))
    results.append(assert_true(
        "S01 UpdateProfile và ChangePassword có [Authorize]",
        bool(re.search(r"\[Authorize\]\s*\[HttpPost\][\s\S]{0,100}UpdateProfile", s01_controller)) and
        bool(re.search(r"\[Authorize\]\s*\[HttpPost\][\s\S]{0,100}ChangePassword", s01_controller)),
    ))
    results.append(assert_true(
        "S21/S22 GET được bảo vệ bằng role server-side và S22 không fallback user ID",
        bool(re.search(r"\[Authorize\(Roles = \"Librarian,Admin\"\)\]\s*public async Task<IActionResult> Index", s21_controller)) and
        bool(re.search(r"\[Authorize\(Roles = \"Admin\"\)\]\s*public async Task<IActionResult> Index", s22_controller)) and
        '?? "u-admin-01"' not in s22_controller and
        '?? "u-librarian-01"' not in s21_controller,
    ))
    results.append(assert_true(
        "Đăng ký công khai luôn gán role User phía server",
        'const string roleName = "User";' in auth_service and
        "input.SelectedRole" not in auth_service and
        'name="SelectedRole"' not in s01_view and "SelectedRole" not in viewmodel,
    ))

    summary = {
        "suite": "BookShare shared UI component smoke tests",
        "passed": len(results),
        "failed": 0,
        "results": results,
        "limitations": [
            "Harness trình duyệt dùng DOM giả lập cho helper UI; không boot ASP.NET Core.",
            "Không kiểm tra SQL Server, EF Core save/load, cookie auth HTTP hoặc luồng mượn/trả thật.",
            "Cần dotnet build và kiểm thử end-to-end sau khi chạy ứng dụng với BookShareDb.",
        ],
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
