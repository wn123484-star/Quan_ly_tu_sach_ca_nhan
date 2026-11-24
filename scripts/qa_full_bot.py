#!/usr/bin/env python3
"""
BookShare Comprehensive 6-Layer E2E QA Bot (scripts/qa_full_bot.py)
Checks static invariants, Light Mode UI, Guest/Google OAuth fail-closed,
RBAC & cross-user IDOR across all 22 modules, connected multi-module flows,
and final database reset integrity against real ASP.NET Core 10 Kestrel + LocalDB.
"""

from __future__ import annotations

import argparse
import hashlib
import hmac
import html as html_lib
import http.cookiejar
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Optional

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT_DIR = Path(__file__).resolve().parent.parent
WEB_DIR = ROOT_DIR / "src" / "BookShare.Web"

FORBIDDEN_TECH_PATTERNS = [
    (r"\bCRUD\b", "CRUD"),
    (r"\bBookShareDb\b", "BookShareDb"),
    (r"\bSQL Server\b", "SQL Server"),
    (r"\bEF Core\b", "EF Core"),
    (r"\bCookie Auth\b", "Cookie Auth"),
    (r"\bHttpOnly\b", "HttpOnly"),
    (r"\bSameSite\b", "SameSite"),
    (r"\bSHA-256\b", "SHA-256"),
    (r"\bIDOR\b", "IDOR"),
    (r"\bRubric\b", "Rubric"),
    (r"\bDev Console\b", "Dev Console"),
    (r"\bVer07\b", "Ver07"),
    (r"\bbook-tracker-ver07\b", "book-tracker-ver07"),
    (r"\.NET 10\b", ".NET 10"),
    (r"\bHangfire\b", "Hangfire"),
    (r"\bRAG\b", "RAG"),
    (r"\[Authorize", "[Authorize"),
    (r"\bAspNetUsers\b", "AspNetUsers"),
    (r"\bAspNetRoles\b", "AspNetRoles"),
    (r"\bBookCopies\b", "BookCopies"),
    (r"\bLoanRequests\b", "LoanRequests"),
    (r"\bConditionReports\b", "ConditionReports"),
    (r"\bOtpVerifications\b", "OtpVerifications"),
    (r"\bAiChatMessages\b", "AiChatMessages"),
    (r"\bAiChatSessions\b", "AiChatSessions"),
    (r"\bShelfItems\b", "ShelfItems"),
    (r"\bBookNotes\b", "BookNotes"),
    (r"\bReadingProgresses\b", "ReadingProgresses"),
    (r"\bBookShareDailyJobs\b", "BookShareDailyJobs"),
]

COMMERCIAL_FORBIDDEN = [
    r"\bgiá bán\b",
    r"\bmua ngay\b",
    r"\bgiỏ hàng\b",
    r"\bthanh toán mua sách\b",
]

MODULE_GET_ROUTES = [
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


@dataclass
class CheckResult:
    layer: str
    name: str
    passed: bool
    detail: str = ""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


@dataclass
class SessionClient:
    base_url: str
    name: str = "anon"
    jar: http.cookiejar.CookieJar = field(default_factory=http.cookiejar.CookieJar)
    csrf_token: str = ""

    def __post_init__(self) -> None:
        self._opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar)
        )
        self._no_redirect_opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar),
            NoRedirect(),
        )

    def _extract_csrf(self, html: str) -> None:
        m = re.search(
            r'<meta\s+name="csrf-token"\s+content="([^"]+)"', html, re.IGNORECASE
        )
        if m:
            self.csrf_token = m.group(1)
            return
        m = re.search(
            r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',
            html,
            re.IGNORECASE,
        )
        if m:
            self.csrf_token = m.group(1)

    def get(self, path: str, accept_json: bool = False) -> tuple[int, str, dict[str, str]]:
        url = self.base_url + path
        headers = {
            "Accept": "application/json" if accept_json else "text/html,application/xhtml+xml"
        }
        req = urllib.request.Request(url, headers=headers, method="GET")
        try:
            with self._opener.open(req, timeout=20) as resp:
                body = resp.read().decode("utf-8", errors="replace")
                if not accept_json:
                    self._extract_csrf(body)
                return resp.status, body, dict(resp.headers.items())
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            if not accept_json:
                self._extract_csrf(body)
            return e.code, body, dict(e.headers.items())

    def refresh_csrf(self, path: str = "/S01AuthOtpGoogle") -> str:
        self.get(path)
        return self.csrf_token

    def post_json(
        self, path: str, payload: dict[str, Any], ensure_csrf: bool = True
    ) -> tuple[int, Any, str]:
        if ensure_csrf and not self.csrf_token:
            self.refresh_csrf("/")
        url = self.base_url + path
        data = json.dumps(payload).encode("utf-8")
        headers = {
            "Content-Type": "application/json; charset=utf-8",
            "Accept": "application/json",
            "RequestVerificationToken": self.csrf_token,
        }
        req = urllib.request.Request(url, data=data, headers=headers, method="POST")
        try:
            with self._no_redirect_opener.open(req, timeout=25) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                try:
                    return resp.status, json.loads(raw), raw
                except Exception:
                    return resp.status, None, raw
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", errors="replace")
            try:
                return e.code, json.loads(raw), raw
            except Exception:
                return e.code, None, raw

    def post_form(
        self,
        path: str,
        fields: dict[str, str],
        follow_redirects: bool = True,
        accept_json: bool = False,
    ) -> tuple[int, str, dict[str, str]]:
        if not self.csrf_token:
            self.refresh_csrf("/S01AuthOtpGoogle")
        form_data = dict(fields)
        form_data["__RequestVerificationToken"] = self.csrf_token
        encoded = urllib.parse.urlencode(form_data).encode("utf-8")
        headers = {
            "Content-Type": "application/x-www-form-urlencoded",
            "Accept": "application/json" if accept_json else "text/html,application/xhtml+xml",
            "RequestVerificationToken": self.csrf_token,
        }
        opener = self._opener if follow_redirects else self._no_redirect_opener
        req = urllib.request.Request(
            self.base_url + path, data=encoded, headers=headers, method="POST"
        )
        try:
            with opener.open(req, timeout=25) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                if not accept_json:
                    self._extract_csrf(raw)
                return resp.status, raw, dict(resp.headers.items())
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", errors="replace")
            if not accept_json:
                self._extract_csrf(raw)
            return e.code, raw, dict(e.headers.items())

    def post_multipart(
        self,
        path: str,
        fields: dict[str, str],
        file_field: Optional[tuple[str, str, bytes, str]] = None,
    ) -> tuple[int, Any, str]:
        if not self.csrf_token:
            self.refresh_csrf("/S10ConditionReport")
        boundary = f"----BookShareBoundary{int(time.time() * 1000)}"
        chunks: list[bytes] = []
        all_fields = dict(fields)
        all_fields["__RequestVerificationToken"] = self.csrf_token
        for k, v in all_fields.items():
            chunks.append(f"--{boundary}\r\n".encode())
            chunks.append(
                f'Content-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode(
                    "utf-8"
                )
            )
        if file_field:
            fname_key, filename, content_bytes, ctype = file_field
            chunks.append(f"--{boundary}\r\n".encode())
            chunks.append(
                f'Content-Disposition: form-data; name="{fname_key}"; filename="{filename}"\r\n'
                f"Content-Type: {ctype}\r\n\r\n".encode("utf-8")
            )
            chunks.append(content_bytes + b"\r\n")
        chunks.append(f"--{boundary}--\r\n".encode())
        body = b"".join(chunks)

        headers = {
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Accept": "application/json",
            "RequestVerificationToken": self.csrf_token,
        }
        req = urllib.request.Request(
            self.base_url + path, data=body, headers=headers, method="POST"
        )
        try:
            with self._no_redirect_opener.open(req, timeout=25) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                try:
                    return resp.status, json.loads(raw), raw
                except Exception:
                    return resp.status, None, raw
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", errors="replace")
            try:
                return e.code, json.loads(raw), raw
            except Exception:
                return e.code, None, raw

    def login(self, email: str, password: str) -> tuple[bool, str]:
        self.refresh_csrf("/S01AuthOtpGoogle")
        code, html, _ = self.post_form(
            "/S01AuthOtpGoogle/Login",
            {"Email": email, "Password": password, "RememberMe": "true"},
            follow_redirects=True,
        )
        self.refresh_csrf("/S01AuthOtpGoogle")
        ok = code == 200 and ('id="notificationBell"' in html or "Đăng xuất" in html)
        return ok, self._extract_flash(html)

    def logout(self) -> None:
        self.refresh_csrf("/S01AuthOtpGoogle")
        self.post_form("/S01AuthOtpGoogle/Logout", {}, follow_redirects=True)
        self.refresh_csrf("/S01AuthOtpGoogle")

    @staticmethod
    def _extract_flash(html: str) -> str:
        t = re.search(r'id="statusTitleText"[^>]*>(.*?)</strong>', html, re.DOTALL)
        d = re.search(r'id="statusDetailText"[^>]*>(.*?)</span>', html, re.DOTALL)
        title = html_lib.unescape(re.sub(r"<[^>]+>", "", t.group(1))).strip() if t else ""
        detail = html_lib.unescape(re.sub(r"<[^>]+>", "", d.group(1))).strip() if d else ""
        return f"{title} | {detail}".strip(" |")


