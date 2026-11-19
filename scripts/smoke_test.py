#!/usr/bin/env python3
"""
BookShare Automated Smoke Test & Security Audit Script (scripts/smoke_test.py)
Kiểm thử tự động 3 lớp trước khi triển khai lên VPS:
  - Lớp 1: Biên dịch (dotnet build) + Quét mã nguồn (không sót nhãn S01-S22 / Ver07 trên UI + kiểm tra Docker mount)
  - Lớp 2: Quét GET Trang chủ & toàn bộ 22 phân hệ (/S01AuthOtpGoogle -> /S22AdminAnalytics), đảm bảo HTTP 200 & 0 lỗi 500
  - Lớp 3: Kiểm thử bảo mật RBAC & CSRF cho các API Admin / Librarian / Curator khi chưa đăng nhập (bắt buộc 403 / 400)

Cách chạy:
  1. Kiểm thử vào server đang chạy sẵn (mặc định http://127.0.0.1:5087):
     python scripts/smoke_test.py --base-url http://127.0.0.1:5087
  2. Tự động khởi chạy tạm BookShare.Web rồi kiểm thử & tắt gọn:
     python scripts/smoke_test.py --autostart
"""

from __future__ import annotations

import argparse
import http.cookiejar
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT_DIR = pathlib.Path(__file__).resolve().parent.parent
WEB_CSPROJ = ROOT_DIR / "src" / "BookShare.Web" / "BookShare.Web.csproj"
VIEWS_DIR = ROOT_DIR / "src" / "BookShare.Web" / "Views"

ROUTES_22 = [
    "/",
    "/S01AuthOtpGoogle",
    "/S02PersonalShelf",
    "/S03ReadingTracker",
    "/S04BookNotesQuotes",
    "/S05IsbnAutoCatalog",
    "/S06SearchDiscovery",
    "/S07LibraryCatalog",
    "/S08LibraryLoanFlow",
    "/S09P2PLoanFlow",
    "/S10ConditionReport",
    "/S11HotBookQueue",
    "/S12LoanRenewalFine",
    "/S13GeminiAiAssistant",
    "/S14AiRecommendation",
    "/S15TrustScoreReview",
    "/S16MembershipTier",
    "/S17SandboxPayment",
    "/S18NotificationHub",
    "/S19DisputeResolution",
    "/S20ExchangeHubClub",
    "/S21LibrarianDesk",
    "/S22AdminAnalytics",
]

SENSITIVE_RBAC_ENDPOINTS = [
    ("/S22AdminAnalytics/ManageSampleData", {"actionMode": "ResetTransactions"}, "Admin"),
    ("/S21LibrarianDesk/BatchImportCopies", {"bookId": 1, "quantity": 1}, "Librarian,Admin"),
    ("/S19DisputeResolution/ResolveDispute", {"disputeId": 1, "resolutionStatus": "Resolved", "resolutionNote": "test"}, "Admin"),
    ("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": 1, "isVerified": True}, "Curator,Admin"),
]


class NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def print_header(title: str) -> None:
    print(f"\n{'=' * 76}\n  {title}\n{'=' * 76}")