class QARunner:
    def __init__(self, base_url: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.results: list[CheckResult] = []

    def check(self, layer: str, name: str, condition: bool, detail: str = "") -> bool:
        self.results.append(CheckResult(layer, name, bool(condition), detail))
        icon = "PASS" if condition else "FAIL"
        msg = f"  [{icon}] {layer} :: {name}"
        if detail and not condition:
            msg += f" -> {detail}"
        print(msg)
        return bool(condition)

    @staticmethod
    def strip_non_visible_html(html: str) -> str:
        cleaned = re.sub(r"<!--.*?-->", " ", html, flags=re.DOTALL)
        cleaned = re.sub(r"<script\b[^>]*>.*?</script>", " ", cleaned, flags=re.DOTALL | re.IGNORECASE)
        cleaned = re.sub(r"<style\b[^>]*>.*?</style>", " ", cleaned, flags=re.DOTALL | re.IGNORECASE)
        return cleaned

    def run_layer1_static(self) -> None:
        print("\n=== LAYER 1: STATIC ARCHITECTURE, LIGHT THEME & ASSET AUDIT ===")
        unused = [
            WEB_DIR / "wwwroot" / "css" / "site.css",
            WEB_DIR / "wwwroot" / "js" / "site.js",
            WEB_DIR / "Views" / "Shared" / "_Layout.cshtml.css",
        ]
        self.check(
            "L1",
            "Unused template files deleted",
            all(not p.exists() for p in unused),
            ", ".join(str(p.name) for p in unused if p.exists()),
        )

        ctrl_files = list((WEB_DIR / "Controllers").glob("*.cs"))
        ctrl_dbcontext = [
            p.name for p in ctrl_files if "BookShareDbContext" in p.read_text(encoding="utf-8")
        ]
        self.check(
            "L1",
            "Strict Service Layer (0 BookShareDbContext in Controllers)",
            len(ctrl_dbcontext) == 0,
            str(ctrl_dbcontext),
        )

        code_files = ctrl_files + list((WEB_DIR / "Services" / "Implementations").glob("*.cs"))
        fallback_hits = []
        for p in code_files:
            txt = p.read_text(encoding="utf-8")
            if re.search(r'\?\?\s*"u-|u-lib-01', txt):
                fallback_hits.append(p.name)
        self.check(
            "L1",
            "Zero hardcoded fallback user IDs in Controllers/Services",
            len(fallback_hits) == 0,
            str(fallback_hits),
        )

        layout = (WEB_DIR / "Views" / "Shared" / "_Layout.cshtml").read_text(encoding="utf-8")
        self.check(
            "L1",
            "_Layout.cshtml defaults to Light Mode without prefers-color-scheme auto-dark",
            'data-bs-theme="light"' in layout and "prefers-color-scheme: dark" not in layout,
        )

        core_css = (WEB_DIR / "wwwroot" / "css" / "core-theme.css").read_text(encoding="utf-8")
        splash_m = re.search(r"\.app-splash\s*\{([^}]*)\}", core_css)
        splash_block = splash_m.group(1) if splash_m else ""
        self.check(
            "L1",
            "Splash screen uses light background (#f8fafc)",
            "#f8fafc" in splash_block and "#0f172a" in splash_block,
        )

        manifest = (WEB_DIR / "wwwroot" / "manifest.json").read_text(encoding="utf-8")
        self.check("L1", "manifest.json uses light background_color #f8fafc", "#f8fafc" in manifest)

        sw = (WEB_DIR / "wwwroot" / "service-worker.js").read_text(encoding="utf-8")
        self.check(
            "L1",
            "service-worker.js bumped to bookshare-static-v2 & light offline card",
            "bookshare-static-v2" in sw and "#f8fafc" in sw,
        )

        s01_js = (
            WEB_DIR / "wwwroot" / "modules" / "s01-auth-otp-google" / "s01-auth-otp-google.js"
        ).read_text(encoding="utf-8")
        self.check(
            "L1",
            "s01-auth-otp-google.js has zero demo quick-fill handlers or passwords",
            "Admin@123" not in s01_js and "js-quick-fill" not in s01_js and "BookShare@123" not in s01_js,
        )

    def _audit_rendered_page(self, layer: str, route: str, html: str) -> None:
        self.check(
            layer,
            f"HTML Light Mode default on {route}",
            'data-bs-theme="light"' in html,
            "missing data-bs-theme='light'",
        )

        # Strip razor controller/action attribute values before scanning visible text
        vis = self.strip_non_visible_html(html)
        # Strip attributes that legitimately hold route names / JSON data / IDs (e.g. asp-/href/action/id)
        vis_no_urls = re.sub(r'(?:href|action|src|id|for|name|data-[a-z-]+)="[^"]*"', "", vis, flags=re.IGNORECASE)
        # Strip S04 route controller strings inside script data/href if any left
        hits = []
        for pat, label in FORBIDDEN_TECH_PATTERNS:
            if re.search(pat, vis_no_urls):
                hits.append(label)
        self.check(
            layer,
            f"No forbidden tech jargon on {route}",
            len(hits) == 0,
            f"Found: {hits}",
        )

        comm_hits = [p for p in COMMERCIAL_FORBIDDEN if re.search(p, vis_no_urls, re.IGNORECASE)]
        self.check(
            layer,
            f"Non-commercial check on {route}",
            len(comm_hits) == 0,
            f"Found commercial wording: {comm_hits}",
        )

        bad_imgs = []
        for m in re.finditer(r"<img\b[^>]*>", vis, re.IGNORECASE):
            tag = m.group(0)
            if not re.search(r'\balt\s*=', tag, re.IGNORECASE) or not re.search(
                r'\bonerror\s*=', tag, re.IGNORECASE
            ):
                bad_imgs.append(tag[:80])
        self.check(
            layer,
            f"All <img> tags have alt & onerror on {route}",
            len(bad_imgs) == 0,
            str(bad_imgs),
        )

    def run_layer2_guest_and_oauth(self) -> None:
        print("\n=== LAYER 2: GUEST PAGES, UI INTEGRITY & GOOGLE OAUTH FAIL-CLOSED ===")
        guest = SessionClient(self.base_url, "guest")

        for route in MODULE_GET_ROUTES:
            code, html, _ = guest.get(route)
            self.check("L2", f"GET {route} returns 200", code == 200, f"got {code}")
            if code == 200:
                self._audit_rendered_page("L2", route, html)

        # S01 UI clean check
        _, s01_html, _ = guest.get("/S01AuthOtpGoogle")
        self.check(
            "L2",
            "S01 hides demo accounts, demo passwords, SelectedRole, tabOtpResend & tabProfile",
            all(
                tok not in s01_html
                for tok in [
                    "Admin@123",
                    "Librarian@123",
                    "Curator@123",
                    "User@123",
                    "BookShare@123",
                    'name="SelectedRole"',
                    "tabOtpResend",
                    'id="tabProfile"',
                ]
            ),
        )
        self.check(
            "L2",
            "S01 disables Google OAuth button when keys are not configured",
            re.search(r'<button[^>]*class="s01-google-btn"[^>]*disabled', s01_html) is not None,
        )

        # S21 / S22 PII non-leak for guest
        for r in ("/S21LibrarianDesk", "/S22AdminAnalytics"):
            _, phtml, _ = guest.get(r)
            leaked = [
                e for e in ["nguyenhung@cmc.edu.vn", "thuthu@cmc.edu.vn", "clbsach@cmc.edu.vn"] if e in phtml
            ]
            self.check("L2", f"Guest GET {r} hides PII & admin controls", len(leaked) == 0, str(leaked))

        # GoogleLogin fail-closed without real OAuth keys
        guest.refresh_csrf("/S01AuthOtpGoogle")
        gcode, ghtml, _ = guest.post_form(
            "/S01AuthOtpGoogle/GoogleLogin", {}, follow_redirects=False
        )
        self.check(
            "L2",
            "POST GoogleLogin fails closed HTTP 400 when unconfigured",
            gcode == 400 and "Đăng xuất" not in ghtml,
            f"code={gcode}",
        )
        gcode_json, graw_json, _ = guest.post_form(
            "/S01AuthOtpGoogle/GoogleLogin", {}, follow_redirects=False, accept_json=True
        )
        try:
            gjson = json.loads(graw_json)
        except Exception:
            gjson = None
        self.check(
            "L2",
            "POST GoogleLogin JSON returns 400 with explicit Vietnamese error",
            gcode_json == 400 and isinstance(gjson, dict) and "Google OAuth 2.0" in gjson.get("message", ""),
            f"code={gcode_json}, body={graw_json}",
        )

        # Guest QuickAiAsk behavior
        qcode1, qjson1, _ = guest.post_json("/api/ai-chat/quick-ask", {"message": "Kiểm tra hạn trả của mình"})
        self.check(
            "L2",
            "Guest QuickAiAsk prompts login for personal loan question",
            qcode1 == 200 and "đăng nhập" in (qjson1 or {}).get("reply", "").lower(),
            f"code={qcode1}, reply={qjson1}",
        )
        qcode2, qjson2, _ = guest.post_json("/api/ai-chat/quick-ask", {"message": "Gợi ý sách sẵn sàng cho mượn"})
        qreply2 = (qjson2 or {}).get("reply", "")
        self.check(
            "L2",
            "Guest QuickAiAsk returns public catalog books cleanly",
            qcode2 == 200 and "Mã vạch:" in qreply2 and "0đ" in qreply2,
            f"code={qcode2}, reply={qreply2[:120]}",
        )

        # Guest POST to protected endpoints -> 401
        guest_write_endpoints = [
            ("/S02PersonalShelf/CreateShelf", {"name": "HackShelf"}),
            ("/S03ReadingTracker/UpdateProgress", {"progressId": 1, "currentPage": 10}),
            ("/S04BookNotesQuotes/SaveNote", {"progressId": 1, "pageNumber": 5, "noteContent": "x"}),
            ("/S05IsbnAutoCatalog/SaveBookAndCopy", {"isbn": "9786048045562", "title": "x", "author": "y"}),
            ("/S06SearchDiscovery/QuickRequestLoan", {"bookCopyId": 1, "desiredDays": 7}),
            ("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": 1, "desiredDays": 7}),
            ("/S09P2PLoanFlow/CreateRequest", {"bookCopyId": 1, "desiredDays": 7}),
            ("/S11HotBookQueue/Enqueue", {"bookId": 1}),
            ("/S12LoanRenewalFine/RenewLoan", {"loanId": 1}),
            ("/S13GeminiAiAssistant/CreateSession", {"title": "x"}),
            ("/S14AiRecommendation/GenerateCustomAdvice", {"mood": "DeepFocus", "skillGoal": "Go"}),
            ("/S15TrustScoreReview/SubmitReview", {"targetType": "Book", "targetId": "1", "rating": 5, "comment": "ok"}),
            ("/S16MembershipTier/SwitchMembership", {"targetTier": "Premium"}),
            ("/S17SandboxPayment/CreateOrder", {"transactionType": "SimulatedDeposit", "amount": 50000}),
            ("/S18NotificationHub/MarkAllRead", {}),
            ("/S19DisputeResolution/CreateDispute", {"loanId": 1, "reason": "x"}),
            ("/S20ExchangeHubClub/RegisterDropoff", {"bookCopyId": 1, "hubId": 3}),
            ("/S22AdminAnalytics/ManageSampleData", {"actionMode": "ResetTransactions"}),
        ]
        for ep, body in guest_write_endpoints:
            c, _, _ = guest.post_json(ep, body)
            self.check("L2", f"Guest POST {ep} blocked with 401", c == 401, f"got {c}")

    def run_layer3_auth_rbac_idor(self) -> dict[str, SessionClient]:
        print("\n=== LAYER 3: SEED RESET, AUTH HARDENING, 4-ROLE RBAC & IDOR ===")

        # 1. Admin reset DB first
        admin = SessionClient(self.base_url, "admin")
        ok, flash = admin.login("admin@bookshare.vn", "Admin@123")
        self.check("L3", "Admin login with Admin@123 succeeds", ok, flash)

        sc, sj, sraw = admin.post_json("/S22AdminAnalytics/ManageSampleData", {"actionMode": "SeedSampleData"})
        self.check("L3", "Admin SeedSampleData succeeds", sc == 200, f"code={sc} body={sraw}")

        # Verify all 4 roles log in cleanly with synced seed passwords
        lib = SessionClient(self.base_url, "librarian")
        ok_lib, fl_lib = lib.login("thuthu@cmc.edu.vn", "Librarian@123")
        self.check("L3", "Librarian login with Librarian@123 succeeds", ok_lib, fl_lib)

        cur = SessionClient(self.base_url, "curator")
        ok_cur, fl_cur = cur.login("clbsach@cmc.edu.vn", "Curator@123")
        self.check("L3", "Curator login with Curator@123 succeeds", ok_cur, fl_cur)

        u1 = SessionClient(self.base_url, "u-user-01")
        ok_u1, fl_u1 = u1.login("nguyenhung@cmc.edu.vn", "User@123")
        self.check("L3", "User1 login with User@123 succeeds", ok_u1, fl_u1)

        u2 = SessionClient(self.base_url, "u-user-02")
        ok_u2, fl_u2 = u2.login("minhanh@cmc.edu.vn", "User@123")
        self.check("L3", "User2 login with User@123 succeeds", ok_u2, fl_u2)

        u3 = SessionClient(self.base_url, "u-user-03")
        ok_u3, fl_u3 = u3.login("hoangnam@cmc.edu.vn", "User@123")
        self.check("L3", "User3 login with User@123 succeeds", ok_u3, fl_u3)

        # 2. Public Register role escalation prevention & weak password rejection
        ts = int(time.time())
        reg_client = SessionClient(self.base_url, "reg_spoof")
        reg_client.refresh_csrf("/S01AuthOtpGoogle")
        _, weak_html, _ = reg_client.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Short Pass", "Email": f"short{ts}@bookshare.local", "Password": "123"},
        )
        self.check("L3", "Register rejects password < 6 chars", 'id="notificationBell"' not in weak_html)

        spoof_email = f"spoof_admin_{ts}@bookshare.local"
        rc, rhtml, _ = reg_client.post_form(
            "/S01AuthOtpGoogle/Register",
            {
                "FullName": "Public Reader",
                "Email": spoof_email,
                "Password": "UserPass@123",
                "SelectedRole": "Admin",
            },
        )
        self.check("L3", "Public Register signs in new reader", rc == 200 and 'id="notificationBell"' in rhtml)
        reg_client.refresh_csrf("/S01AuthOtpGoogle")
        adm_probe, _, _ = reg_client.get("/S22AdminAnalytics/ChartData", accept_json=True)
        self.check("L3", "Public Register ignores SelectedRole=Admin (403 on Admin API)", adm_probe == 403, f"got {adm_probe}")

        # Verify /S21 and /S22 HTML views for regular User do not leak PII or expose admin panel
        _, s21_u, _ = u1.get("/S21LibrarianDesk")
        _, s22_u, _ = u1.get("/S22AdminAnalytics")
        self.check(
            "L3",
            "Regular User GET /S21 & /S22 hides operational PII & renders clean access notice",
            "thuthu@cmc.edu.vn" not in s21_u and "minhanh@cmc.edu.vn" not in s22_u and "Quản Trị" in s22_u,
        )

        # 3. Admin Password change -> old password MUST fail (no hardcoded backdoor!)
        admin.refresh_csrf("/S01AuthOtpGoogle")
        cp1, cpj1, _ = admin.post_json(
            "/S01AuthOtpGoogle/ChangePassword",
            {"currentPassword": "Admin@123", "newPassword": "AdminNew@987"},
        )
        self.check("L3", "Admin changes password to AdminNew@987", cp1 == 200, str(cpj1))

        adm_test = SessionClient(self.base_url, "adm_probe")
        old_ok, _ = adm_test.login("admin@bookshare.vn", "Admin@123")
        self.check("L3", "Old password Admin@123 rejected after Admin password change (no backdoor)", not old_ok)
        new_ok, _ = adm_test.login("admin@bookshare.vn", "AdminNew@987")
        self.check("L3", "New Admin password AdminNew@987 succeeds", new_ok)

        # Restore Admin@123 cleanly
        cp2, _, _ = adm_test.post_json(
            "/S01AuthOtpGoogle/ChangePassword",
            {"currentPassword": "AdminNew@987", "newPassword": "Admin@123"},
        )
        self.check("L3", "Admin password restored to Admin@123", cp2 == 200)
        admin.login("admin@bookshare.vn", "Admin@123")

        # 4. Lockout enforcement on spoof_email user
        _, s22_adm_html, _ = admin.get("/S22AdminAnalytics")
        spoof_uid = ""
        for tr_blk in s22_adm_html.split("<tr"):
            if spoof_email in tr_blk:
                m_u = re.search(r'data-user-id="([^"]+)"', tr_blk)
                if m_u:
                    spoof_uid = m_u.group(1)
                    break
        self.check("L3", "Locate newly registered user ID in S22 table", bool(spoof_uid))

        if spoof_uid:
            lk_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": spoof_uid, "lockAccount": True})
            self.check("L3", "Admin locks test account", lk_c == 200)
            locked_probe = SessionClient(self.base_url, "locked_probe")
            lk_ok, lk_msg = locked_probe.login(spoof_email, "UserPass@123")
            self.check("L3", "Locked account rejected on Login", not lk_ok and "tạm khóa" in lk_msg.lower(), lk_msg)
            ulk_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": spoof_uid, "lockAccount": False})
            self.check("L3", "Admin unlocks test account", ulk_c == 200)
            ulk_ok, _ = locked_probe.login(spoof_email, "UserPass@123")
            self.check("L3", "Unlocked account can log in again", ulk_ok)

        # 5. Full RBAC 403 matrix
        rbac_user_forbidden = [
            ("/S05IsbnAutoCatalog/UpdateCatalogBook", {"bookId": 1, "title": "Hack", "author": "X", "pageCount": 100, "categoryId": 1}),
            ("/S05IsbnAutoCatalog/DeleteCatalogBook", {"bookId": 1}),
            ("/S07LibraryCatalog/UpdateShelfLocation", {"bookCopyId": 1, "shelfLocation": "Hack"}),
            ("/S08LibraryLoanFlow/ApproveOrReject", {"loanRequestId": 1, "approve": True}),
            ("/S08LibraryLoanFlow/ConfirmHandover", {"loanRequestId": 1}),
            ("/S08LibraryLoanFlow/ConfirmReturn", {"loanRequestId": 1, "returnCondition": "Good"}),
            ("/S11HotBookQueue/NotifyTop", {"bookId": 1}),
            ("/S12LoanRenewalFine/AssessFine", {"loanId": 1, "simulatedOverdueDays": 1}),
            ("/S17SandboxPayment/RefundDeposit", {"transactionId": 1}),
            ("/S18NotificationHub/TriggerResendEmail", {"campaignType": "DueReminder"}),
            ("/S19DisputeResolution/ResolveDispute", {"disputeId": 1, "decisionStatus": "Resolved", "trustScorePenalty": 5}),
            ("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": 1, "hubId": 3}),
            ("/S20ExchangeHubClub/ProcessHubHandover", {"loanRequestId": 1, "actionType": "CheckInDropoff"}),
            ("/S20ExchangeHubClub/UpdateHubStation", {"hubId": 3, "name": "H"}),
            ("/S21LibrarianDesk/QuickScanBarcode", {"barcode": "BS-0001"}),
            ("/S21LibrarianDesk/BatchImportCopies", {"bookId": 1, "libraryId": 1, "quantity": 1}),
            ("/S21LibrarianDesk/UpdateCopyState", {"bookCopyId": 1, "newStatus": "Damaged"}),
            ("/S21LibrarianDesk/DeleteSystemCopy", {"bookCopyId": 1}),
            ("/S22AdminAnalytics/ChangeUserRole", {"userId": "u-user-01", "newRole": "Admin"}),
            ("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-user-01", "lockAccount": True}),
            ("/S22AdminAnalytics/TriggerDailyJob", {"jobKey": "AllDailyJobs"}),
            ("/S22AdminAnalytics/ManageSampleData", {"actionMode": "ResetTransactions"}),
        ]
        for ep, payload in rbac_user_forbidden:
            c, _, raw = u1.post_json(ep, payload)
            self.check("L3", f"RBAC User blocked 403 on {ep}", c == 403, f"got {c}: {raw[:120]}")

        for ep, payload in [
            ("/S05IsbnAutoCatalog/DeleteCatalogBook", {"bookId": 1}),
            ("/S11HotBookQueue/NotifyTop", {"bookId": 1}),
            ("/S21LibrarianDesk/QuickScanBarcode", {"barcode": "BS-0001"}),
            ("/S22AdminAnalytics/ManageSampleData", {"actionMode": "ResetTransactions"}),
        ]:
            c, _, _ = cur.post_json(ep, payload)
            self.check("L3", f"RBAC Curator blocked 403 on {ep}", c == 403, f"got {c}")

        for ep, payload in [
            ("/S19DisputeResolution/ResolveDispute", {"disputeId": 1, "decisionStatus": "Resolved"}),
            ("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": 1, "hubId": 3}),
            ("/S22AdminAnalytics/ManageSampleData", {"actionMode": "ResetTransactions"}),
        ]:
            c, _, _ = lib.post_json(ep, payload)
            self.check("L3", f"RBAC Librarian blocked 403 on {ep}", c == 403, f"got {c}")

        # 6. Cross-User IDOR checks using freshly created u1 & u2 items + seed records
        # u2 tries reading/mutating u1's shelf/copy/progress/notes/notifications
        _, u1_s02_html, _ = u1.get("/S02PersonalShelf")
        u1_shelf_m = re.search(r'data-shelf-id="(\d+)"', u1_s02_html)
        u1_copy_m = re.search(r'data-copy-id="(\d+)"', u1_s02_html)
        u1_shelf_id = int(u1_shelf_m.group(1)) if u1_shelf_m else 1
        u1_copy_id = int(u1_copy_m.group(1)) if u1_copy_m else 1

        _, u1_s03_html, _ = u1.get("/S03ReadingTracker")
        u1_prog_m = re.search(r'data-progress-id="(\d+)"', u1_s03_html)
        u1_prog_id = int(u1_prog_m.group(1)) if u1_prog_m else 1

        _, u1_s04_html, _ = u1.get("/S04BookNotesQuotes")
        u1_note_m = re.search(r'data-note-id="(\d+)"', u1_s04_html)
        u1_note_id = int(u1_note_m.group(1)) if u1_note_m else 1

        _, u1_s18_html, _ = u1.get("/S18NotificationHub")
        u1_notif_m = re.search(r'data-id="(\d+)"', u1_s18_html)
        u1_notif_id = int(u1_notif_m.group(1)) if u1_notif_m else 0

        idor_checks = [
            (u2, "/S02PersonalShelf/UpdateShelf", {"shelfId": u1_shelf_id, "name": "Hacked", "isPublic": True}),
            (u2, "/S02PersonalShelf/DeleteShelf", {"shelfId": u1_shelf_id}),
            (u2, "/S02PersonalShelf/ToggleLendable", {"bookCopyId": u1_copy_id, "isLendable": False}),
            (u2, "/S02PersonalShelf/UpdateCopy", {"bookCopyId": u1_copy_id, "condition": "Worn"}),
            (u2, "/S03ReadingTracker/UpdateProgress", {"progressId": u1_prog_id, "currentPage": 1}),
            (u2, "/S03ReadingTracker/DeleteProgress", {"progressId": u1_prog_id}),
            (u2, "/S04BookNotesQuotes/DeleteNote", {"noteId": u1_note_id}),
            (u2, "/S20ExchangeHubClub/RegisterDropoff", {"bookCopyId": u1_copy_id, "hubId": 3}),
        ]
        if u1_notif_id:
            idor_checks.append((u2, "/S18NotificationHub/MarkRead", {"notificationId": u1_notif_id}))
            idor_checks.append((u2, "/S18NotificationHub/DeleteNotification", {"notificationId": u1_notif_id}))

        for attacker, ep, body in idor_checks:
            c, _, raw = attacker.post_json(ep, body)
            self.check("L3", f"Cross-user IDOR blocked 403 on {ep}", c == 403, f"got {c}: {raw[:120]}")

        return {
            "admin": admin,
            "librarian": lib,
            "curator": cur,
            "u1": u1,
            "u2": u2,
            "u3": u3,
            "spoof": reg_client,
        }

    def run_layer4_business_loops(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 4: CONNECTED END-TO-END BUSINESS WORKFLOWS (S02 -> S22) ===")
        admin = actors["admin"]
        lib = actors["librarian"]
        cur = actors["curator"]
        u1 = actors["u1"]
        u2 = actors["u2"]
        u3 = actors["u3"]

        # ------------------------------------------------------------------
        # FLOW A: S05 -> S02 -> S06 -> S09 (Double Approve Lock) -> S10 -> S15
        # ------------------------------------------------------------------
        lk_c, lk_j, _ = u1.post_json("/S05IsbnAutoCatalog/LookupIsbn", {"isbn": "9786048045562"})
        self.check("L4", "S05 Lookup curated ISBN 9786048045562", lk_c == 200 and (lk_j or {}).get("data", {}).get("found") is True)

        # User tries TargetOwnerType='System' -> rejected with 403 Forbidden
        sv_sys_c, _, sv_sys_raw = u1.post_json(
            "/S05IsbnAutoCatalog/SaveBookAndCopy",
            {
                "isbn": "9780135957059",
                "title": "The Pragmatic Programmer E2E",
                "author": "David Thomas",
                "pageCount": 352,
                "categoryId": 1,
                "targetOwnerType": "System",
                "condition": "New",
                "isLendable": True,
            },
        )
        self.check(
            "L4",
            "S05 Regular user blocked from creating System copy (403)",
            sv_sys_c == 403,
            f"code={sv_sys_c}, body={sv_sys_raw}",
        )

        # User creates personal P2P copy cleanly with TargetOwnerType='User'
        sv_c, sv_j, sv_raw = u1.post_json(
            "/S05IsbnAutoCatalog/SaveBookAndCopy",
            {
                "isbn": "9780135957059",
                "title": "The Pragmatic Programmer E2E",
                "author": "David Thomas",
                "pageCount": 352,
                "categoryId": 1,
                "targetOwnerType": "User",
                "condition": "New",
                "isLendable": True,
            },
        )
        new_barcode = (sv_j or {}).get("barcode", "")
        prag_book_id_from_save = (sv_j or {}).get("bookId", 0)
        self.check("L4", "S05 User SaveBookAndCopy succeeds & assigns barcode", sv_c == 200 and new_barcode.startswith("BS-"), sv_raw)

        # Verify copy lives in u1's personal shelf (NOT System desk!)
        _, u1_shelf_html, _ = u1.get("/S02PersonalShelf")
        self.check("L4", "S05 forced TargetOwnerType='User' appears on S02 Personal Shelf", new_barcode in u1_shelf_html)
        copy_m = re.search(rf'data-copy-id="(\d+)"[^>]*data-barcode="{re.escape(new_barcode)}"', u1_shelf_html)
        self.check("L4", "Extract new BookCopyId on S02", copy_m is not None)
        flow_copy_id = int(copy_m.group(1)) if copy_m else 0

        # Also check SystemCopies desk doesn't contain new_barcode
        _, desk_html, _ = lib.get("/S21LibrarianDesk")
        self.check("L4", "User-created copy NOT injected into System Inventory (S21)", new_barcode not in desk_html)

        # Toggle IsLendable off -> u2 borrow fails -> toggle back on -> u2 & u3 borrow
        t1_c, _, _ = u1.post_json("/S02PersonalShelf/ToggleLendable", {"bookCopyId": flow_copy_id, "isLendable": False})
        self.check("L4", "S02 Toggle IsLendable = false", t1_c == 200)
        b_fail_c, _, _ = u2.post_json("/S09P2PLoanFlow/CreateRequest", {"bookCopyId": flow_copy_id, "pickupHubId": 3, "desiredDays": 7})
        self.check("L4", "Borrowing copy with IsLendable=false rejected", b_fail_c == 400)
        t2_c, _, _ = u1.post_json("/S02PersonalShelf/ToggleLendable", {"bookCopyId": flow_copy_id, "isLendable": True})
        self.check("L4", "S02 Toggle IsLendable = true", t2_c == 200)

        # u2 and spoof (registered clean Member) both submit P2P request for flow_copy_id
        spoof = actors["spoof"]
        req2_c, req2_j, req2_raw = u2.post_json("/S09P2PLoanFlow/CreateRequest", {"bookCopyId": flow_copy_id, "pickupHubId": 3, "desiredDays": 7, "requestNote": "E2E P2P U2"})
        req3_c, req3_j, req3_raw = spoof.post_json("/S09P2PLoanFlow/CreateRequest", {"bookCopyId": flow_copy_id, "pickupHubId": 3, "desiredDays": 7, "requestNote": "E2E P2P U3"})
        self.check("L4", "S09 u2 & spoof create pending P2P requests", req2_c == 200 and req3_c == 200, f"u2={req2_c}:{req2_raw} spoof={req3_c}:{req3_raw}")

        # Extract RequestIds from S09 as u1 (owner)
        _, s09_u1, _ = u1.get("/S09P2PLoanFlow")
        s09_u1_txt = html_lib.unescape(s09_u1)
        req2_id = req3_id = 0
        for blk in s09_u1_txt.split('s09-p2p-card'):
            if new_barcode in blk and "js-s09-approve" in blk:
                m = re.search(r'data-req-id="(\d+)"', blk)
                if m and "Trần Minh Anh" in blk:
                    req2_id = int(m.group(1))
                elif m and "Public Reader" in blk:
                    req3_id = int(m.group(1))
        self.check("L4", "Locate both pending P2P request IDs on S09", req2_id > 0 and req3_id > 0, f"req2={req2_id}, req3={req3_id}")

        # IDOR: u3 tries approving u2's P2P request -> 403
        idor_p2p_c, _, _ = u3.post_json("/S09P2PLoanFlow/ApproveOrReject", {"loanRequestId": req2_id, "approve": True})
        self.check("L4", "IDOR non-owner cannot approve P2P request (403)", idor_p2p_c == 403, f"got {idor_p2p_c}")

        # Owner u1 approves req2 -> succeeds
        ap2_c, _, ap2_raw = u1.post_json("/S09P2PLoanFlow/ApproveOrReject", {"loanRequestId": req2_id, "approve": True})
        self.check("L4", "Owner approves req2", ap2_c == 200, ap2_raw)

        # Double approve guard: Owner tries approving req3 on same reserved copy -> blocked 400!
        ap3_c, ap3_j, _ = u1.post_json("/S09P2PLoanFlow/ApproveOrReject", {"loanRequestId": req3_id, "approve": True})
        self.check("L4", "Double-approve guard blocks second P2P approval", ap3_c == 400, str(ap3_j))

        # Curator advances req2 -> InTransit -> HandoverBorrowed
        st1_c, _, st1_raw = cur.post_json("/S09P2PLoanFlow/AdvanceStage", {"loanRequestId": req2_id, "stage": "InTransit"})
        self.check("L4", "Curator advances P2P stage -> InTransit", st1_c == 200, st1_raw)
        st2_c, _, st2_raw = cur.post_json("/S09P2PLoanFlow/AdvanceStage", {"loanRequestId": req2_id, "stage": "HandoverBorrowed"})
        self.check("L4", "P2P stage -> HandoverBorrowed (Loan Active)", st2_c == 200, st2_raw)

        # Extract LoanId for S10/S15
        _, s10_u2, _ = u2.get("/S10ConditionReport")
        loan_m = re.search(rf'value="(\d+)"[^>]*>[^<]*{re.escape(new_barcode)}', s10_u2)
        p2p_loan_id = int(loan_m.group(1)) if loan_m else 0
        self.check("L4", "Loan created and visible in S10 eligible loans", p2p_loan_id > 0)

        # Upload real PNG to S10 + IDOR check (u3 cannot upload on u2's loan!)
        tiny_png = (
            b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01"
            b"\x08\x02\x00\x00\x00\x90wS\xde\x00\x00\x00\x0cIDATx\x9cc`\x00\x00\x00\x02\x00\x01"
            b"\xe2!\xbc3\x00\x00\x00\x00IEND\xaeB`\x82"
        )
        idor_s10_c, _, _ = u3.post_multipart(
            "/S10ConditionReport/UploadReport",
            {"loanId": str(p2p_loan_id), "reportType": "Handover", "conditionGrade": "Good", "note": "Attacker"},
            ("imageFile", "tiny.png", tiny_png, "image/png"),
        )
        self.check("L4", "IDOR unrelated user blocked from S10 UploadReport (403)", idor_s10_c == 403, f"got {idor_s10_c}")

        up_c, up_j, up_raw = u2.post_multipart(
            "/S10ConditionReport/UploadReport",
            {"loanId": str(p2p_loan_id), "reportType": "Handover", "conditionGrade": "Good", "note": "E2E clean handover"},
            ("imageFile", "e2e_handover.png", tiny_png, "image/png"),
        )
        self.check(
            "L4",
            "S10 UploadReport uploads PNG to /uploads/conditions/",
            up_c == 200 and "/uploads/conditions/" in (up_j or {}).get("imageUrl", ""),
            up_raw,
        )

        # Return book P2P
        st3_c, _, st3_raw = u1.post_json("/S09P2PLoanFlow/AdvanceStage", {"loanRequestId": req2_id, "stage": "Returned"})
        self.check("L4", "Owner completes P2P stage -> Returned", st3_c == 200, st3_raw)

        # S15: Self-review blocked + Book review +1 + Duplicate review blocked + Delete revert -1
        _, s15_u2, _ = u2.get("/S15TrustScoreReview")
        u2_uid_m = re.search(r'data-user-id="([^"]+)"', s15_u2)
        u2_uid = u2_uid_m.group(1) if u2_uid_m else "u-user-02"
        score_before_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_u2)
        score_before = int(score_before_m.group(1)) if score_before_m else 100

        self_rev_c, _, _ = u2.post_json(
            "/S15TrustScoreReview/SubmitReview",
            {"targetType": "User", "targetId": u2_uid, "rating": 5, "comment": "Self rating"},
        )
        self.check("L4", "S15 self-review blocked (400)", self_rev_c == 400)

        # Pick Pragmatic Programmer bookId from SaveBookAndCopy or S05 page
        prag_book_id = prag_book_id_from_save
        if not prag_book_id:
            _, s05_html, _ = u1.get("/S05IsbnAutoCatalog")
            bm = re.search(r'data-book-id="(\d+)"\s+data-title="The Pragmatic Programmer E2E"', s05_html)
            prag_book_id = int(bm.group(1)) if bm else 1

        rev_ok_c, _, rev_raw = u2.post_json(
            "/S15TrustScoreReview/SubmitReview",
            {"targetType": "Book", "targetId": str(prag_book_id), "loanId": p2p_loan_id, "rating": 5, "comment": "Rất hay E2E"},
        )
        self.check("L4", "S15 submit Book review +1 TrustScore", rev_ok_c == 200, rev_raw)

        dup_rev_c, _, _ = u2.post_json(
            "/S15TrustScoreReview/SubmitReview",
            {"targetType": "Book", "targetId": str(prag_book_id), "loanId": p2p_loan_id, "rating": 5, "comment": "Duplicate"},
        )
        self.check("L4", "S15 duplicate review on same Loan+Target blocked (400)", dup_rev_c == 400)

        _, s15_after, _ = u2.get("/S15TrustScoreReview")
        s15_after_txt = html_lib.unescape(s15_after)
        score_after_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_after_txt)
        score_after = int(score_after_m.group(1)) if score_after_m else 0
        self.check("L4", "S15 Book review added +1 TrustScore", score_after == score_before + 1, f"{score_before} -> {score_after}")

        rev_id_m = re.search(r'data-review-id="(\d+)"[^>]*data-rating="5"[^>]*data-comment="Rất hay E2E"', s15_after_txt)
        rev_id = int(rev_id_m.group(1)) if rev_id_m else 0
        self.check("L4", "Locate newly submitted S15 review ID", rev_id > 0)
        if rev_id > 0:
            del_rev_c, _, _ = u2.post_json("/S15TrustScoreReview/DeleteReview", {"reviewId": rev_id})
            self.check("L4", "S15 DeleteReview succeeds", del_rev_c == 200)
            _, s15_reverted, _ = u2.get("/S15TrustScoreReview")
            score_rev_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_reverted)
            score_rev = int(score_rev_m.group(1)) if score_rev_m else 0
            self.check("L4", "S15 deleting Book review reverts -1 TrustScore", score_rev == score_before, f"{score_after} -> {score_rev}")

        # ------------------------------------------------------------------
        # FLOW B: S08 Library Loan + S12 Renewal + S11 Hot Book Queue Promotion + S21 Direct Checkout/Checkin
        # ------------------------------------------------------------------
        _, s08_u2, _ = u2.get("/S08LibraryLoanFlow")
        sys_copy_m = re.search(r'id="s08BookCopyId"[^>]*>.*?<option value="(\d+)"[^>]*>(.*?)</option>', s08_u2, re.DOTALL)
        sys_copy_id = int(sys_copy_m.group(1)) if sys_copy_m else 0
        sys_copy_opt_text = sys_copy_m.group(2) if sys_copy_m else ""
        bc_opt_m = re.search(r'\[([^\]]+)\]', sys_copy_opt_text)
        sys_copy_bc = bc_opt_m.group(1) if bc_opt_m else ""
        self.check("L4", "S08 lists available System copy", sys_copy_id > 0)

        lib_req_c, _, lib_req_raw = u2.post_json(
            "/S08LibraryLoanFlow/CreateRequest",
            {"bookCopyId": sys_copy_id, "desiredDays": 14, "requestNote": "E2E LibReq U2"},
        )
        self.check("L4", "S08 u2 creates Library borrow request", lib_req_c == 200, lib_req_raw)

        _, s08_lib, _ = lib.get("/S08LibraryLoanFlow")
        s08_lib_txt = html_lib.unescape(s08_lib)
        lib_req_candidates = []
        for blk in s08_lib_txt.split("s08-flow-card"):
            if "Trần Minh Anh" in blk and "js-s08-approve" in blk and (not sys_copy_bc or sys_copy_bc in blk):
                m = re.search(r'data-req-id="(\d+)"', blk)
                if m:
                    lib_req_candidates.append(int(m.group(1)))
        lib_req_id = max(lib_req_candidates) if lib_req_candidates else 0
        self.check("L4", "Librarian finds u2 request on S08", lib_req_id > 0)

        ap_lib_c, _, _ = lib.post_json("/S08LibraryLoanFlow/ApproveOrReject", {"loanRequestId": lib_req_id, "approve": True})
        self.check("L4", "Librarian approves S08 Library request", ap_lib_c == 200)
        ho_lib_c, _, _ = lib.post_json("/S08LibraryLoanFlow/ConfirmHandover", {"loanRequestId": lib_req_id})
        self.check("L4", "Librarian confirms S08 handover (Loan Active)", ho_lib_c == 200)

        # Find u2's active loan on S12 -> renew + IDOR u3 renew blocked
        _, s12_u2, _ = u2.get("/S12LoanRenewalFine")
        lib_loan_m = re.search(r'data-loan-id="(\d+)"', s12_u2)
        # Pick highest loanId belonging to u2
        all_loans_u2 = [int(x) for x in re.findall(r'data-loan-id="(\d+)"', s12_u2)]
        lib_loan_id = max(all_loans_u2) if all_loans_u2 else 0
        self.check("L4", "Active Library loan visible on S12", lib_loan_id > 0)

        idor_ren_c, _, _ = u3.post_json("/S12LoanRenewalFine/RenewLoan", {"loanId": lib_loan_id})
        self.check("L4", "IDOR non-borrower blocked from renewing loan (403)", idor_ren_c == 403, f"got {idor_ren_c}")

        ren_c, ren_j, ren_raw = u2.post_json("/S12LoanRenewalFine/RenewLoan", {"loanId": lib_loan_id})
        self.check("L4", "S12 Borrower renews active loan (+7 days)", ren_c == 200 and (ren_j or {}).get("newRenewCount") == 1, ren_raw)

        # S21 QuickScanBarcode Checkout: overdue u-user-03 blocked; clean u-user-01 succeeds
        _, desk2_html, _ = lib.get("/S21LibrarianDesk")
        avail_sys_bc_m = re.search(r'class="[^"]*js-sample-barcode[^"]*"[^>]*data-code="([^"]+)"[^>]*>[^<]*\(Available\)', desk2_html)
        avail_sys_bc = avail_sys_bc_m.group(1) if avail_sys_bc_m else ""
        if not avail_sys_bc:
            for tr_blk in desk2_html.split("<tr"):
                if "bg-success" in tr_blk and "Available" in tr_blk:
                    cm = re.search(r"<code>([^<]+)</code>", tr_blk)
                    if cm:
                        avail_sys_bc = cm.group(1).strip()
                        break
        self.check("L4", "Locate Available System copy on S21", bool(avail_sys_bc))
        if avail_sys_bc:
            no_u_c, _, _ = lib.post_json("/S21LibrarianDesk/QuickScanBarcode", {"barcode": avail_sys_bc, "deskAction": "Checkout"})
            self.check("L4", "S21 Checkout available copy requires BorrowerUserId (400)", no_u_c == 400)
            ov_blk_c, _, _ = lib.post_json(
                "/S21LibrarianDesk/QuickScanBarcode",
                {"barcode": avail_sys_bc, "deskAction": "Checkout", "borrowerUserId": "u-user-03"},
            )
            self.check("L4", "S21 Checkout blocked for borrower with overdue loan (400)", ov_blk_c == 400)
            dir_c, _, dir_raw = lib.post_json(
                "/S21LibrarianDesk/QuickScanBarcode",
                {"barcode": avail_sys_bc, "deskAction": "Checkout", "borrowerUserId": "u-user-01"},
            )
            self.check("L4", "S21 Checkout with clean BorrowerUserId succeeds", dir_c == 200, dir_raw)
            chk_c, _, chk_raw = lib.post_json(
                "/S21LibrarianDesk/QuickScanBarcode",
                {"barcode": avail_sys_bc, "deskAction": "Checkin", "returnCondition": "Good"},
            )
            self.check("L4", "S21 Checkin returns copy to Available", chk_c == 200, chk_raw)

        # S11 HotBookQueue promotion on return:
        # Check a book on S11 with waiting queue (or enqueue u3 on book of S08 loan) & return S08 loan
        # First check which book has copies & enqueue u3 on pragmatic/hot book
        _, s11_u3, _ = u3.get("/S11HotBookQueue")
        hot_book_m = re.search(r'class="[^"]*js-s11-enqueue[^"]*"[^>]*data-book-id="(\d+)"', s11_u3)
        # Better: find BookId of lib_req_id or pick book with WaitingCount == 0 or Borrowed
        # Remember we can also return lib_req_id cleanly first, or check if hot_book has busy copy
        if hot_book_m:
            hot_bid = int(hot_book_m.group(1))
            enq_c, _, enq_raw = u3.post_json("/S11HotBookQueue/Enqueue", {"bookId": hot_bid})
            self.check("L4", "S11 Enqueue reservation executes", enq_c in (200, 400), enq_raw)

        # Confirm return on lib_req_id (triggers auto-queue notification if book has Waiting!)
        ret_lib_c, _, ret_lib_raw = lib.post_json("/S08LibraryLoanFlow/ConfirmReturn", {"loanRequestId": lib_req_id, "returnCondition": "Good"})
        self.check("L4", "S08 ConfirmReturn completes Library loan", ret_lib_c == 200, ret_lib_raw)

        # ------------------------------------------------------------------
        # FLOW C: S16 <-> S17 HMAC-SHA512 Payment, Replay Idempotency & S12 Fine Sync
        # ------------------------------------------------------------------
        sw_c, sw_j, sw_raw = u3.post_json("/S16MembershipTier/SwitchMembership", {"targetTier": "Premium", "durationMonths": 12})
        tx_id = (sw_j or {}).get("createdTransactionId")
        raw_payload = (sw_j or {}).get("rawPayload", "")
        good_hmac = (sw_j or {}).get("expectedHmac", "")
        redirect_url = (sw_j or {}).get("redirectUrl", "")
        self.check(
            "L4",
            "S16 SwitchMembership(Premium) creates Pending S17 transaction & redirectUrl",
            sw_c == 200 and isinstance(tx_id, int) and len(good_hmac) == 128 and "S17SandboxPayment" in redirect_url,
            sw_raw,
        )

        # Verify S17 prefills txId when navigated via ?txId=...
        _, s17_prefill, _ = u3.get(redirect_url or f"/S17SandboxPayment?txId={tx_id}")
        self.check("L4", "S17?txId prefills canonical payload & signature", str(tx_id) in s17_prefill and good_hmac in s17_prefill)

        if bad_hmac := (("0" if (good_hmac and good_hmac[0] != "0") else "1") + (good_hmac[1:] if len(good_hmac) > 1 else "x")):
            bad_cb_c, _, _ = u3.post_json(
                "/S17SandboxPayment/VerifyCallback",
                {"transactionId": tx_id, "rawPayload": raw_payload, "hmacSignature": bad_hmac, "responseCode": "00"},
            )
            self.check("L4", "S17 rejects tampered HMAC signature (400)", bad_cb_c == 400)

        # IDOR: u2 cannot verify u3's transaction
        idor_cb_c, _, _ = u2.post_json(
            "/S17SandboxPayment/VerifyCallback",
            {"transactionId": tx_id, "rawPayload": raw_payload, "hmacSignature": good_hmac, "responseCode": "00"},
        )
        self.check("L4", "IDOR non-owner blocked from verifying S17 tx (403)", idor_cb_c == 403, f"got {idor_cb_c}")

        # Valid HMAC completes & activates Premium
        good_cb_c, _, good_cb_raw = u3.post_json(
            "/S17SandboxPayment/VerifyCallback",
            {"transactionId": tx_id, "rawPayload": raw_payload, "hmacSignature": good_hmac, "responseCode": "00"},
        )
        self.check("L4", "S17 verifies valid HMAC-SHA512 & completes tx", good_cb_c == 200, good_cb_raw)

        # Replay callback must be rejected idempotently
        replay_c, _, _ = u3.post_json(
            "/S17SandboxPayment/VerifyCallback",
            {"transactionId": tx_id, "rawPayload": raw_payload, "hmacSignature": good_hmac, "responseCode": "00"},
        )
        self.check("L4", "S17 replay VerifyCallback is rejected idempotently (400)", replay_c == 400)

        _, s16_u3, _ = u3.get("/S16MembershipTier")
        self.check("L4", "S16 shows u3 upgraded to Premium (5 concurrent loans)", "Gói hiện tại: Premium" in s16_u3 and "5 cuốn" in s16_u3)

        # Librarian assesses unpaid fine on u3's existing overdue loan -> u3 pays via S17 -> clears Overdue -> S21 unblocks u3!
        _, s12_u3_before, _ = u3.get("/S12LoanRenewalFine")
        u3_seed_loans = [int(x) for x in re.findall(r'data-loan-id="(\d+)"', s12_u3_before)]
        u3_overdue_loan_id = min(u3_seed_loans) if u3_seed_loans else 3
        af_c, af_j, af_raw = lib.post_json(
            "/S12LoanRenewalFine/AssessFine",
            {"loanId": u3_overdue_loan_id, "simulatedOverdueDays": 3, "markPaidImmediately": False},
        )
        fine_tx_id = (af_j or {}).get("transactionId", 0)
        self.check("L4", "Librarian assesses unpaid overdue fine via S12", af_c == 200 and fine_tx_id > 0, af_raw)

        # Fetch canonical payload/hmac from S17?txId=fine_tx_id as u3 and pay!
        _, s17_fine_html, _ = u3.get(f"/S17SandboxPayment?txId={fine_tx_id}")
        rp_m = re.search(r'id="cbRawPayload"[^>]*value="([^"]+)"', s17_fine_html)
        hm_m = re.search(r'id="cbHmacSignature"[^>]*>([^<]+)</textarea>', s17_fine_html)
        if rp_m and hm_m:
            pay_c, _, pay_raw = u3.post_json(
                "/S17SandboxPayment/VerifyCallback",
                {
                    "transactionId": fine_tx_id,
                    "rawPayload": html_lib.unescape(rp_m.group(1)),
                    "hmacSignature": html_lib.unescape(hm_m.group(1).strip()),
                    "responseCode": "00",
                },
            )
            self.check("L4", "u3 pays fine on S17 & clears loan FineAmount", pay_c == 200, pay_raw)

        # Verify u3 is still blocked while seeded Dispute on Loan #3 remains Open, then use clean u2 for full S19 Dispute lifecycle
        if avail_sys_bc:
            u3_disp_blk_c, _, _ = lib.post_json(
                "/S21LibrarianDesk/QuickScanBarcode",
                {"barcode": avail_sys_bc, "deskAction": "Checkout", "borrowerUserId": "u-user-03"},
            )
            self.check("L4", "S21 Checkout still blocked while borrower has open Dispute (400)", u3_disp_blk_c == 400)

            chk_clean_c, _, chk_clean_raw = lib.post_json(
                "/S21LibrarianDesk/QuickScanBarcode",
                {"barcode": avail_sys_bc, "deskAction": "Checkout", "borrowerUserId": "u-user-02"},
            )
            self.check("L4", "Clean borrower u2 checks out copy on S21 for Dispute test", chk_clean_c == 200, chk_clean_raw)
            _, s12_u2_d, _ = u2.get("/S12LoanRenewalFine")
            u2_loans_d = [int(x) for x in re.findall(r'data-loan-id="(\d+)"', s12_u2_d)]
            u2_active_loan_id = max(u2_loans_d) if u2_loans_d else 2

            # Simulated Deposit + Librarian Refund
            dep_c, dep_j, _ = u2.post_json("/S17SandboxPayment/CreateOrder", {"transactionType": "SimulatedDeposit", "amount": 50000})
            dep_id = (dep_j or {}).get("transactionId", 0)
            if dep_c == 200 and dep_id:
                u2.post_json(
                    "/S17SandboxPayment/VerifyCallback",
                    {"transactionId": dep_id, "rawPayload": dep_j["rawPayload"], "hmacSignature": dep_j["expectedHmacSignature"], "responseCode": "00"},
                )
                ref_c, _, ref_raw = lib.post_json("/S17SandboxPayment/RefundDeposit", {"transactionId": dep_id})
                self.check("L4", "Librarian refunds completed SimulatedDeposit via S17", ref_c == 200, ref_raw)

            # ------------------------------------------------------------------
            # FLOW D: S19 Dispute Lock & Admin Resolution Unlock
            # ------------------------------------------------------------------
            cd_c, cd_j, cd_raw = u2.post_json(
                "/S19DisputeResolution/CreateDispute",
                {"loanId": u2_active_loan_id, "reason": "E2E Kiểm tra tranh chấp khóa mượn"},
            )
            disp_id = (cd_j or {}).get("disputeId", 0)
            self.check("L4", "Participant raises dispute on S19 -> Loan Disputed", cd_c == 200 and disp_id > 0, cd_raw)

            # While disputed, u2 cannot borrow another available book
            _, s08_blk_page, _ = u2.get("/S08LibraryLoanFlow")
            blk_opt_m = re.search(r'id="s08BookCopyId"[^>]*>.*?<option value="(\d+)"', s08_blk_page, re.DOTALL)
            avail_for_blk_id = int(blk_opt_m.group(1)) if blk_opt_m else sys_copy_id
            blk_c, blk_j, blk_raw = u2.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": avail_for_blk_id, "desiredDays": 7})
            self.check(
                "L4",
                "Open dispute blocks borrower from creating new LoanRequest",
                blk_c == 400 and ("tranh chấp" in blk_raw.lower() or "khiếu nại" in blk_raw.lower()),
                blk_raw,
            )

            res_c, _, res_raw = admin.post_json(
                "/S19DisputeResolution/ResolveDispute",
                {
                    "disputeId": disp_id,
                    "decisionStatus": "Resolved",
                    "trustScorePenalty": 5,
                    "depositDeductionAmount": 25000,
                    "copyTargetStatus": "Damaged",
                    "resolutionNote": "E2E Admin phân xử mở khóa hợp đồng",
                },
            )
            self.check("L4", "Admin resolves S19 dispute & unlocks Loan", res_c == 200, res_raw)

            re_res_c, _, _ = admin.post_json(
                "/S19DisputeResolution/ResolveDispute",
                {"disputeId": disp_id, "decisionStatus": "Rejected"},
            )
            self.check("L4", "S19 prevents re-resolving already Resolved dispute (400)", re_res_c == 400)

        # ------------------------------------------------------------------
        # FLOW E: S18 OTP + EmailConfirmed & S20 Duplicate Bonus Guard & S13/S14 AI & S22 DailyJobs
        # ------------------------------------------------------------------
        otp_c, otp_j, otp_raw = u1.post_json(
            "/S01AuthOtpGoogle/SendOtp",
            {"email": "nguyenhung@cmc.edu.vn", "purpose": "VerifyNotificationEmail"},
        )
        dev_otp = (otp_j or {}).get("devOtpCode", "")
        self.check("L4", "S01/S18 SendOtp generates 6-digit OTP", otp_c == 200 and len(dev_otp) == 6, otp_raw)
        votp_c, _, votp_raw = u1.post_json(
            "/S01AuthOtpGoogle/VerifyOtp",
            {"email": "nguyenhung@cmc.edu.vn", "otpCode": dev_otp, "purpose": "VerifyNotificationEmail"},
        )
        self.check("L4", "VerifyOtp succeeds & confirms email", votp_c == 200, votp_raw)

        # S20 Dropoff + Curator double-verify TrustScore bonus guard
        dr_c, _, dr_raw = u1.post_json("/S20ExchangeHubClub/RegisterDropoff", {"bookCopyId": flow_copy_id, "hubId": 3, "note": "E2E Hub dropoff"})
        self.check("L4", "S20 Owner registers physical copy dropoff at Hub", dr_c == 200, dr_raw)

        _, s15_u1_b, _ = u1.get("/S15TrustScoreReview")
        ts_b_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_u1_b)
        ts_before = int(ts_b_m.group(1)) if ts_b_m else 100

        cv1_c, _, cv1_raw = cur.post_json("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": flow_copy_id, "hubId": 3, "verifiedCondition": "Good", "shelfLocation": "Trạm CLB-K9"})
        _, s15_u1_a1, _ = u1.get("/S15TrustScoreReview")
        ts_1_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_u1_a1)
        ts_after1 = int(ts_1_m.group(1)) if ts_1_m else 0
        self.check("L4", "S20 Curator first verification grants +2 TrustScore", cv1_c == 200 and ts_after1 == ts_before + 2, f"{cv1_raw} ({ts_before}->{ts_after1})")

        cv2_c, _, cv2_raw = cur.post_json("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": flow_copy_id, "hubId": 3, "verifiedCondition": "Good", "shelfLocation": "Trạm CLB-K9"})
        _, s15_u1_a2, _ = u1.get("/S15TrustScoreReview")
        ts_2_m = re.search(r'class="s15-my-score"\s+data-trust-score="(\d+)"', s15_u1_a2)
        ts_after2 = int(ts_2_m.group(1)) if ts_2_m else 0
        self.check(
            "L4",
            "S20 duplicate verification guard does NOT double-award TrustScore bonus",
            cv2_c == 200 and ts_after2 == ts_after1,
            f"before={ts_before}, after1={ts_after1}, after2={ts_after2}, msg={cv2_raw}",
        )

        # S13 + S14 + S22 DailyJobs
        cs_c, cs_j, _ = u1.post_json("/S13GeminiAiAssistant/CreateSession", {"title": "E2E AI Check"})
        sid = (cs_j or {}).get("sessionId")
        msg_c, msg_j, _ = u1.post_json("/S13GeminiAiAssistant/SendMessage", {"sessionId": sid, "message": "Kiểm tra hạn trả sách"})
        self.check("L4", "S13 AI CreateSession + SendMessage succeed", cs_c == 200 and msg_c == 200 and bool((msg_j or {}).get("replyText")))

        rec_c, rec_j, _ = u1.post_json("/S14AiRecommendation/GenerateCustomAdvice", {"mood": "DeepFocus", "skillGoal": "System Design"})
        self.check("L4", "S14 GenerateCustomAdvice succeeds", rec_c == 200 and bool((rec_j or {}).get("aiAdvice")))

        job_c, job_j, job_raw = admin.post_json("/S22AdminAnalytics/TriggerDailyJob", {"jobKey": "AllDailyJobs"})
        self.check("L4", "S22 Admin TriggerDailyJob(AllDailyJobs) succeeds", job_c == 200, job_raw)

    def run_layer6_sprint3(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 6: SPRINT 3 UPGRADES (QR AUTH 2-WAY, HANDOVER QR, S19<->S17, CURATOR, COPILOT, MOBILE) ===")
        u1 = actors["u1"]
        u2 = actors["u2"]
        cur = actors["curator"]
        lib = actors["librarian"]
        admin = actors["admin"]

        # 6.1 2-Way QR Login — Direction 1 (WebLoginByMobile)
        qr_web = SessionClient(self.base_url, "qr_web")
        qr_web.refresh_csrf("/S01AuthOtpGoogle")
        qc_c, qc_j, qc_raw = qr_web.post_json("/api/qr-login/create", {})
        qc_data = (qc_j or {}).get("data") or {}
        sid = int(qc_data.get("sessionId") or 0)
        tok = qc_data.get("token") or ""
        qr_payload = qc_data.get("qrPayload") or ""
        self.check(
            "L6",
            "QR Login Dir1 create returns sessionId, token & BOOKSHARE-QRLOGIN payload",
            qc_c == 200 and sid > 0 and len(tok) >= 20 and qr_payload.startswith("BOOKSHARE-QRLOGIN:v1:"),
            qc_raw,
        )

        st_c, st_raw, _ = qr_web.get(f"/api/qr-login/status/{sid}", accept_json=True)
        self.check("L6", "QR Login status polls Pending while awaiting approval", st_c == 200 and '"Pending"' in st_raw, st_raw)

        u1.refresh_csrf("/S01AuthOtpGoogle")
        pv_c, pv_j, pv_raw = u1.post_json("/api/qr-login/preview", {"qrPayload": qr_payload})
        self.check(
            "L6",
            "QR Login preview returns device info & sessionId for approver",
            pv_c == 200 and ((pv_j or {}).get("data") or {}).get("sessionId") == sid,
            pv_raw,
        )

        ap_c, _, ap_raw = u1.post_json("/api/qr-login/approve", {"sessionId": sid, "approve": True})
        self.check("L6", "Authenticated User1 approves Web QR session", ap_c == 200, ap_raw)

        cl_c, _, cl_raw = qr_web.post_json("/api/qr-login/claim", {"sessionId": sid, "token": tok})
        ms_c, ms_raw, _ = qr_web.get("/api/mobile/session", accept_json=True)
        try:
            ms_j = json.loads(ms_raw)
        except Exception:
            ms_j = {}
        self.check(
            "L6",
            "QR Login Dir1 claim issues valid session cookie & prevents replay",
            cl_c == 200 and ms_c == 200 and ms_j.get("authenticated") is True and ms_j.get("userId") == "u-user-01",
            f"claim={cl_c} session={ms_raw}",
        )

        rep_c, _, rep_raw = qr_web.post_json("/api/qr-login/claim", {"sessionId": sid, "token": tok})
        self.check("L6", "QR Login Dir1 replay claim rejected (400)", rep_c == 400, rep_raw)

        # Locked account cannot approve Web QR session
        qr_web2 = SessionClient(self.base_url, "qr_web2")
        qr_web2.refresh_csrf("/S01AuthOtpGoogle")
        _, q2_j, _ = qr_web2.post_json("/api/qr-login/create", {})
        sid2 = int(((q2_j or {}).get("data") or {}).get("sessionId") or 0)
        admin.refresh_csrf("/S22AdminAnalytics")
        lk_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-user-02", "lockAccount": True})
        u2.refresh_csrf("/S01AuthOtpGoogle")
        lk_ap_c, _, lk_ap_raw = u2.post_json("/api/qr-login/approve", {"sessionId": sid2, "approve": True})
        admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-user-02", "lockAccount": False})
        self.check("L6", "Locked account blocked from approving QR session (400)", lk_c == 200 and lk_ap_c == 400, lk_ap_raw)

        # 6.2 2-Way QR Login — Direction 2 (MobileLoginByWeb)
        ml_c, ml_j, ml_raw = u1.post_json("/api/qr-login/create-mobile-link", {})
        mob_payload = ((ml_j or {}).get("data") or {}).get("qrPayload") or ""
        self.check(
            "L6",
            "QR Login Dir2 create-mobile-link returns BOOKSHARE-MOBILELINK payload",
            ml_c == 200 and mob_payload.startswith("BOOKSHARE-MOBILELINK:v1:"),
            ml_raw,
        )

        qr_mob = SessionClient(self.base_url, "qr_mob")
        qr_mob.refresh_csrf("/S01AuthOtpGoogle")
        mc_c, _, mc_raw = qr_mob.post_json("/api/qr-login/mobile-claim", {"qrPayload": mob_payload})
        _, m_sess_raw, _ = qr_mob.get("/api/mobile/session", accept_json=True)
        try:
            m_sess = json.loads(m_sess_raw)
        except Exception:
            m_sess = {}
        self.check(
            "L6",
            "QR Login Dir2 mobile-claim authenticates mobile client & authenticates user1",
            mc_c == 200 and m_sess.get("authenticated") is True and m_sess.get("userId") == "u-user-01",
            f"claim={mc_c} sess={m_sess_raw}",
        )

        mc_rep_c, _, _ = qr_mob.post_json("/api/qr-login/mobile-claim", {"qrPayload": mob_payload})
        self.check("L6", "QR Login Dir2 replay mobile-claim rejected (400)", mc_rep_c == 400)

        # 6.3 Physical Book Handover & Return QR (Anti-IDOR + Single-Use)
        _, s08_u1_html, _ = u1.get("/S08LibraryLoanFlow")
        s08_opt_m = re.search(r'id="s08BookCopyId"[^>]*>.*?<option value="(\d+)"', s08_u1_html, re.DOTALL)
        ho_copy_id = int(s08_opt_m.group(1)) if s08_opt_m else 0
        self.check("L6", "S08 lists Available copy for Handover QR flow", ho_copy_id > 0)
        if ho_copy_id > 0:
            u1.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": ho_copy_id, "desiredDays": 7, "requestNote": "L6 QR Handover"})
            _, s08_lib2, _ = lib.get("/S08LibraryLoanFlow")
            s08_lib2_txt = html_lib.unescape(s08_lib2)
            req_cands = []
            for blk in s08_lib2_txt.split("s08-flow-card"):
                if "js-s08-approve" in blk and ("Nguyễn" in blk or "nguyenhung" in blk):
                    m = re.search(r'data-req-id="(\d+)"', blk)
                    if m:
                        req_cands.append(int(m.group(1)))
            ho_req_id = max(req_cands) if req_cands else 0
            lib.post_json("/S08LibraryLoanFlow/ApproveOrReject", {"loanRequestId": ho_req_id, "approve": True})

            hq_c, hq_j, hq_raw = lib.post_json("/api/handover-qr/create", {"loanRequestId": ho_req_id, "handoverType": "Handover"})
            ho_payload = ((hq_j or {}).get("data") or {}).get("qrPayload") or ""
            self.check(
                "L6",
                "Librarian generates Handover QR payload",
                hq_c == 200 and ho_payload.startswith("BOOKSHARE-HANDOVER:v1:"),
                hq_raw,
            )

            idor_ho_c, _, _ = cur.post_json("/api/handover-qr/confirm", {"qrPayload": ho_payload})
            self.check("L6", "Unrelated party blocked from confirming Handover QR (403 Anti-IDOR)", idor_ho_c == 403, f"got {idor_ho_c}")

            ok_ho_c, _, ok_ho_raw = u1.post_json("/api/handover-qr/confirm", {"qrPayload": ho_payload})
            self.check("L6", "Borrower confirms Handover QR -> Loan activated", ok_ho_c == 200, ok_ho_raw)

            rep_ho_c, _, _ = u1.post_json("/api/handover-qr/confirm", {"qrPayload": ho_payload})
            self.check("L6", "Replaying consumed Handover QR fails (400)", rep_ho_c == 400)

            rq_c, rq_j, rq_raw = lib.post_json("/api/handover-qr/create", {"loanRequestId": ho_req_id, "handoverType": "Return"})
            ret_payload = ((rq_j or {}).get("data") or {}).get("qrPayload") or ""
            ok_ret_c, _, ok_ret_raw = lib.post_json("/api/handover-qr/confirm", {"qrPayload": ret_payload, "returnCondition": "Good"})
            self.check("L6", "Return QR closes loan cleanly back to Available", rq_c == 200 and ok_ret_c == 200, f"{rq_raw} / {ok_ret_raw}")

        # 6.4 Dispute Closure S19 <-> S17 (Pending compensation locks borrow until paid via HMAC)
        _, desk_l6, _ = lib.get("/S21LibrarianDesk")
        avail_bc_m = re.search(r'class="[^"]*js-sample-barcode[^"]*"[^>]*data-code="([^"]+)"[^>]*>[^<]*\(Available\)', desk_l6)
        avail_bc_l6 = avail_bc_m.group(1) if avail_bc_m else ""
        if not avail_bc_l6:
            for tr_blk in desk_l6.split("<tr"):
                if "bg-success" in tr_blk and "Available" in tr_blk:
                    cm = re.search(r"<code>([^<]+)</code>", tr_blk)
                    if cm:
                        avail_bc_l6 = cm.group(1).strip()
                        break
        self.check("L6", "Locate Available System copy on S21 for Dispute closure test", bool(avail_bc_l6))
        if avail_bc_l6:
            lib.post_json("/S21LibrarianDesk/QuickScanBarcode", {"barcode": avail_bc_l6, "deskAction": "Checkout", "borrowerUserId": "u-user-01"})
            _, s12_u1, _ = u1.get("/S12LoanRenewalFine")
            u1_loans = [int(x) for x in re.findall(r'data-loan-id="(\d+)"', s12_u1)]
            u1_loan_id = max(u1_loans) if u1_loans else 0
            _, dl6_j, _ = u1.post_json("/S19DisputeResolution/CreateDispute", {"loanId": u1_loan_id, "reason": "L6 Kiểm tra bồi thường khép kín"})
            disp_l6_id = int((dl6_j or {}).get("disputeId") or 0)

            res6_c, res6_j, res6_raw = admin.post_json(
                "/S19DisputeResolution/ResolveDispute",
                {
                    "disputeId": disp_l6_id,
                    "decisionStatus": "Resolved",
                    "trustScorePenalty": 2,
                    "depositDeductionAmount": 0,
                    "compensationAmount": 25000,
                    "copyTargetStatus": "Damaged",
                    "resolutionNote": "L6 Bồi thường qua S17 để mở khóa",
                },
            )
            ptx_id = int((res6_j or {}).get("pendingTransactionId") or 0)
            p_raw = (res6_j or {}).get("rawPayload") or ""
            p_hmac = (res6_j or {}).get("expectedHmac") or ""
            unlocked_flag = (res6_j or {}).get("loanUnlocked")
            self.check(
                "L6",
                "S19 ResolveDispute with compensation creates Pending S17 tx & keeps lock",
                res6_c == 200 and ptx_id > 0 and len(p_hmac) == 128 and unlocked_flag is False,
                res6_raw,
            )

            _, s08_u1_blk, _ = u1.get("/S08LibraryLoanFlow")
            blk_opt = re.search(r'id="s08BookCopyId"[^>]*>.*?<option value="(\d+)"', s08_u1_blk, re.DOTALL)
            blk_cid = int(blk_opt.group(1)) if blk_opt else 0
            still_blk_c, _, still_blk_raw = u1.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": blk_cid, "desiredDays": 7})
            self.check("L6", "Borrower remains blocked from borrowing while compensation tx Pending (400)", still_blk_c == 400, still_blk_raw)

            pay6_c, _, pay6_raw = u1.post_json(
                "/S17SandboxPayment/VerifyCallback",
                {"transactionId": ptx_id, "rawPayload": p_raw, "hmacSignature": p_hmac, "responseCode": "00"},
            )
            unblk_c, _, unblk_raw = u1.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": blk_cid, "desiredDays": 7})
            self.check(
                "L6",
                "Paying compensation invoice in S17 unlocks borrower immediately",
                pay6_c == 200 and unblk_c == 200,
                f"pay={pay6_raw} borrow={unblk_raw}",
            )

        # 6.5 Curator Role Upgrade (S01 -> S22)
        cur_sub_c, _, cur_sub_raw = u2.post_json(
            "/S01AuthOtpGoogle/SubmitCuratorRequest",
            {"organizationName": "CLB Sách Sinh Viên K4", "reason": "Tổ chức điểm trạm đọc tầng 2"},
        )
        cur_dup_c, _, _ = u2.post_json(
            "/S01AuthOtpGoogle/SubmitCuratorRequest",
            {"organizationName": "CLB Sách Sinh Viên K4", "reason": "Gửi trùng lần 2"},
        )
        self.check("L6", "User2 submits Curator upgrade & duplicate is rejected (400)", cur_sub_c == 200 and cur_dup_c == 400, cur_sub_raw)

        _, s22_adm_html, _ = admin.get("/S22AdminAnalytics")
        upg_ids = [int(x) for x in re.findall(r'js-btn-review-upgrade[^>]*data-id="(\d+)"', s22_adm_html)]
        upg_id = max(upg_ids) if upg_ids else 0
        rev_c, _, rev_raw = admin.post_json(
            "/S22AdminAnalytics/ReviewRoleUpgrade",
            {"requestId": upg_id, "approve": True, "reviewNote": "L6 Admin đồng ý nâng quyền"},
        )
        u2_promoted = SessionClient(self.base_url, "u2_promoted")
        ok_u2_re, _ = u2_promoted.login("minhanh@cmc.edu.vn", "User@123")
        _, u2_sess_raw, _ = u2_promoted.get("/api/mobile/session", accept_json=True)
        try:
            u2_sess = json.loads(u2_sess_raw)
        except Exception:
            u2_sess = {}
        self.check(
            "L6",
            "Admin ReviewRoleUpgrade promotes User2 to Curator role",
            upg_id > 0 and rev_c == 200 and ok_u2_re and u2_sess.get("role") == "Curator",
            f"upg_id={upg_id} rev={rev_raw} sess={u2_sess_raw}",
        )

        # 6.6 AI Copilot 2-Tier Commands & RBAC Enforcement
        ask_c, ask_j, ask_raw = u1.post_json("/api/ai-chat/quick-ask", {"message": "Chuyển sang chế độ tối"})
        cmds = (ask_j or {}).get("clientCommands") or []
        has_theme_cmd = any(c.get("command") == "set_theme" and (c.get("args") or {}).get("theme") == "dark" for c in cmds)
        self.check("L6", "AI Copilot quick-ask returns Tier-1 set_theme clientCommand", ask_c == 200 and has_theme_cmd, ask_raw)

        rbac_blk_c, _, _ = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_due_reminders_batch"})
        self.check("L6", "AI Copilot blocks User from batch reminder action (403 RBAC)", rbac_blk_c == 403, f"got {rbac_blk_c}")

        self_rem_c, _, self_rem_raw = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_my_reminder_email"})
        self.check("L6", "AI Copilot allows User self reminder action (200)", self_rem_c == 200, self_rem_raw)

        lib.refresh_csrf("/S13GeminiAiAssistant")
        lib_batch_c, _, lib_batch_raw = lib.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_due_reminders_batch"})
        self.check("L6", "AI Copilot allows Librarian batch reminder action (200)", lib_batch_c == 200, lib_batch_raw)

        # 6.7 Mobile Companion API, UA Adaptive Shell & Static Mobile/CI/Docker Checks
        anon_mob = SessionClient(self.base_url, "anon_mob")
        unauth_c, _, unauth_hdrs = anon_mob.get("/api/mobile/notifications/unread", accept_json=True)
        ct_unauth = next((v for k, v in unauth_hdrs.items() if k.lower() == "content-type"), "")
        self.check(
            "L6",
            "Unauthenticated GET /api/mobile/notifications/unread returns 401 JSON (not HTML redirect)",
            unauth_c == 401 and "json" in ct_unauth.lower(),
            f"status={unauth_c}, ct={ct_unauth}",
        )

        auth_mob_c, auth_mob_raw, _ = u1.get("/api/mobile/notifications/unread?sinceId=0", accept_json=True)
        self.check(
            "L6",
            "Authenticated GET /api/mobile/notifications/unread returns unreadCount & notifications",
            auth_mob_c == 200 and "unreadCount" in auth_mob_raw and "notifications" in auth_mob_raw,
            auth_mob_raw[:200],
        )

        req_ua = urllib.request.Request(
            self.base_url + "/",
            headers={"User-Agent": "Mozilla/5.0 (Linux; Android 14) BookShareAndroidApp/1.0"},
            method="GET",
        )
        with urllib.request.urlopen(req_ua, timeout=15) as resp_ua:
            ua_html = resp_ua.read().decode("utf-8", errors="replace")
        self.check(
            "L6",
            "BookShareAndroidApp User-Agent triggers is-mobile-app & renders mobileBottomNav",
            "is-mobile-app" in ua_html and 'id="mobileBottomNav"' in ua_html,
        )

        main_act = ROOT_DIR / "mobile" / "bookshare-android" / "app" / "src" / "main" / "java" / "vn" / "bookshare" / "mobile" / "MainActivity.java"
        poller = ROOT_DIR / "mobile" / "bookshare-android" / "app" / "src" / "main" / "java" / "vn" / "bookshare" / "mobile" / "NotificationPoller.java"
        wf_yml = ROOT_DIR / ".github" / "workflows" / "build-bookshare-android-apk.yml"
        docker_yml = ROOT_DIR / "docker-compose.yml"

        main_act_txt = main_act.read_text(encoding="utf-8") if main_act.exists() else ""
        poller_txt = poller.read_text(encoding="utf-8") if poller.exists() else ""
        wf_txt = wf_yml.read_text(encoding="utf-8") if wf_yml.exists() else ""
        docker_txt = docker_yml.read_text(encoding="utf-8") if docker_yml.exists() else ""

        self.check(
            "L6",
            "Android MainActivity enforces BuildConfig.BASE_URL, BookShareAndroidApp UA, Custom Tabs & NO URL dialog",
            bool(main_act_txt)
            and "BuildConfig.BASE_URL" in main_act_txt
            and "BookShareAndroidApp/1.0" in main_act_txt
            and "CustomTabsIntent" in main_act_txt
            and "AlertDialog" not in main_act_txt
            and "EditText" not in main_act_txt,
        )
        self.check(
            "L6",
            "Android NotificationPoller attaches CookieManager cookies for background notifications",
            bool(poller_txt) and "CookieManager.getInstance().getCookie" in poller_txt,
        )
        self.check(
            "L6",
            "GitHub Actions workflow builds BookShare-Mobile-Android.apk with BOOKSHARE_BASE_URL",
            bool(wf_txt) and "BookShare-Mobile-Android.apk" in wf_txt and "BOOKSHARE_BASE_URL" in wf_txt,
        )
        db_init_section = docker_txt.split("db-init:", 1)[1].split("web:", 1)[0] if "db-init:" in docker_txt else docker_txt
        self.check(
            "L6",
            "Docker Compose db-init runs schema-only by default (no 02_BookShare_SeedData.sql)",
            "01_BookShare_Schema.sql" in db_init_section and "02_BookShare_SeedData.sql" not in db_init_section,
        )

        # 6.8 Global Exception & Friendly Status Code Error Pages (400/401/403/404/405/500)
        guest_err = SessionClient(self.base_url, "guest_err")
        c_s02p, h_s02p, _ = guest_err.get("/S02Pers")
        h_s02p_txt = html_lib.unescape(h_s02p)
        self.check(
            "L6",
            "GET /S02Pers returns 404 friendly Vietnamese page with /S02PersonalShelf suggestion & no dev leaks",
            c_s02p == 404
            and "Về Trang Chủ" in h_s02p_txt
            and "Quay Lại Trang Trước" in h_s02p_txt
            and re.search(r'id="btnErrorSuggested"[^>]*href="/S02PersonalShelf"[^>]*>.*?Tủ Sách Cá Nhân', h_s02p_txt, re.DOTALL) is not None
            and "Development Mode" not in h_s02p_txt
            and "ASPNETCORE_ENVIRONMENT" not in h_s02p_txt,
            f"status={c_s02p}",
        )

        c_404, h_404, _ = guest_err.get("/duong-dan-khong-ton-tai-xyz")
        self.check(
            "L6",
            "GET /duong-dan-khong-ton-tai-xyz returns 404 friendly BookShare error page",
            c_404 == 404 and "Mã lỗi 404" in h_404 and "Về Trang Chủ" in h_404,
            f"status={c_404}",
        )

        c_api404, b_api404, h_api404 = guest_err.get("/api/khong-ton-tai-xyz", accept_json=True)
        ct_api404 = next((v for k, v in h_api404.items() if k.lower() == "content-type"), "")
        try:
            j_api404 = json.loads(b_api404)
        except Exception:
            j_api404 = {}
        self.check(
            "L6",
            "GET /api/khong-ton-tai-xyz with Accept JSON returns 404 JSON (not HTML)",
            c_api404 == 404
            and "application/json" in ct_api404.lower()
            and j_api404.get("success") is False
            and j_api404.get("statusCode") == 404,
            f"status={c_api404}, ct={ct_api404}, body={b_api404[:140]}",
        )

        c_u1_s22, h_u1_s22, _ = u1.get("/S22AdminAnalytics")
        c_u1_s22_api, b_u1_s22_api, h_u1_s22_api = u1.get("/S22AdminAnalytics/ChartData", accept_json=True)
        ct_u1_api = next((v for k, v in h_u1_s22_api.items() if k.lower() == "content-type"), "")
        self.check(
            "L6",
            "User GET /S22AdminAnalytics returns friendly 403 page & ChartData API returns 403 JSON",
            c_u1_s22 == 403
            and "Đăng Nhập / Đổi Tài Khoản" in h_u1_s22
            and "Về Trang Chủ" in h_u1_s22
            and c_u1_s22_api == 403
            and "application/json" in ct_u1_api.lower(),
            f"html_status={c_u1_s22}, api_status={c_u1_s22_api}, ct={ct_u1_api}, api_body={b_u1_s22_api[:120]}",
        )

        static_req = urllib.request.Request(self.base_url + "/uploads/khong-ton-tai.png", method="GET")
        try:
            with guest_err._no_redirect_opener.open(static_req, timeout=10) as sr:
                st_code = sr.status
                st_body = sr.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as e:
            st_code = e.code
            st_body = e.read().decode("utf-8", errors="replace")
        self.check(
            "L6",
            "Missing static asset /uploads/khong-ton-tai.png returns direct 404 without redirect or HTML loop",
            st_code == 404 and "<html" not in st_body.lower(),
            f"status={st_code}, len={len(st_body)}",
        )

    def run_layer5_cleanup_and_integrity(self, admin: SessionClient) -> None:
        print("\n=== LAYER 5: FINAL DATABASE RESET & POST-TEST SEED INTEGRITY ===")
        sc, _, sraw = admin.post_json("/S22AdminAnalytics/ManageSampleData", {"actionMode": "SeedSampleData"})
        self.check("L5", "Final Admin SeedSampleData restores baseline cleanly", sc == 200, sraw)

        _, s22_html, _ = admin.get("/S22AdminAnalytics")
        self.check(
            "L5",
            "Baseline counts verified (6 Thành viên, 55 Bản sao)",
            "6 Thành Viên" in s22_html and "55 Bản Sao" in s22_html,
        )

        u1_verify = SessionClient(self.base_url, "u1_final")
        ok, fl = u1_verify.login("nguyenhung@cmc.edu.vn", "User@123")
        self.check("L5", "Post-reset User1 login works with User@123", ok, fl)

    def run_layer7_sprint35(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 7: Sprint 3.5 (Local Assets, QR, Header, Icon Manager, Blank DB & Stale Cookie) ===")
        admin = actors["admin"]
        u1 = actors["u1"]

        # 7.1 Local vendor assets & 0 external CDN audit
        required_local = [
            WEB_DIR / "wwwroot" / "vendor" / "qr" / "qrcode.min.js",
            WEB_DIR / "wwwroot" / "vendor" / "qr" / "html5-qrcode.min.js",
            WEB_DIR / "wwwroot" / "vendor" / "confetti" / "confetti.browser.min.js",
            WEB_DIR / "wwwroot" / "vendor" / "jspdf" / "jspdf.umd.min.js",
            WEB_DIR / "wwwroot" / "vendor" / "html2canvas" / "html2canvas.min.js",
            WEB_DIR / "wwwroot" / "images" / "book-placeholder.svg",
            WEB_DIR / "wwwroot" / "images" / "avatar-default.svg",
        ]
        missing_local = [str(p.relative_to(ROOT_DIR)) for p in required_local if not p.exists() or p.stat().st_size == 0]
        self.check("L7", "All 7 self-hosted vendor JS & SVG placeholder files exist", not missing_local, str(missing_local))

        cdn_pattern = re.compile(
            r"cdn\.jsdelivr\.net|cdnjs\.cloudflare\.com|unpkg\.com|api\.qrserver\.com|chart\.googleapis\.com|images\.unsplash\.com",
            re.IGNORECASE,
        )
        legacy_pattern = re.compile(r"CLEAN STATE|02_BookShare_SeedData\.sql|book-tracker-ver07", re.IGNORECASE)
        cdn_hits: list[str] = []
        legacy_hits: list[str] = []
        scan_roots = [
            WEB_DIR / "Views",
            WEB_DIR / "wwwroot" / "js",
            WEB_DIR / "wwwroot" / "modules",
            WEB_DIR / "wwwroot" / "css",
        ]
        for root in scan_roots:
            for f in root.rglob("*"):
                if f.suffix.lower() not in {".cshtml", ".js", ".css"}:
                    continue
                txt = f.read_text(encoding="utf-8", errors="ignore")
                if cdn_pattern.search(txt):
                    cdn_hits.append(str(f.relative_to(ROOT_DIR)))
                if legacy_pattern.search(txt):
                    legacy_hits.append(str(f.relative_to(ROOT_DIR)))
        self.check("L7", "Zero external CDN / Unsplash fallback URLs in Views & wwwroot", not cdn_hits, str(cdn_hits))
        self.check("L7", "Zero legacy CLEAN STATE / SeedData / ver07 strings in Views & wwwroot", not legacy_hits, str(legacy_hits))

        # Verify zero schema drift between EF DbContext/Entities and database/01_BookShare_Schema.sql
        ef_check_files = [
            WEB_DIR / "Data" / "BookShareDbContext.cs",
            WEB_DIR / "Models" / "Entities" / "BookCopy.cs",
            WEB_DIR / "Models" / "Entities" / "Loan.cs",
        ]
        drift_pattern = re.compile(r"\b(DailyRentalFee|DepositAmount|DepositHeld|EscrowStatus|RentalFeeTotal)\b")
        drift_hits = [
            str(p.relative_to(ROOT_DIR))
            for p in ef_check_files
            if p.exists() and drift_pattern.search(p.read_text(encoding="utf-8", errors="ignore"))
        ]
        self.check("L7", "Zero legacy column drift between EF entities and 01_BookShare_Schema.sql", not drift_hits, str(drift_hits))

        guest = SessionClient(self.base_url)
        for asset_url in ["/vendor/qr/qrcode.min.js", "/vendor/qr/html5-qrcode.min.js", "/images/book-placeholder.svg", "/images/avatar-default.svg"]:
            ac, _, _ = guest.get(asset_url)
            self.check("L7", f"Local static asset {asset_url} returns 200", ac == 200, f"HTTP {ac}")

        # 7.2 QR render stability & owned canvas check
        core_js = (WEB_DIR / "wwwroot" / "js" / "core-api.js").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L7",
            "core-api.js renders QR via local qrcode.min.js onto owned canvas & neutralizes img.onload",
            "/vendor/qr/qrcode.min.js" in core_js
            and "img.onload = null" in core_js
            and "quietZone = 12" in core_js
            and "ownedCanvas" in core_js,
        )
        guest.refresh_csrf("/S01AuthOtpGoogle")
        qr_c, qr_resp, _ = guest.post_json("/api/qr-login/create", {})
        qr_data = qr_resp.get("data", {}) if isinstance(qr_resp, dict) else {}
        qr_sid = qr_data.get("sessionId")
        qr_stat_c, qr_stat_raw, _ = guest.get(f"/api/qr-login/status/{qr_sid}", accept_json=True) if qr_sid else (0, "{}", {})
        qr_stat = json.loads(qr_stat_raw).get("status", "") if qr_stat_c == 200 else ""
        self.check(
            "L7",
            "QR login web session generates stable payload & Pending status (TTL >= 100s)",
            qr_c == 200
            and bool(qr_data.get("qrPayload"))
            and int(qr_data.get("expiresInSeconds", 0)) >= 100
            and qr_stat == "Pending",
            f"HTTP {qr_c}, ttl={qr_data.get('expiresInSeconds')}, status={qr_stat}",
        )

        # 7.3 Header 1-row, Auth state clarity, Compact AI Chat & No fake green banner
        gc_home, g_home_html, _ = guest.get("/")
        self.check(
            "L7",
            "Guest Home hides idle status banner & hides Admin menu",
            gc_home == 200 and 'class="status-banner status-200 hidden-banner"' in g_home_html and "Điều Hành" not in g_home_html,
            f"HTTP {gc_home}",
        )
        gc_ai, g_ai_html, _ = guest.get("/S13GeminiAiAssistant")
        self.check(
            "L7",
            "Floating AI widget auto-hides on full-page /S13GeminiAiAssistant",
            gc_ai == 200 and 'id="aiFloatingWidget"' not in g_ai_html,
            f"HTTP {gc_ai}",
        )
        u1_s01_c, u1_s01_html, _ = u1.get("/S01AuthOtpGoogle")
        self.check(
            "L7",
            "Authenticated S01 hides Login/Register tabs and activates Profile tab",
            u1_s01_c == 200
            and 'id="tabProfile"' in u1_s01_html
            and 'id="tabLogin"' not in u1_s01_html
            and 'id="tabRegister"' not in u1_s01_html,
            f"HTTP {u1_s01_c}",
        )
        adm_home_c, adm_home_html, _ = admin.get("/")
        self.check(
            "L7",
            "Authenticated Admin Header renders compact Account Dropdown & Điều Hành",
            adm_home_c == 200 and 'id="accountMenuToggle"' in adm_home_html and "Điều Hành" in adm_home_html,
            f"HTTP {adm_home_c}",
        )

        # 7.4 Admin System Icon Manager E2E (RBAC, validation, upload, toggle 2-way, reset)
        u1.refresh_csrf("/S02PersonalShelf")
        u1_get_c, _, _ = u1.get("/S22AdminAnalytics/GetIconSettings", accept_json=True)
        self.check("L7", "RBAC: User blocked 403 on GET GetIconSettings", u1_get_c == 403, f"HTTP {u1_get_c}")
        u1_tog_c, _, _ = u1.post_json("/S22AdminAnalytics/ToggleIconMode", {"iconKey": "brand_logo", "useCustomImage": True})
        self.check("L7", "RBAC: User blocked 403 on POST ToggleIconMode", u1_tog_c == 403, f"HTTP {u1_tog_c}")
        u1_rst_c, _, _ = u1.post_json("/S22AdminAnalytics/ResetIconToDefault", {"iconKey": "ALL"})
        self.check("L7", "RBAC: User blocked 403 on POST ResetIconToDefault", u1_rst_c == 403, f"HTTP {u1_rst_c}")

        admin.refresh_csrf("/S22AdminAnalytics")
        adm_get_c, adm_get_raw, _ = admin.get("/S22AdminAnalytics/GetIconSettings", accept_json=True)
        icon_list = json.loads(adm_get_raw).get("items", []) if adm_get_c == 200 else []
        self.check("L7", "Admin GET GetIconSettings returns all 18 system icons", adm_get_c == 200 and len(icon_list) == 18, f"HTTP {adm_get_c}, count={len(icon_list)}")

        # Ensure clean baseline before toggle test
        admin.post_json("/S22AdminAnalytics/ResetIconToDefault", {"iconKey": "brand_logo"})
        tog_empty_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleIconMode", {"iconKey": "brand_logo", "useCustomImage": True})
        self.check("L7", "ToggleIconMode right before upload rejected with 400", tog_empty_c == 400, f"HTTP {tog_empty_c}")

        bad_exe_c, _, _ = admin.post_multipart(
            "/S22AdminAnalytics/UploadCustomIcon",
            {"iconKey": "brand_logo"},
            ("file", "malware.exe", b"MZ\x90\x00fakeexe", "application/octet-stream"),
        )
        self.check("L7", "UploadCustomIcon blocks fake .exe with 400", bad_exe_c == 400, f"HTTP {bad_exe_c}")

        bad_svg_c, _, _ = admin.post_multipart(
            "/S22AdminAnalytics/UploadCustomIcon",
            {"iconKey": "brand_logo"},
            ("file", "xss.svg", b'<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>', "image/svg+xml"),
        )
        self.check("L7", "UploadCustomIcon blocks XSS <script> SVG with 400", bad_svg_c == 400, f"HTTP {bad_svg_c}")

        # Minimal valid 1x1 PNG bytes
        valid_png = bytes.fromhex(
            "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
            "0000000a49444154789c63000100000500010d0a2db40000000049454e44ae426082"
        )
        up_c, up_data, _ = admin.post_multipart(
            "/S22AdminAnalytics/UploadCustomIcon",
            {"iconKey": "brand_logo"},
            ("file", "custom-logo.png", valid_png, "image/png"),
        )
        custom_url = (up_data or {}).get("item", {}).get("customImageUrl", "") if isinstance(up_data, dict) else ""
        self.check(
            "L7",
            "UploadCustomIcon accepts valid PNG & enables custom mode",
            up_c == 200 and custom_url.startswith("/uploads/icons/brand_logo_"),
            f"HTTP {up_c}, url={custom_url}",
        )

        _, home_custom_html, _ = guest.get("/")
        self.check(
            "L7",
            "Home immediately renders uploaded custom brand_logo with alt & onerror",
            bool(custom_url) and f'src="{custom_url}"' in home_custom_html and 'onerror="' in home_custom_html,
            custom_url,
        )

        tog_left_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleIconMode", {"iconKey": "brand_logo", "useCustomImage": False})
        _, home_left_html, _ = guest.get("/")
        self.check(
            "L7",
            "ToggleIconMode Left (false) reverts Home brand_logo to default icon immediately",
            tog_left_c == 200 and custom_url not in home_left_html and "bi-book-half" in home_left_html,
            f"HTTP {tog_left_c}",
        )

        tog_right_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleIconMode", {"iconKey": "brand_logo", "useCustomImage": True})
        _, home_right_html, _ = guest.get("/")
        self.check(
            "L7",
            "ToggleIconMode Right (true) restores custom icon without re-upload",
            tog_right_c == 200 and custom_url in home_right_html,
            f"HTTP {tog_right_c}",
        )

        rst_all_c, _, _ = admin.post_json("/S22AdminAnalytics/ResetIconToDefault", {"iconKey": "ALL"})
        _, home_reset_html, _ = guest.get("/")
        self.check(
            "L7",
            "ResetIconToDefault ALL restores default icon cleanly",
            rst_all_c == 200 and custom_url not in home_reset_html,
            f"HTTP {rst_all_c}",
        )

        # 7.5 Blank DB (FactoryClean) + Stale Cookie Auto-Rejection + Admin@123 Login
        fc_c, _, _ = admin.post_json("/S22AdminAnalytics/ManageSampleData", {"actionMode": "FactoryClean"})
        self.check("L7", "Admin FactoryClean puts database into clean blank state", fc_c == 200, f"HTTP {fc_c}")

        blank_guest = SessionClient(self.base_url)
        blank_routes = ["/", "/S01AuthOtpGoogle", "/S02PersonalShelf", "/S06SearchDiscovery", "/S07LibraryCatalog", "/S22AdminAnalytics"]
        blank_failures: list[str] = []
        for route in blank_routes:
            bc, bhtml, _ = blank_guest.get(route)
            if bc != 200:
                blank_failures.append(f"{route}=HTTP {bc}")
            elif route == "/" and ("CLEAN STATE" in bhtml or "02_BookShare_SeedData" in bhtml):
                blank_failures.append("/ leaked legacy string")
        self.check("L7", "Blank DB: Guest visits 6 key routes with 0 HTTP 500 & 0 legacy text", not blank_failures, str(blank_failures))

        b404_c, b404_html, _ = blank_guest.get("/S02Pers")
        self.check(
            "L7",
            "Blank DB: /S02Pers returns friendly 404 suggesting /S02PersonalShelf without green 200 banner",
            b404_c == 404 and "/S02PersonalShelf" in b404_html and 'class="status-banner status-200 hidden-banner"' in b404_html,
            f"HTTP {b404_c}",
        )

        # u1's account was deleted by FactoryClean, yet u1 still holds its old AuthCookie!
        stale_home_c, _, _ = u1.get("/")
        stale_sess_c, stale_sess_raw, _ = u1.get("/api/mobile/session", accept_json=True)
        stale_auth = json.loads(stale_sess_raw).get("authenticated", True) if stale_sess_c == 200 else True
        self.check(
            "L7",
            "Blank DB: Stale cookie of deleted user auto-rejected (0 HTTP 500, authenticated=false)",
            stale_home_c == 200 and stale_sess_c == 200 and stale_auth is False,
            f"home={stale_home_c}, sess={stale_sess_c}, auth={stale_auth}",
        )

        fresh_admin = SessionClient(self.base_url)
        adm_blank_ok, adm_blank_fl = fresh_admin.login("admin@bookshare.vn", "Admin@123")
        adm_s22_blank_c, _, _ = fresh_admin.get("/S22AdminAnalytics")
        self.check(
            "L7",
            "Blank DB: Fresh Admin login (admin@bookshare.vn / Admin@123) succeeds immediately",
            adm_blank_ok and adm_s22_blank_c == 200,
            f"login={adm_blank_ok} ({adm_blank_fl}), s22={adm_s22_blank_c}",
        )

    def run_layer8_sprint4(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 8: Sprint 4 (No Real Bank .env, Virtual Wallet, 4-Tier Membership, QR Pay & Admin DeleteUser) ===")
        admin = actors["admin"]
        lib = actors["librarian"]
        u1 = actors["u1"]
        u2 = actors["u2"]

        # 8.1 Zero real bank / VNPay keys in .env.example & S17SandboxPaymentService.cs
        env_example = (ROOT_DIR / ".env.example").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L8",
            ".env.example removes all VNPay real-bank env variables",
            "VNPay__TmnCode" not in env_example
            and "VNPay__HashSecret" not in env_example
            and "VNPay__BaseUrl" not in env_example
            and "VNPay__ReturnUrl" not in env_example,
        )
        s17_svc = (WEB_DIR / "Services" / "Implementations" / "S17SandboxPaymentService.cs").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L8",
            "S17SandboxPaymentService has zero VNPay config key lookups",
            '["VNPay:' not in s17_svc and "VNPAY_HASH_SECRET" not in s17_svc,
        )
        core_js = (WEB_DIR / "wwwroot" / "js" / "core-api.js").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L8",
            "core-api.js syncs Bank-style QR Pay scanner with Web & Android Bottom Nav",
            "openQrPaymentScanner" in core_js
            and "openQrInvoiceConfirmDialog" in core_js
            and "BOOKSHARE-PAY:" in core_js,
        )

        # 8.2 Virtual Wallet Free Top-Up & 4-Tier Membership UI + InstantWallet flow
        c16, h16, _ = u2.get("/S16MembershipTier")
        h16_txt = html_lib.unescape(h16)
        self.check(
            "L8",
            "S16 displays all 4 membership tiers (Free, Standard, Premium, Scholar) & virtual balance",
            c16 == 200
            and "Độc Giả Cơ Bản" in h16_txt
            and "Hội Viên Đọc Sâu" in h16_txt
            and "Hội Viên Học Thuật" in h16_txt
            and "Nghiên Cứu VIP" in h16_txt
            and "Số dư Ví Tiền Ảo" in h16_txt,
            f"HTTP {c16}",
        )
        c17, h17, _ = u2.get("/S17SandboxPayment")
        self.check(
            "L8",
            "S17 displays Free Virtual Wallet Top-Up card & QR Payment camera button",
            c17 == 200 and 'id="walletTopUpCard"' in h17 and 'id="btnS17ScanQrPay"' in h17,
            f"HTTP {c17}",
        )

        u2.refresh_csrf("/S17SandboxPayment")
        bad_top_c, _, _ = u2.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 500})
        self.check("L8", "TopUpVirtualWallet rejects amount below 1,000đ (400)", bad_top_c == 400, f"HTTP {bad_top_c}")

        # Instant upgrade before topping up must fail 400 (insufficient balance)
        short_c, _, _ = u2.post_json(
            "/S16MembershipTier/SwitchMembership",
            {"targetTier": "Scholar", "durationMonths": 1, "paymentMode": "InstantWallet"},
        )
        self.check("L8", "S16 InstantWallet upgrade blocked 400 when virtual balance is insufficient", short_c == 400, f"HTTP {short_c}")

        top1_c, top1_j, _ = u2.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 100000, "note": "L8 Nap tien ao mien phi"})
        bal1 = float((top1_j or {}).get("balance", 0)) if isinstance(top1_j, dict) else 0
        self.check("L8", "TopUpVirtualWallet credits +100,000đ virtual balance free", top1_c == 200 and bal1 >= 100000, f"HTTP {top1_c}, bal={bal1}")

        std_c, std_j, std_raw = u2.post_json(
            "/S16MembershipTier/SwitchMembership",
            {"targetTier": "Standard", "durationMonths": 1, "paymentMode": "InstantWallet"},
        )
        bal_after_std = float((std_j or {}).get("walletBalance", -1)) if isinstance(std_j, dict) else -1
        _, h16_std, _ = u2.get("/S16MembershipTier")
        self.check(
            "L8",
            "S16 InstantWallet activates Standard tier (3 books) & deducts 29,000đ",
            std_c == 200 and abs((bal1 - 29000) - bal_after_std) < 1 and "Gói hiện tại: Standard" in h16_std and "3 cuốn" in h16_std,
            f"HTTP {std_c}, bal={bal_after_std}",
        )

        # 8.3 Bank-Style QR Payment Invoice Flow (Create -> Preview -> Anti-IDOR -> Shortfall -> TopUp -> Confirm -> Poll -> Anti-Replay)
        qr_ord_c, qr_ord_j, _ = u2.post_json(
            "/S16MembershipTier/SwitchMembership",
            {"targetTier": "Scholar", "durationMonths": 6, "paymentMode": "QrInvoice"},
        )
        pay_tx_id = (qr_ord_j or {}).get("createdTransactionId") if isinstance(qr_ord_j, dict) else None
        pay_qr = (qr_ord_j or {}).get("qrPayload", "") if isinstance(qr_ord_j, dict) else ""
        self.check(
            "L8",
            "S16 QrInvoice mode creates Pending Scholar 6M invoice (449,000đ) with BOOKSHARE-PAY QR payload",
            qr_ord_c == 200 and isinstance(pay_tx_id, int) and pay_qr == f"BOOKSHARE-PAY:{pay_tx_id}",
            f"HTTP {qr_ord_c}, qr={pay_qr}",
        )

        # Anti-IDOR: u1 cannot preview, confirm, or poll u2's invoice
        u1.refresh_csrf("/S17SandboxPayment")
        idor_prev_c, _, _ = u1.post_json("/S17SandboxPayment/PreviewQrInvoice", {"qrPayload": pay_qr})
        idor_conf_c, _, _ = u1.post_json("/S17SandboxPayment/ConfirmQrInvoice", {"transactionId": pay_tx_id})
        idor_poll_c, _, _ = u1.get(f"/S17SandboxPayment/InvoiceStatus?txId={pay_tx_id}", accept_json=True)
        self.check(
            "L8",
            "Anti-IDOR: Non-owner u1 blocked 403 on Preview/Confirm/Poll of u2 QR invoice",
            idor_prev_c == 403 and idor_conf_c == 403 and idor_poll_c == 403,
            f"prev={idor_prev_c}, conf={idor_conf_c}, poll={idor_poll_c}",
        )

        # Owner u2 previews invoice before enough money (bal ~121,000 vs 449,000)
        prev1_c, prev1_j, _ = u2.post_json("/S17SandboxPayment/PreviewQrInvoice", {"qrPayload": pay_qr})
        inv1 = (prev1_j or {}).get("invoice", {}) if isinstance(prev1_j, dict) else {}
        self.check(
            "L8",
            "PreviewQrInvoice returns full invoice details & flags insufficient balance accurately",
            prev1_c == 200 and float(inv1.get("amount", 0)) == 449000 and inv1.get("hasSufficientBalance") is False,
            f"HTTP {prev1_c}, inv={inv1}",
        )

        conf_short_c, _, _ = u2.post_json("/S17SandboxPayment/ConfirmQrInvoice", {"transactionId": pay_tx_id})
        self.check("L8", "ConfirmQrInvoice blocks 400 when virtual wallet balance < invoice amount", conf_short_c == 400, f"HTTP {conf_short_c}")

        # Top up +400,000đ -> Preview shows sufficient -> Confirm succeeds -> InvoiceStatus Completed -> Replay blocked 400
        u2.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 400000})
        prev2_c, prev2_j, _ = u2.post_json("/S17SandboxPayment/PreviewQrInvoice", {"qrPayload": pay_qr})
        inv2 = (prev2_j or {}).get("invoice", {}) if isinstance(prev2_j, dict) else {}
        self.check(
            "L8",
            "PreviewQrInvoice reflects updated wallet balance & sufficient flag",
            prev2_c == 200 and inv2.get("hasSufficientBalance") is True,
            f"HTTP {prev2_c}, bal={inv2.get('walletBalance')}",
        )

        conf_ok_c, conf_ok_j, conf_raw = u2.post_json("/S17SandboxPayment/ConfirmQrInvoice", {"transactionId": pay_tx_id})
        poll_c, poll_raw, _ = u2.get(f"/S17SandboxPayment/InvoiceStatus?txId={pay_tx_id}", accept_json=True)
        poll_status = json.loads(poll_raw).get("status") if poll_c == 200 else ""
        _, h16_sch, _ = u2.get("/S16MembershipTier")
        self.check(
            "L8",
            "ConfirmQrInvoice deducts wallet, marks Completed for Web polling & activates Scholar (10 books)",
            conf_ok_c == 200 and poll_status == "Completed" and "Gói hiện tại: Scholar" in h16_sch and "10 cuốn" in h16_sch,
            f"conf={conf_ok_c}, poll={poll_status}",
        )

        replay_qr_c, _, _ = u2.post_json("/S17SandboxPayment/ConfirmQrInvoice", {"transactionId": pay_tx_id})
        self.check("L8", "ConfirmQrInvoice blocks duplicate replay payment idempotently (400)", replay_qr_c == 400, f"HTTP {replay_qr_c}")

        # 8.4 Admin Delete User (RBAC, self-delete block, active-loan safety guard, clean atomic delete + stale cookie reject)
        u1_del_c, _, _ = u1.post_json("/S22AdminAnalytics/DeleteUser", {"userId": "u-user-03"})
        self.check("L8", "RBAC: User blocked 403 on POST /S22AdminAnalytics/DeleteUser", u1_del_c == 403, f"HTTP {u1_del_c}")

        admin.refresh_csrf("/S22AdminAnalytics")
        self_del_c, _, _ = admin.post_json("/S22AdminAnalytics/DeleteUser", {"userId": "u-admin-01"})
        self.check("L8", "Admin blocked 400 from deleting root/self account u-admin-01", self_del_c == 400, f"HTTP {self_del_c}")

        # Register a disposable member to verify both active-loan protection (400) and clean atomic deletion (200)
        disp_email = f"delete.l8.{int(time.time())}@cmc.edu.vn"
        disp = SessionClient(self.base_url)
        disp.refresh_csrf("/S01AuthOtpGoogle")
        reg_c, _, _ = disp.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Tai Khoan Kiem Thu Xoa L8", "Email": disp_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        disp.refresh_csrf("/S17SandboxPayment")
        disp.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 50000})

        _, s22_html, _ = admin.get("/S22AdminAnalytics")
        # Extract disposable user's ID from the S22 table button
        m_uid = re.search(
            r'class="[^"]*js-btn-delete-user[^"]*"[^>]*data-user-id="([^"]+)"[^>]*data-user-name="Tai Khoan Kiem Thu Xoa L8"',
            s22_html,
        )
        disp_uid = m_uid.group(1) if m_uid else ""
        self.check("L8", "S22 renders Delete User button for newly registered member", reg_c == 200 and bool(disp_uid), f"uid={disp_uid}")

        if disp_uid:
            # Check out an available system copy to disp_uid via Librarian Desk -> verify DeleteUser is blocked 400 while holding book!
            _, desk_l8_html, _ = lib.get("/S21LibrarianDesk")
            avail_candidates: list[str] = []
            for m in re.finditer(r'class="[^"]*js-sample-barcode[^"]*"[^>]*data-code="([^"]+)"[^>]*>[^<]*\(Available\)', desk_l8_html):
                avail_candidates.append(m.group(1).strip())
            for tr_blk in desk_l8_html.split("<tr"):
                if "bg-success" in tr_blk and "Available" in tr_blk:
                    cm = re.search(r"<code>([^<]+)</code>", tr_blk)
                    if cm:
                        avail_candidates.append(cm.group(1).strip())
            checked_out_bc = ""
            last_co_err = ""
            for cand_bc in list(dict.fromkeys(avail_candidates))[:10]:
                co_c, _, co_raw = lib.post_json(
                    "/S21LibrarianDesk/QuickScanBarcode",
                    {"barcode": cand_bc, "deskAction": "Checkout", "borrowerUserId": disp_uid},
                )
                if co_c == 200:
                    checked_out_bc = cand_bc
                    break
                last_co_err = f"{cand_bc}:{co_c}:{co_raw[:80]}"
            blk_del_c, _, _ = admin.post_json("/S22AdminAnalytics/DeleteUser", {"userId": disp_uid}) if checked_out_bc else (0, None, "")
            self.check(
                "L8",
                "Admin DeleteUser blocked 400 while user holds active unreturned book",
                bool(checked_out_bc) and blk_del_c == 400,
                f"bc={checked_out_bc}, HTTP {blk_del_c}, err={last_co_err}",
            )
            if checked_out_bc:
                lib.post_json(
                    "/S21LibrarianDesk/QuickScanBarcode",
                    {"barcode": checked_out_bc, "deskAction": "Checkin", "returnCondition": "Good"},
                )

            ok_del_c, _, ok_del_raw = admin.post_json("/S22AdminAnalytics/DeleteUser", {"userId": disp_uid})
            self.check("L8", "Admin DeleteUser atomically removes clean user & dependent FK rows (200)", ok_del_c == 200, ok_del_raw)

            # Deleted user's old cookie must be auto-rejected with 0 HTTP 500!
            after_del_sess_c, after_del_raw, _ = disp.get("/api/mobile/session", accept_json=True)
            after_del_auth = json.loads(after_del_raw).get("authenticated", True) if after_del_sess_c == 200 else True
            self.check(
                "L8",
                "Deleted user's active session cookie auto-rejected by OnValidatePrincipal (authenticated=false)",
                after_del_sess_c == 200 and after_del_auth is False,
                f"HTTP {after_del_sess_c}, auth={after_del_auth}",
            )


def wait_for_server(base_url: str, timeout_s: float = 40.0) -> bool:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(base_url + "/", timeout=3) as resp:
                if resp.status < 500:
                    return True
        except Exception:
            time.sleep(0.5)
    return False


def pick_free_port(preferred: int = 5098) -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        if s.connect_ex(("127.0.0.1", preferred)) != 0:
            return preferred
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def main() -> int:
    parser = argparse.ArgumentParser(description="BookShare E2E QA Automation Bot")
    parser.add_argument("--base-url", default="", help="Running BookShare URL (if not using --autostart)")
    parser.add_argument("--autostart", action="store_true", help="Build and start Kestrel automatically on a free port")
    parser.add_argument("--no-build", action="store_true", help="Skip dotnet build when --autostart is set")
    parser.add_argument("--json-out", default="", help="Optional path to dump structured JSON results")
    args = parser.parse_args()

    proc: Optional[subprocess.Popen[bytes]] = None
    try:
        if args.autostart:
            if not args.no_build:
                print("Building BookShare.sln...")
                b = subprocess.run(
                    ["dotnet", "build", "BookShare.sln", "--nologo"],
                    cwd=str(ROOT_DIR),
                )
                if b.returncode != 0:
                    print("BUILD FAILED!")
                    return 2

            port = pick_free_port(5098)
            base_url = f"http://127.0.0.1:{port}"
            print(f"Starting BookShare.Web at {base_url}...")
            proc = subprocess.Popen(
                [
                    "dotnet",
                    "run",
                    "--project",
                    str(WEB_DIR / "BookShare.Web.csproj"),
                    "--no-build",
                    "--no-launch-profile",
                    "--",
                    "--urls",
                    base_url,
                ],
                cwd=str(ROOT_DIR),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            if not wait_for_server(base_url, 45.0):
                print("Server failed to start within 45s.")
                return 3
        else:
            base_url = args.base_url or "http://127.0.0.1:5087"
            if not wait_for_server(base_url, 10.0):
                print(f"Cannot reach server at {base_url}")
                return 3

        runner = QARunner(base_url)
        runner.run_layer1_static()
        runner.run_layer2_guest_and_oauth()
        actors = runner.run_layer3_auth_rbac_idor()
        runner.run_layer4_business_loops(actors)
        runner.run_layer6_sprint3(actors)
        runner.run_layer8_sprint4(actors)
        runner.run_layer7_sprint35(actors)
        runner.run_layer5_cleanup_and_integrity(actors["admin"])

        passed = sum(1 for r in runner.results if r.passed)
        failed = [r for r in runner.results if not r.passed]
        total = len(runner.results)

        print(f"\n==================== SUMMARY ====================")
        print(f"TOTAL: {total} | PASS: {passed} | FAIL: {len(failed)}")
        if failed:
            print("\nFAILED CHECKS:")
            for r in failed:
                print(f" - [{r.layer}] {r.name}: {r.detail}")

        if args.json_out:
            Path(args.json_out).write_text(
                json.dumps(
                    {
                        "total": total,
                        "passed": passed,
                        "failed": len(failed),
                        "checks": [
                            {"layer": r.layer, "name": r.name, "passed": r.passed, "detail": r.detail}
                            for r in runner.results
                        ],
                    },
                    ensure_ascii=False,
                    indent=2,
                ),
                encoding="utf-8",
            )

        return 0 if not failed else 1
    finally:
        if proc is not None:
            proc.terminate()
            try:
                proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                proc.kill()


if __name__ == "__main__":
    raise SystemExit(main())