def layer1_build_and_static_checks(skip_build: bool = False) -> tuple[bool, list[str]]:
    print_header("LỚP 1: KIỂM TRA BIÊN DỊCH, HẠ TẦNG DOCKER & QUÉT NHÃN UI")
    errors: list[str] = []

    if not skip_build:
        print(f"[1.1] Chạy dotnet build: {WEB_CSPROJ.relative_to(ROOT_DIR)}")
        proc = subprocess.run(
            ["dotnet", "build", str(WEB_CSPROJ), "--no-restore"],
            cwd=str(ROOT_DIR),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if proc.returncode != 0:
            errors.append("dotnet build thất bại (exit code != 0).")
            print(proc.stdout)
            print(proc.stderr)
        else:
            print("  [PASS] dotnet build thành công (0 Errors).")
    else:
        print("[1.1] Bỏ qua dotnet build (--skip-build).")

    print("[1.2] Kiểm tra cấu hình Docker mount & Dockerfile copy database/...")
    compose_txt = (ROOT_DIR / "docker-compose.yml").read_text(encoding="utf-8", errors="ignore")
    dockerfile_txt = (ROOT_DIR / "Dockerfile").read_text(encoding="utf-8", errors="ignore")
    web_section = compose_txt.split("bookshare-web:", 1)[-1] if "bookshare-web:" in compose_txt else ""
    if "./database:/sql:ro" in web_section:
        print("  [PASS] docker-compose.yml đã mount - ./database:/sql:ro vào bookshare-web.")
    else:
        errors.append("docker-compose.yml chưa mount - ./database:/sql:ro vào bookshare-web.")
        print("  [FAIL] Thiếu mount - ./database:/sql:ro trong service bookshare-web.")

    if "database/" in dockerfile_txt:
        print("  [PASS] Dockerfile đã sao chép thư mục database/ vào runtime image.")
    else:
        errors.append("Dockerfile chưa COPY thư mục database/ vào image.")
        print("  [FAIL] Dockerfile chưa COPY database/.")

    print("[1.3] Quét toàn bộ Views/*.cshtml đảm bảo không còn sót nhãn S01-S22 hay Ver07 trên UI...")
    pat = re.compile(r"(?i)(ver07|\bS(?:0[1-9]|1[0-9]|2[0-2])\b)")
    ui_leaks: list[str] = []
    for p in sorted(VIEWS_DIR.rglob("*.cshtml")):
        txt = p.read_text(encoding="utf-8", errors="ignore")
        clean = re.sub(r"<!--.*?-->", "", txt, flags=re.S)
        clean = re.sub(r"@\*.*?\*@", "", clean, flags=re.S)
        clean = re.sub(r"@model\s+.*", "", clean)

        for m in re.finditer(r'(?:placeholder|title|aria-label)="([^"]*)"', clean):
            if pat.search(m.group(1)):
                ui_leaks.append(f"{p.relative_to(ROOT_DIR)} [ATTR]: {m.group(1)}")

        for m in re.finditer(r'ViewData\["Title"\]\s*=\s*"([^"]*)"', clean):
            if pat.search(m.group(1)):
                ui_leaks.append(f"{p.relative_to(ROOT_DIR)} [TITLE]: {m.group(1)}")

        text_only = re.sub(r"<[^>]+>", " ", clean)
        for line in text_only.splitlines():
            s = line.strip()
            if not s or re.match(r"^(@|\{|\}|var\s|if\s|else|foreach|for\s|ViewData|string\s)", s):
                continue
            if pat.search(s):
                ui_leaks.append(f"{p.relative_to(ROOT_DIR)} [TEXT]: {s[:100]}")

    if ui_leaks:
        for leak in ui_leaks:
            print(f"  [FAIL] Sót nhãn UI: {leak}")
            errors.append(f"Sót nhãn UI: {leak}")
    else:
        print("  [PASS] 0 nhãn kỹ thuật S01-S22 / Ver07 sót trên giao diện hiển thị Views/.")

    return len(errors) == 0, errors


def http_request(
    opener: urllib.request.OpenerDirector,
    url: str,
    method: str = "GET",
    headers: dict[str, str] | None = None,
    data: bytes | None = None,
    timeout: float = 15.0,
) -> tuple[int, str, dict[str, str]]:
    req = urllib.request.Request(url, data=data, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with opener.open(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8", errors="replace")
            return resp.status, body, dict(resp.headers.items())
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace") if e.fp else ""
        return e.code, body, dict(e.headers.items()) if e.headers else {}


def wait_for_server(base_url: str, timeout_sec: float = 30.0) -> bool:
    deadline = time.time() + timeout_sec
    opener = urllib.request.build_opener(NoRedirectHandler())
    while time.time() < deadline:
        try:
            code, _, _ = http_request(opener, f"{base_url}/", timeout=3.0)
            if code in (200, 302, 400, 403, 500):
                return True
        except Exception:
            pass
        time.sleep(0.5)
    return False


def layer2_route_health_check(base_url: str) -> tuple[bool, list[str], str]:
    print_header(f"LỚP 2: QUÉT TRANG CHỦ & 22 PHÂN HỆ ({base_url})")
    errors: list[str] = []
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirectHandler())
    csrf_token = ""

    for route in ROUTES_22:
        url = f"{base_url.rstrip('/')}{route}"
        try:
            code, body, _ = http_request(opener, url, method="GET", headers={"Accept": "text/html"})
            has_dev_exception = "An unhandled exception occurred while processing the request" in body
            if code == 200 and not has_dev_exception:
                print(f"  [PASS] GET {route:<26} -> HTTP {code} OK ({len(body):,} bytes)")
                if not csrf_token:
                    m = re.search(r'<meta\s+name="csrf-token"\s+content="([^"]+)"', body)
                    if m:
                        csrf_token = m.group(1)
            else:
                msg = f"GET {route} trả về HTTP {code}" + (" (Unhandled Exception)" if has_dev_exception else "")
                print(f"  [FAIL] {msg}")
                errors.append(msg)
        except Exception as exc:
            msg = f"GET {route} lỗi kết nối: {exc}"
            print(f"  [FAIL] {msg}")
            errors.append(msg)

    return len(errors) == 0, errors, csrf_token


def layer3_rbac_security_check(base_url: str) -> tuple[bool, list[str]]:
    print_header("LỚP 3: KIỂM THỬ BẢO MẬT RBAC & CHỐNG TRUY CẬP TRÁI PHÉP")
    errors: list[str] = []

    # 3.1 Kiểm tra POST không đăng nhập & không CSRF -> phải bị chặn 403 hoặc 400
    opener_anon = urllib.request.build_opener(NoRedirectHandler())
    print("[3.1] Gọi POST API nhạy cảm khi CHƯA đăng nhập (không gửi CSRF):")
    for endpoint, payload, role_req in SENSITIVE_RBAC_ENDPOINTS:
        url = f"{base_url.rstrip('/')}{endpoint}"
        data = json.dumps(payload).encode("utf-8")
        try:
            code, _, _ = http_request(
                opener_anon,
                url,
                method="POST",
                headers={"Accept": "application/json", "Content-Type": "application/json"},
                data=data,
            )
            if code in (403, 400, 401):
                print(f"  [PASS] POST {endpoint:<42} -> HTTP {code} (Chặn đúng chuẩn RBAC [{role_req}])")
            else:
                msg = f"POST {endpoint} không đăng nhập trả về HTTP {code} (Kỳ vọng 403/400)!"
                print(f"  [FAIL] {msg}")
                errors.append(msg)
        except Exception as exc:
            msg = f"POST {endpoint} lỗi: {exc}"
            print(f"  [FAIL] {msg}")
            errors.append(msg)

    # 3.2 Lấy Cookie + CSRF token hợp lệ nhưng vẫn CHƯA đăng nhập -> [Authorize(Roles)] bắt buộc chặn 403 Forbidden!
    print("[3.2] Gọi POST API nhạy cảm CÓ CSRF hợp lệ nhưng CHƯA đăng nhập (kiểm tra khóa [Authorize(Roles)]):")
    jar = http.cookiejar.CookieJar()
    opener_csrf = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirectHandler())
    try:
        _, html, _ = http_request(opener_csrf, f"{base_url.rstrip('/')}/S22AdminAnalytics", method="GET")
        m = re.search(r'<meta\s+name="csrf-token"\s+content="([^"]+)"', html)
        csrf_token = m.group(1) if m else ""
    except Exception:
        csrf_token = ""

    for endpoint, payload, role_req in SENSITIVE_RBAC_ENDPOINTS:
        url = f"{base_url.rstrip('/')}{endpoint}"
        data = json.dumps(payload).encode("utf-8")
        hdrs = {
            "Accept": "application/json",
            "Content-Type": "application/json",
        }
        if csrf_token:
            hdrs["RequestVerificationToken"] = csrf_token
        try:
            code, _, _ = http_request(opener_csrf, url, method="POST", headers=hdrs, data=data)
            if code in (403, 401):
                print(f"  [PASS] POST {endpoint:<42} (có CSRF) -> HTTP {code} Forbidden ([Authorize(Roles={role_req})])")
            else:
                msg = f"POST {endpoint} (có CSRF, chưa login) trả về HTTP {code} (Kỳ vọng 403 Forbidden)!"
                print(f"  [FAIL] {msg}")
                errors.append(msg)
        except Exception as exc:
            msg = f"POST {endpoint} (có CSRF) lỗi: {exc}"
            print(f"  [FAIL] {msg}")
            errors.append(msg)

    return len(errors) == 0, errors


def main() -> int:
    parser = argparse.ArgumentParser(description="BookShare 3-Layer Smoke & RBAC Security Test")
    parser.add_argument("--base-url", default="http://127.0.0.1:5087", help="URL ứng dụng BookShare đang chạy")
    parser.add_argument("--autostart", action="store_true", help="Tự khởi chạy tạm BookShare.Web trong khi test")
    parser.add_argument("--autostart-port", type=int, default=5097, help="Cổng tạm khi dùng --autostart")
    parser.add_argument("--skip-build", action="store_true", help="Bỏ qua bước dotnet build")
    parser.add_argument("--static-only", action="store_true", help="Chỉ chạy Lớp 1 (build + static scan)")
    args = parser.parse_args()

    all_errors: list[str] = []

    ok1, err1 = layer1_build_and_static_checks(skip_build=args.skip_build)
    all_errors.extend(err1)
    if not ok1 and not args.skip_build:
        print("\n[ABORT] Lớp 1 thất bại, dừng kiểm thử HTTP.")
        return 1

    if args.static_only:
        print("\n[DONE] Hoàn tất kiểm thử Lớp 1 (--static-only).")
        return 0 if not all_errors else 1

    server_proc: subprocess.Popen[str] | None = None
    base_url = args.base_url

    try:
        if args.autostart:
            base_url = f"http://127.0.0.1:{args.autostart_port}"
            print(f"\n[AUTOSTART] Đang khởi động tạm BookShare.Web tại {base_url} ...")
            env = os.environ.copy()
            env["ASPNETCORE_URLS"] = base_url
            env["ASPNETCORE_ENVIRONMENT"] = "Development"
            server_proc = subprocess.Popen(
                [
                    "dotnet",
                    "run",
                    "--project",
                    str(WEB_CSPROJ),
                    "--no-build",
                    "--no-launch-profile",
                    "--",
                    "--urls",
                    base_url,
                ],
                cwd=str(WEB_CSPROJ.parent),
                env=env,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            if not wait_for_server(base_url, timeout_sec=35.0):
                all_errors.append(f"Không thể khởi động server tạm tại {base_url} trong 35 giây.")
                print(f"  [FAIL] Server không phản hồi tại {base_url}.")
                server_proc.terminate()
                try:
                    out, _ = server_proc.communicate(timeout=5)
                except subprocess.TimeoutExpired:
                    server_proc.kill()
                    out, _ = server_proc.communicate()
                if out:
                    print("--- Server Output ---")
                    print(out[:2500])
                server_proc = None
                return 1
            print(f"  [READY] Server đã sẵn sàng tại {base_url}!")

        ok2, err2, _ = layer2_route_health_check(base_url)
        all_errors.extend(err2)

        ok3, err3 = layer3_rbac_security_check(base_url)
        all_errors.extend(err3)
    finally:
        if server_proc is not None:
            print("\n[AUTOSTART] Đang tắt tiến trình server tạm...")
            server_proc.terminate()
            try:
                server_proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                server_proc.kill()

    print_header("TỔNG KẾT KIỂM THỬ TỰ ĐỘNG BOOKSHARE")
    if not all_errors:
        print("  ✅ TẤT CẢ 3 LỚP KIỂM THỬ ĐỀU ĐẠT 100% (0 LỖI)!")
        return 0

    print(f"  ❌ PHÁT HIỆN {len(all_errors)} LỖI CẦN XỬ LÝ:")
    for e in all_errors:
        print(f"    - {e}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
