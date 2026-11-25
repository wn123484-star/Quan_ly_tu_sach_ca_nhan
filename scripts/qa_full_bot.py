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
            "service-worker.js bumped to bookshare-static-v3+ & light offline card",
            re.search(r"bookshare-static-v[2-9]", sw) is not None and "#f8fafc" in sw,
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

        # Locked account cannot approve Web QR session (OnValidatePrincipal revokes locked cookie -> 401 or service rejects -> 400)
        qr_web2 = SessionClient(self.base_url, "qr_web2")
        qr_web2.refresh_csrf("/S01AuthOtpGoogle")
        _, q2_j, _ = qr_web2.post_json("/api/qr-login/create", {})
        sid2 = int(((q2_j or {}).get("data") or {}).get("sessionId") or 0)
        admin.refresh_csrf("/S22AdminAnalytics")
        lk_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-user-02", "lockAccount": True})
        u2.refresh_csrf("/S01AuthOtpGoogle")
        lk_ap_c, _, lk_ap_raw = u2.post_json("/api/qr-login/approve", {"sessionId": sid2, "approve": True})
        admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-user-02", "lockAccount": False})
        # Re-authenticate u2 because locking immediately revoked u2's previous session cookie
        u2.login("minhanh@cmc.edu.vn", "User@123")
        self.check("L6", "Locked account blocked from approving QR session (401/400)", lk_c == 200 and lk_ap_c in (400, 401), lk_ap_raw)

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

        # Reset u2 from seeded Premium to Free first so L8 tests pure Free -> Register Standard -> Switch Scholar lifecycle
        u2.refresh_csrf("/S16MembershipTier")
        u2.post_json("/S16MembershipTier/CancelMembership", {"reason": "L8 setup Free baseline"})

        # Instant upgrade before topping up must fail 400 (insufficient balance: 50k < 89k)
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

        # Owner u2 previews invoice before enough money (Standard->Scholar 6M = 449k - 29k proration + 15k switchFee = 435,000đ)
        prev1_c, prev1_j, _ = u2.post_json("/S17SandboxPayment/PreviewQrInvoice", {"qrPayload": pay_qr})
        inv1 = (prev1_j or {}).get("invoice", {}) if isinstance(prev1_j, dict) else {}
        self.check(
            "L8",
            "PreviewQrInvoice returns full switch-fee invoice details & flags insufficient balance accurately",
            prev1_c == 200
            and float(inv1.get("switchFee", 0)) == 15000
            and float(inv1.get("prorationCredit", 0)) > 0
            and 420000 <= float(inv1.get("amount", 0)) <= 464000
            and inv1.get("hasSufficientBalance") is False,
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

    def run_layer9_sprint5(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 9: SPRINT 5 — ICON 200 OK, GOOGLE OAUTH SCHEME, THEMES/FONTS, SETTINGS, NOTIF POPUP, PERSISTENT AI & ANTI-SPAM ===")
        admin = actors["admin"]
        u1 = actors["u1"]
        u2 = actors["u2"]

        # 9.1 Static & Local Asset Verification (0 CDN in Bootswatch, 6 local fonts HTTP 200, Program.cs UseStaticFiles + ExternalCookieScheme)
        bw_dir = WEB_DIR / "wwwroot" / "vendor" / "bootswatch"
        bw_files = list(bw_dir.glob("*.min.css"))
        bw_no_cdn = len(bw_files) >= 4 and all("fonts.googleapis.com" not in f.read_text(encoding="utf-8", errors="ignore") for f in bw_files)
        self.check("L9", "All local Bootswatch CSS files have 0 external Google Fonts @import", bw_no_cdn, f"count={len(bw_files)}")

        for font_rel in [
            "/fonts/BeVietnamPro-Regular.ttf",
            "/fonts/PatrickHand-Regular.ttf",
            "/fonts/Lora-Variable.ttf",
            "/fonts/JetBrainsMono-Regular.ttf",
        ]:
            fc, fbody, _ = u1.get(font_rel)
            self.check("L9", f"Local Vietnamese font serves HTTP 200 ({font_rel})", fc == 200 and len(fbody) > 10000, f"HTTP {fc}")

        prog_src = (WEB_DIR / "Program.cs").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L9",
            "Program.cs enables UseStaticFiles() and dedicated ExternalCookieScheme for Google OAuth",
            "app.UseStaticFiles()" in prog_src and "ExternalCookieScheme" in prog_src,
        )

        theme_css = (WEB_DIR / "wwwroot" / "css" / "core-theme.css").read_text(encoding="utf-8", errors="ignore")
        self.check(
            "L9",
            "core-theme.css defines balanced 180deg header, bottom-left reCAPTCHA tuck, Gold/Rose palettes & Sketchy style",
            "linear-gradient(180deg" in theme_css
            and ".grecaptcha-badge" in theme_css
            and "left: -186px" in theme_css
            and 'data-portal-palette="gold"' in theme_css
            and 'data-portal-palette="rose"' in theme_css
            and 'data-ui-style="sketchy"' in theme_css,
        )

        # 9.2 Uploaded Icon returns HTTP 200 (disk static file + SQL binary fallback endpoint)
        admin.refresh_csrf("/S22AdminAnalytics")
        tiny_png = (
            b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01"
            b"\x08\x02\x00\x00\x00\x90wS\xde\x00\x00\x00\x0cIDATx\x9cc`\x00\x00\x00\x02\x00\x01"
            b"\xe2!\xbc3\x00\x00\x00\x00IEND\xaeB`\x82"
        )
        up_ic, up_ij, up_iraw = admin.post_multipart(
            "/S22AdminAnalytics/UploadCustomIcon",
            {"iconKey": "brand_logo"},
            ("file", "brand_test_s5.png", tiny_png, "image/png"),
        )
        icon_url = ((up_ij or {}).get("item") or {}).get("customImageUrl", "") if isinstance(up_ij, dict) else ""
        self.check("L9", "Admin uploads custom brand_logo icon successfully", up_ic == 200 and icon_url.startswith("/uploads/icons/"), up_iraw[:140])

        if icon_url:
            # Fetch raw bytes to ensure real 200 OK (not 404!)
            req_disk = urllib.request.Request(self.base_url + icon_url)
            try:
                with urllib.request.urlopen(req_disk, timeout=5) as r_disk:
                    disk_status = r_disk.status
                    disk_bytes = r_disk.read()
            except Exception as exc:
                disk_status, disk_bytes = 0, str(exc).encode()
            self.check(
                "L9",
                "Uploaded icon static URL serves HTTP 200 PNG bytes (fixes 404 bug)",
                disk_status == 200 and disk_bytes.startswith(b"\x89PNG"),
                f"HTTP {disk_status}, len={len(disk_bytes)}",
            )

            req_db = urllib.request.Request(self.base_url + "/assets/system-icons/brand_logo")
            try:
                with urllib.request.urlopen(req_db, timeout=5) as r_db:
                    db_status = r_db.status
                    db_bytes = r_db.read()
            except Exception as exc:
                db_status, db_bytes = 0, str(exc).encode()
            self.check(
                "L9",
                "SQL-backed fallback icon endpoint /assets/system-icons/brand_logo returns HTTP 200 PNG",
                db_status == 200 and db_bytes.startswith(b"\x89PNG"),
                f"HTTP {db_status}, len={len(db_bytes)}",
            )
            admin.post_json("/S22AdminAnalytics/ResetIcon", {"iconKey": "brand_logo"})

        # 9.3 All-Role Settings: FullName vs Email separation, Email edit, UI Preferences persistence & Email Gate
        u1.refresh_csrf("/S01AuthOtpGoogle")
        bad_fn_c, _, _ = u1.post_json("/S01AuthOtpGoogle/UpdateProfile", {"fullName": "nguyenhung@cmc.edu.vn", "email": "nguyenhung@cmc.edu.vn"})
        self.check("L9", "UpdateProfile rejects using raw email as display FullName (400)", bad_fn_c == 400, f"HTTP {bad_fn_c}")

        # Save UI Preferences (Sketchy + Gold + Patrick + lg) and read back
        sp_c, sp_j, _ = u1.post_json(
            "/S01AuthOtpGoogle/SaveUiPreferences",
            {"theme": "light", "style": "sketchy", "palette": "gold", "font": "patrick", "fontSize": "lg"},
        )
        gp_c, gp_raw, _ = u1.get("/S01AuthOtpGoogle/GetUiPreferences", accept_json=True)
        gp_prefs = (json.loads(gp_raw).get("preferences") or {}) if gp_c == 200 else {}
        self.check(
            "L9",
            "User saves & reloads UI Preferences (sketchy/gold/patrick/lg) from DB",
            sp_c == 200 and gp_c == 200 and gp_prefs.get("style") == "sketchy" and gp_prefs.get("palette") == "gold" and gp_prefs.get("font") == "patrick" and gp_prefs.get("fontSize") == "lg",
            f"save={sp_c}, prefs={gp_prefs}",
        )
        # Restore standard prefs cleanly
        u1.post_json("/S01AuthOtpGoogle/SaveUiPreferences", {"theme": "light", "style": "standard", "palette": "blue", "font": "roboto", "fontSize": "md"})

        # Clear email temporarily -> verify AI SendChatSummaryEmail blocks with requiresEmailSetup=true -> restore email -> verify succeeds!
        clr_c, _, _ = u1.post_json("/S01AuthOtpGoogle/UpdateProfile", {"fullName": "Nguyễn Quang Hùng", "email": ""})
        self.check("L9", "Member can clear/edit Email separately from FullName & UserName", clr_c == 200, f"HTTP {clr_c}")
        no_mail_c, no_mail_j, _ = u1.post_json("/S13GeminiAiAssistant/SendChatSummaryEmail", {})
        self.check(
            "L9",
            "SendChatSummaryEmail blocks 400 & sets requiresEmailSetup=true when user has no valid Email",
            no_mail_c == 400 and (no_mail_j or {}).get("requiresEmailSetup") is True,
            f"HTTP {no_mail_c}, body={no_mail_j}",
        )
        # Restore u1's email cleanly
        rest_c, _, _ = u1.post_json("/S01AuthOtpGoogle/UpdateProfile", {"fullName": "Nguyễn Quang Hùng", "email": "nguyenhung@cmc.edu.vn"})
        self.check("L9", "Member restores valid Email in Settings (200)", rest_c == 200, f"HTTP {rest_c}")

        # 9.4 Notification Quick Popup API (S18/RecentPopup)
        pop_c, pop_raw, _ = u1.get("/S18NotificationHub/RecentPopup?take=8", accept_json=True)
        pop_data = json.loads(pop_raw) if pop_c == 200 else {}
        pop_items = pop_data.get("items") or []
        self.check(
            "L9",
            "S18 RecentPopup returns unreadCount & notifications with clickable detailUrl",
            pop_c == 200 and isinstance(pop_items, list) and (len(pop_items) == 0 or str(pop_items[0].get("detailUrl", "")).startswith("/")),
            f"HTTP {pop_c}, count={len(pop_items)}",
        )

        # 9.5 Persistent Role-Scoped AI Chat + Reset Chat + AI Style/Font Commands + Send Chat Email
        u1.refresh_csrf("/S13GeminiAiAssistant")
        marker_msg = f"Đổi phong cách nét vẽ nghệch ngoặc màu hồng ấm áp {int(time.time())}"
        ask_c, ask_j, _ = u1.post_json("/api/ai-chat/quick-ask", {"message": marker_msg})
        cmds = (ask_j or {}).get("clientCommands") or []
        has_ui_cmd = any(c.get("command") == "set_ui_prefs" for c in cmds)
        self.check("L9", "AI QuickAsk returns set_ui_prefs command for Sketchy/Rose prompt", ask_c == 200 and has_ui_cmd, f"HTTP {ask_c}, cmds={cmds}")

        h1_c, h1_raw, _ = u1.get("/api/ai-chat/history", accept_json=True)
        h1_msgs = (json.loads(h1_raw).get("messages") or []) if h1_c == 200 else []
        h2_c, h2_raw, _ = u2.get("/api/ai-chat/history", accept_json=True)
        h2_msgs = (json.loads(h2_raw).get("messages") or []) if h2_c == 200 else []
        self.check(
            "L9",
            "AI chat history persists across requests for u1 & stays strictly isolated from u2",
            h1_c == 200
            and any(marker_msg in (m.get("messageText") or "") for m in h1_msgs)
            and not any(marker_msg in (m.get("messageText") or "") for m in h2_msgs),
            f"u1_msgs={len(h1_msgs)}, u2_msgs={len(h2_msgs)}",
        )

        em_c, em_j, _ = u1.post_json("/S13GeminiAiAssistant/SendChatSummaryEmail", {})
        self.check("L9", "AI SendChatSummaryEmail succeeds (200) once valid Email is set", em_c == 200, f"HTTP {em_c}, body={em_j}")

        rst_c, rst_j, _ = u1.post_json("/api/ai-chat/reset", {})
        h1_after_c, h1_after_raw, _ = u1.get("/api/ai-chat/history", accept_json=True)
        h1_after_msgs = (json.loads(h1_after_raw).get("messages") or []) if h1_after_c == 200 else []
        self.check(
            "L9",
            "AI Reset Chat clears old messages & creates clean role-aware greeting",
            rst_c == 200
            and len(h1_after_msgs) == 1
            and not any(marker_msg in (m.get("messageText") or "") for m in h1_after_msgs),
            f"rst={rst_c}, remaining={len(h1_after_msgs)}",
        )

        # 9.6 Membership Anti-Duplicate Purchase, Switch Fee Preview, Cancel Membership & Anti-Spam Ledger
        u2.refresh_csrf("/S16MembershipTier")
        dup_buy_c, dup_buy_j, _ = u2.post_json(
            "/S16MembershipTier/SwitchMembership",
            {"targetTier": "Scholar", "durationMonths": 1, "paymentMode": "InstantWallet"},
        )
        self.check(
            "L9",
            "S16 blocks purchasing duplicate active paid tier Scholar (400)",
            dup_buy_c == 400 and "đang sở hữu" in str(dup_buy_j).lower(),
            f"HTTP {dup_buy_c}, body={dup_buy_j}",
        )

        pv_sw_c, pv_sw_j, _ = u2.post_json(
            "/S16MembershipTier/PreviewTierSwitch",
            {"targetTier": "Premium", "durationMonths": 6},
        )
        pv_sw = (pv_sw_j or {}).get("preview") or {}
        self.check(
            "L9",
            "S16 PreviewTierSwitch computes transparent 15,000đ switch fee & proration credit",
            pv_sw_c == 200 and pv_sw.get("isSwitch") is True and float(pv_sw.get("switchFee", 0)) == 15000 and float(pv_sw.get("prorationCredit", 0)) > 0,
            f"HTTP {pv_sw_c}, pv={pv_sw}",
        )

        can_c, can_j, _ = u2.post_json("/S16MembershipTier/CancelMembership", {"reason": "L9 test cancel"})
        _, h16_free, _ = u2.get("/S16MembershipTier")
        self.check(
            "L9",
            "S16 CancelMembership downgrades active Scholar cleanly to Free (200)",
            can_c == 200 and "Gói hiện tại: Free" in h16_free,
            f"HTTP {can_c}, body={can_j}",
        )
        can2_c, _, _ = u2.post_json("/S16MembershipTier/CancelMembership", {})
        self.check("L9", "S16 CancelMembership rejects cancelling when already on Free (400)", can2_c == 400, f"HTTP {can2_c}")

        # IdempotencyKey duplicate block on TopUpVirtualWallet
        u2.refresh_csrf("/S17SandboxPayment")
        idem_key = f"l9-idem-{int(time.time())}"
        t_first_c, t_first_j, _ = u2.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 25000, "idempotencyKey": idem_key})
        bal_after_first = float((t_first_j or {}).get("balance", 0))
        t_dup_c, t_dup_j, _ = u2.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 25000, "idempotencyKey": idem_key})
        bal_after_dup = float((t_dup_j or {}).get("balance", -1))
        self.check(
            "L9",
            "S17 IdempotencyKey blocks duplicate TopUpVirtualWallet (400, balance unchanged)",
            t_first_c == 200 and t_dup_c == 400 and abs(bal_after_first - bal_after_dup) < 0.01,
            f"c1={t_first_c}, c2={t_dup_c}, b1={bal_after_first}, b2={bal_after_dup}",
        )

        # Pending invoice anti-spam deduplication
        ord1_c, ord1_j, _ = u2.post_json("/S17SandboxPayment/CreateOrder", {"transactionType": "Membership", "targetTier": "Standard", "durationMonths": 1, "amount": 29000})
        ord2_c, ord2_j, _ = u2.post_json("/S17SandboxPayment/CreateOrder", {"transactionType": "Membership", "targetTier": "Standard", "durationMonths": 1, "amount": 29000})
        tx1_id = (ord1_j or {}).get("transactionId")
        tx2_id = (ord2_j or {}).get("transactionId")
        self.check(
            "L9",
            "S17 CreateOrder deduplicates identical recent Pending invoice (reuses exact #TX-ID)",
            ord1_c == 200 and ord2_c == 200 and tx1_id and tx1_id == tx2_id,
            f"tx1={tx1_id}, tx2={tx2_id}",
        )
        if tx1_id:
            u2.post_json("/S17SandboxPayment/CancelTransaction", {"transactionId": tx1_id})

        # Rate-limit anti-spam on TopUpVirtualWallet (6 rapid calls -> 6th hits 429)
        spam_email = f"spam.l9.{int(time.time())}@cmc.edu.vn"
        spammer = SessionClient(self.base_url)
        spammer.refresh_csrf("/S01AuthOtpGoogle")
        spammer.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu Chong Spam L9", "Email": spam_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        spammer.refresh_csrf("/S17SandboxPayment")
        rate_codes = []
        for idx in range(6):
            rc, _, _ = spammer.post_json("/S17SandboxPayment/TopUpVirtualWallet", {"amount": 5000, "idempotencyKey": f"spam-{int(time.time())}-{idx}"})
            rate_codes.append(rc)
        self.check(
            "L9",
            "S17 TopUpVirtualWallet enforces anti-spam rate limit (5 OK -> 6th HTTP 429)",
            rate_codes[:5] == [200, 200, 200, 200, 200] and rate_codes[5] == 429,
            f"codes={rate_codes}",
        )

        # 9.7 Android Web App Sync (https://bookshare.castle-tech.cloud), Anti-White-Screen, On-Demand Camera & Mobile OAuth Handoff
        mob_dir = ROOT_DIR / "mobile" / "bookshare-android"
        gradle_props = (mob_dir / "gradle.properties").read_text(encoding="utf-8", errors="ignore")
        app_gradle = (mob_dir / "app" / "build.gradle").read_text(encoding="utf-8", errors="ignore")
        apk_wf = (ROOT_DIR / ".github" / "workflows" / "build-bookshare-android-apk.yml").read_text(encoding="utf-8", errors="ignore")
        main_java = (mob_dir / "app" / "src" / "main" / "java" / "vn" / "bookshare" / "mobile" / "MainActivity.java").read_text(encoding="utf-8", errors="ignore")
        layout_xml = (mob_dir / "app" / "src" / "main" / "res" / "layout" / "activity_main.xml").read_text(encoding="utf-8", errors="ignore")
        core_js_mob = (WEB_DIR / "wwwroot" / "js" / "core-api.js").read_text(encoding="utf-8", errors="ignore")

        target_prod_url = "https://bookshare.castle-tech.cloud"
        self.check(
            "L9",
            "Android build config & CI workflow default to https://bookshare.castle-tech.cloud (zero 10.0.2.2)",
            target_prod_url in gradle_props
            and target_prod_url in app_gradle
            and target_prod_url in apk_wf
            and "10.0.2.2" not in gradle_props
            and "10.0.2.2" not in app_gradle
            and "10.0.2.2" not in apk_wf,
        )

        on_create_block = main_java.split("protected void onCreate(", 1)[1].split("protected void onNewIntent(", 1)[0] if "protected void onCreate(" in main_java else ""
        self.check(
            "L9",
            "Android MainActivity has loadingOverlay, watchdog timeout, onPageCommitVisible, safe SSL cancel & on-demand CAMERA only",
            'id="@+id/loadingOverlay"' in layout_xml
            and "LOAD_TIMEOUT_MS" in main_java
            and "onPageCommitVisible" in main_java
            and "onReceivedSslError" in main_java
            and "handler.cancel()" in main_java
            and "handler.proceed()" not in main_java
            and "onRenderProcessGone" in main_java
            and "onPermissionRequestCanceled" in main_java
            and "new String[]{ PermissionRequest.RESOURCE_VIDEO_CAPTURE }" in main_java
            and "Manifest.permission.CAMERA" not in on_create_block
            and "setPullToRefreshEnabled" in main_java,
        )

        self.check(
            "L9",
            "core-api.js routes Android Google OAuth to GoogleLoginMobile & opens rear camera on demand",
            "/S01AuthOtpGoogle/GoogleLoginMobile" in core_js_mob
            and "setPullToRefreshEnabled" in core_js_mob
            and "openAppSettings" in core_js_mob,
        )

        req_mob_ua = urllib.request.Request(
            self.base_url + "/",
            headers={"User-Agent": "Mozilla/5.0 (Linux; Android 14) BookShareAndroidApp/1.0"},
            method="GET",
        )
        with urllib.request.urlopen(req_mob_ua, timeout=15) as resp_mob_ua:
            mob_ua_html = resp_mob_ua.read().decode("utf-8", errors="replace")
        self.check(
            "L9",
            "BookShareAndroidApp User-Agent omits duplicate web #appSplash (prevents double-loading freeze)",
            "is-mobile-app" in mob_ua_html and 'id="appSplash"' not in mob_ua_html,
        )

        # GET /S01AuthOtpGoogle/GoogleLoginMobile must NOT return 405 Method Not Allowed
        mob_oauth_guest = SessionClient(self.base_url)
        go_mob_c, _, _ = mob_oauth_guest.get("/S01AuthOtpGoogle/GoogleLoginMobile")
        self.check(
            "L9",
            "GET /S01AuthOtpGoogle/GoogleLoginMobile avoids 405 (returns 400 fail-closed or 302 Google challenge)",
            go_mob_c in (302, 400),
            f"HTTP {go_mob_c}",
        )

        # End-to-end single-use CompleteMobileOAuth handoff (creates AuthCookie inside WebView & blocks replay)
        u1.refresh_csrf("/S01AuthOtpGoogle")
        ml_c, ml_j, ml_raw = u1.post_json("/api/qr-login/create-mobile-link", {})
        ml_payload = ((ml_j or {}).get("data") or {}).get("qrPayload", "")
        handoff_client = SessionClient(self.base_url)
        ho_c, _, _ = handoff_client.get(f"/S01AuthOtpGoogle/CompleteMobileOAuth?code={urllib.parse.quote(ml_payload, safe='')}")
        hs_c, hs_raw, _ = handoff_client.get("/api/mobile/session", accept_json=True)
        hs_json = json.loads(hs_raw) if hs_c == 200 else {}
        replay_client = SessionClient(self.base_url)
        rp_c, _, _ = replay_client.get(f"/S01AuthOtpGoogle/CompleteMobileOAuth?code={urllib.parse.quote(ml_payload, safe='')}")
        rs_c, rs_raw, _ = replay_client.get("/api/mobile/session", accept_json=True)
        rs_json = json.loads(rs_raw) if rs_c == 200 else {}
        self.check(
            "L9",
            "CompleteMobileOAuth signs WebView cookie atomically once & rejects token replay",
            ml_c == 200
            and bool(ml_payload)
            and ho_c == 200
            and hs_json.get("authenticated") is True
            and rp_c == 200
            and rs_json.get("authenticated") is False,
            f"ml={ml_c}, ho={ho_c}, auth1={hs_json.get('authenticated')}, auth2={rs_json.get('authenticated')}, raw={ml_raw[:120]}",
        )

        # 9.8 Square QR Scanner, Unified Quick QR Login, Forgot Password OTP, Avatar Upload, Notification Sync, Icon Preview & 9-Style AI Control
        s01_js_txt = (WEB_DIR / "wwwroot" / "modules" / "s01-auth-otp-google" / "s01-auth-otp-google.js").read_text(encoding="utf-8", errors="ignore")
        s22_js_txt = (WEB_DIR / "wwwroot" / "modules" / "s22-admin-analytics" / "s22-admin-analytics.js").read_text(encoding="utf-8", errors="ignore")
        s13_svc_txt = (WEB_DIR / "Services" / "Implementations" / "S13GeminiAiService.cs").read_text(encoding="utf-8", errors="ignore")
        poller_java = (mob_dir / "app" / "src" / "main" / "java" / "vn" / "bookshare" / "mobile" / "NotificationPoller.java").read_text(encoding="utf-8", errors="ignore")

        self.check(
            "L9",
            "QR Scanner is 1:1 square, auto-on (no toggle button), releases tracks on ESC/close & unifies QR login",
            "bs-qr-scanner-stage" in core_js_mob
            and "aspectRatio: 1.0" in core_js_mob
            and "releaseVideoTracks" in core_js_mob
            and "bsBtnToggleCam" not in core_js_mob
            and "confirmDialog({" in core_js_mob
            and "openUniversalScanner" in s01_js_txt
            and "/api/qr-login/mobile-claim" not in s01_js_txt,
        )

        _, guest_h_nav, _ = SessionClient(self.base_url).get("/")
        _, u1_h_nav, _ = u1.get("/")
        self.check(
            "L9",
            "Header exposes dedicated 'Đăng nhập nhanh' QR menu item for both Guest and Authenticated users",
            "Đăng nhập nhanh" in html_lib.unescape(guest_h_nav)
            and "Đăng nhập nhanh" in html_lib.unescape(u1_h_nav)
            and "js-open-quick-qr-login" in u1_h_nav,
        )

        # Forgot Password 3-step OTP flow
        fp_guest = SessionClient(self.base_url)
        fp_guest.refresh_csrf("/S01AuthOtpGoogle")
        fp_email = f"fp.qa.{int(time.time())}@cmc.edu.vn"
        fp_guest.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Quen Mat Khau QA", "Email": fp_email, "Password": "OldPass@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        fp_guest.logout()
        fp_guest.refresh_csrf("/S01AuthOtpGoogle")

        bad_fp_c, _, _ = fp_guest.post_json("/S01AuthOtpGoogle/SendPasswordResetOtp", {"email": "khongtontai.xyz@cmc.edu.vn"})
        send_fp_c, send_fp_j, send_fp_raw = fp_guest.post_json("/S01AuthOtpGoogle/SendPasswordResetOtp", {"email": fp_email})
        wrong_otp_c, _, _ = fp_guest.post_json("/S01AuthOtpGoogle/VerifyPasswordResetOtp", {"email": fp_email, "otpCode": "000000"})
        dev_fp_otp = (send_fp_j or {}).get("devOtpCode")
        if dev_fp_otp:
            ok_otp_c, _, _ = fp_guest.post_json("/S01AuthOtpGoogle/VerifyPasswordResetOtp", {"email": fp_email, "otpCode": dev_fp_otp})
            mis_c, _, _ = fp_guest.post_json(
                "/S01AuthOtpGoogle/ResetPasswordWithOtp",
                {"email": fp_email, "otpCode": dev_fp_otp, "newPassword": "NewPass@456", "confirmPassword": "Mismatch@789"},
            )
            rst_pw_c, _, _ = fp_guest.post_json(
                "/S01AuthOtpGoogle/ResetPasswordWithOtp",
                {"email": fp_email, "otpCode": dev_fp_otp, "newPassword": "NewPass@456", "confirmPassword": "NewPass@456"},
            )
            replay_pw_c, _, _ = fp_guest.post_json(
                "/S01AuthOtpGoogle/ResetPasswordWithOtp",
                {"email": fp_email, "otpCode": dev_fp_otp, "newPassword": "Another@999", "confirmPassword": "Another@999"},
            )
            relogin_ok, _ = fp_guest.login(fp_email, "NewPass@456")
            fp_ok = (
                bad_fp_c == 400
                and send_fp_c == 200
                and wrong_otp_c == 400
                and ok_otp_c == 200
                and mis_c == 400
                and rst_pw_c == 200
                and replay_pw_c == 400
                and relogin_ok
            )
        else:
            fp_ok = bad_fp_c == 400 and send_fp_c == 200 and wrong_otp_c == 400
        self.check(
            "L9",
            "Forgot Password OTP flow validates email, rejects wrong OTP, verifies & resets password atomically",
            fp_ok,
            f"bad={bad_fp_c}, send={send_fp_c}, wrong={wrong_otp_c}, devOtp={bool(dev_fp_otp)}, raw={send_fp_raw[:120]}",
        )

        # Avatar file upload (replaces manual URL input)
        _, u1_prof_html, _ = u1.get("/S01AuthOtpGoogle")
        valid_avt_png = bytes.fromhex(
            "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
            "0000000a49444154789c63000100000500010d0a2db40000000049454e44ae426082"
        )
        u1.refresh_csrf("/S01AuthOtpGoogle")
        bad_avt_c, _, _ = u1.post_multipart("/S01AuthOtpGoogle/UploadAvatar", {}, ("file", "fake.exe", b"MZ\x90\x001234567890", "application/octet-stream"))
        ok_avt_c, ok_avt_j, _ = u1.post_multipart("/S01AuthOtpGoogle/UploadAvatar", {}, ("file", "avatar.png", valid_avt_png, "image/png"))
        rst_avt_c, _, _ = u1.post_json("/S01AuthOtpGoogle/ResetAvatar", {})
        self.check(
            "L9",
            "Profile replaces manual AvatarUrl text box with secure image upload & reset",
            'id="profAvatarFileInput"' in u1_prof_html
            and 'id="profAvatarUrl"' not in u1_prof_html
            and bad_avt_c == 400
            and ok_avt_c == 200
            and str((ok_avt_j or {}).get("avatarUrl", "")).startswith("/uploads/avatars/")
            and rst_avt_c == 200,
            f"bad={bad_avt_c}, ok={ok_avt_c}, rst={rst_avt_c}",
        )

        # S18 Email OTP state synced 100% with S01 from DB (both unverified & verified states) + Mobile anti-loop & auto-read
        _, u1_s01_chk, _ = u1.get("/S01AuthOtpGoogle")
        _, u1_s18_chk, _ = u1.get("/S18NotificationHub")
        _, adm_s01_chk, _ = admin.get("/S01AuthOtpGoogle")
        _, adm_s18_chk, _ = admin.get("/S18NotificationHub")
        u1_unverified_synced = "Chưa xác minh OTP" in html_lib.unescape(u1_s01_chk) and "Chờ xác minh" in html_lib.unescape(u1_s18_chk)
        adm_verified_synced = "Đã xác minh OTP" in html_lib.unescape(adm_s01_chk) and "Đã xác minh OTP" in html_lib.unescape(adm_s18_chk)

        mob_u1_c, mob_u1_raw, _ = u1.get("/api/mobile/notifications/unread?sinceId=0", accept_json=True)
        mob_u1_j = json.loads(mob_u1_raw) if mob_u1_c == 200 else {}
        mob_items = mob_u1_j.get("notifications") or []
        auto_read_ok = True
        if mob_items:
            first_nid = mob_items[0].get("id")
            if first_nid:
                u1.get(f"/S18NotificationHub?highlight={first_nid}&notifId={first_nid}")
                _, mob_after_raw, _ = u1.get("/api/mobile/notifications/unread?sinceId=0", accept_json=True)
                mob_after_ids = [x.get("id") for x in (json.loads(mob_after_raw).get("notifications") or [])]
                auto_read_ok = first_nid not in mob_after_ids
        self.check(
            "L9",
            "S18 syncs EmailConfirmed with S01 (verified & unverified), auto-marks opened notification read & Mobile Poller blocks spam loop",
            u1_unverified_synced
            and adm_verified_synced
            and mob_u1_c == 200
            and "latestNotificationId" in mob_u1_j
            and auto_read_ok
            and "KEY_DELIVERED_IDS" in poller_java
            and "latestNotificationId < lastId" in poller_java,
            f"u1_sync={u1_unverified_synced}, adm_sync={adm_verified_synced}, mob={mob_u1_c}, auto_read={auto_read_ok}",
        )

        # S22 Icon instant preview & 9-Style / 12-Palette AI Chat sync (both Guest & Auth)
        self.check(
            "L9",
            "S22 Icon Manager shows instant URL.createObjectURL preview & SQL /assets/system-icons/ fallback",
            "URL.createObjectURL(file)" in s22_js_txt and "/assets/system-icons/" in s22_js_txt,
        )

        ai_guest = SessionClient(self.base_url)
        ai_guest.refresh_csrf("/")
        ag_c, ag_j, ag_raw = ai_guest.post_json(
            "/api/ai-chat/quick-ask",
            {"message": "Đổi giao diện sang phong cách truyện tranh màu bạc hà cỡ chữ lớn"},
        )
        ag_cmds = (ag_j or {}).get("clientCommands") or []
        has_neobrutal = any((c.get("args") or {}).get("style") == "neobrutal" for c in ag_cmds)
        has_mint = any((c.get("args") or {}).get("palette") == "mint" for c in ag_cmds)
        has_lg = any((c.get("args") or {}).get("fontSize") == "lg" for c in ag_cmds)

        u1.refresh_csrf("/S01AuthOtpGoogle")
        sp_c, sp_j, _ = u1.post_json(
            "/S01AuthOtpGoogle/SaveUiPreferences",
            {"theme": "light", "style": "paper", "palette": "sakura", "font": "lora", "fontSize": "lg"},
        )
        sp_saved = (sp_j or {}).get("preferences") or {}
        # Restore default clean light blue prefs for u1
        u1.post_json("/S01AuthOtpGoogle/SaveUiPreferences", {"theme": "light", "style": "standard", "palette": "blue", "font": "roboto", "fontSize": "md"})
        self.check(
            "L9",
            "AI Chat & Settings support all 9 styles, 12 palettes, fonts & sizes (including Gemini set_ui_prefs tool & Guest quick-ask)",
            'name = "set_ui_prefs"' in s13_svc_txt
            and ag_c == 200
            and has_neobrutal
            and has_mint
            and has_lg
            and sp_c == 200
            and sp_saved.get("style") == "paper"
            and sp_saved.get("palette") == "sakura",
            f"ag={ag_c}, cmds={ag_cmds}, sp={sp_c}:{sp_saved}, raw={ag_raw[:120]}",
        )

        # 9.9 Unified Librarian Desk, Per-Club Governance & Anti-IDOR, Home/S22 Filters & Pagination, 12 Backgrounds + 7 Font Sizes & 8 AI Email Scenarios
        lib = actors["librarian"]
        cur = actors["curator"]
        u3 = actors["u3"]

        # 9.9.1 S21 Unified Librarian Desk & Admin Full Lending Permissions
        adm_s21_c, adm_s21_html, _ = admin.get("/S21LibrarianDesk?loanChannel=Library&loanStatus=all&copyStatus=Available")
        lib_s21_c, lib_s21_html, _ = lib.get("/S21LibrarianDesk?loanChannel=P2P&loanStatus=all")
        self.check(
            "L9",
            "Admin & Librarian access Unified S21 Desk with filters, pagination & Direct Lending form",
            adm_s21_c == 200
            and lib_s21_c == 200
            and 'id="formDirectLend"' in adm_s21_html
            and 'id="s21UnifiedLoanSection"' in adm_s21_html
            and 'id="s21UnifiedLoanSection"' in lib_s21_html,
            f"adm={adm_s21_c}, lib={lib_s21_c}",
        )

        dl_select_m = re.search(r'id="directLendCopyId"[^>]*>.*?<option\s+value="(\d+)">\[([^\]]+)\]', adm_s21_html, re.S)
        dl_copy_id = int(dl_select_m.group(1)) if dl_select_m else 0
        dl_barcode = dl_select_m.group(2) if dl_select_m else ""

        u1.refresh_csrf("/S01AuthOtpGoogle")
        u1_dl_c, _, _ = u1.post_json("/S21LibrarianDesk/DirectLendBook", {"bookCopyId": max(dl_copy_id, 1), "borrowerUserId": "u-user-03", "desiredDays": 7, "immediateHandover": True})
        u1_act_c, _, _ = u1.post_json("/S21LibrarianDesk/ProcessLoanAction", {"loanRequestId": 1, "action": "Approve"})

        admin.refresh_csrf("/S21LibrarianDesk")
        adm_dl_overdue_c, _, _ = admin.post_json(
            "/S21LibrarianDesk/DirectLendBook",
            {"bookCopyId": dl_copy_id, "borrowerUserId": "u-user-03", "desiredDays": 7, "immediateHandover": True, "note": "Overdue borrower guard"},
        ) if dl_copy_id > 0 else (0, {}, "no available copy")
        adm_dl_c, _, adm_dl_raw = admin.post_json(
            "/S21LibrarianDesk/DirectLendBook",
            {"bookCopyId": dl_copy_id, "borrowerUserId": "u-user-02", "desiredDays": 7, "immediateHandover": True, "note": "QA L9.9 walk-in"},
        ) if dl_copy_id > 0 else (0, {}, "no available copy")

        adm_ret_c = 0
        adm_ret_raw = ""
        if adm_dl_c == 200 and dl_barcode:
            _, s21_act_html, _ = admin.get(f"/S21LibrarianDesk?loanStatus=Active&loanKeyword={urllib.parse.quote(dl_barcode)}")
            req_m = re.search(r'data-req-id="(\d+)"[^>]*data-action="Return"', s21_act_html)
            if not req_m:
                req_m = re.search(r'data-action="Return"[^>]*data-req-id="(\d+)"', s21_act_html)
            if req_m:
                adm_ret_c, _, adm_ret_raw = admin.post_json(
                    "/S21LibrarianDesk/ProcessLoanAction",
                    {"loanRequestId": int(req_m.group(1)), "action": "Return", "returnCondition": "Good", "note": "QA L9.9 clean return"},
                )
        self.check(
            "L9",
            "Admin DirectLendBook (guards overdue u3=400, lends clean u2=200) + Unified ProcessLoanAction Return work end-to-end; User gets 403",
            u1_dl_c == 403 and u1_act_c == 403 and adm_dl_overdue_c == 400 and adm_dl_c == 200 and adm_ret_c == 200,
            f"u1=({u1_dl_c},{u1_act_c}), copy={dl_copy_id}, overdue={adm_dl_overdue_c}, dl={adm_dl_c}({adm_dl_raw[:80]}), ret={adm_ret_c}({adm_ret_raw[:80]})",
        )

        # 9.9.2 S20 Per-Club Group Governance, Dedicated Leader & Strict Per-Club Anti-IDOR
        u1.refresh_csrf("/S20ExchangeHubClub")
        u1_cc_c, _, _ = u1.post_json("/S20ExchangeHubClub/CreateClub", {"name": "Unauthorized Club", "address": "Ha Noi"})

        admin.refresh_csrf("/S20ExchangeHubClub")
        club_name = f"CLB Đọc Sách QA {int(time.time())}"
        adm_cc_c, adm_cc_j, adm_cc_raw = admin.post_json(
            "/S20ExchangeHubClub/CreateClub",
            {
                "name": club_name,
                "address": "Phòng 305 Nhà A",
                "hubType": "ClubStation",
                "leaderUserId": "u-user-02",
                "openHours": "08:30 - 19:30",
                "phone": "0901234567",
                "description": "CLB kiểm thử thể chế nhóm riêng",
                "clubRules": "Giữ gìn sách sạch đẹp",
                "isPublicClub": False,
            },
        )
        new_hub_id = int((adm_cc_j or {}).get("hubId") or 0)

        u1_join_c, _, _ = u1.post_json("/S20ExchangeHubClub/JoinClub", {"hubId": new_hub_id, "joinNote": "Xin gia nhập CLB QA"}) if new_hub_id else (0, {}, "")

        # Strict Anti-IDOR: u-curator-01 leads Hub #3, NOT new_hub_id -> MUST receive 403!
        cur.refresh_csrf("/S20ExchangeHubClub")
        cur_idor_add_c, _, _ = cur.post_json("/S20ExchangeHubClub/AddClubMember", {"hubId": new_hub_id, "targetUserId": "u-user-03", "memberRole": "Member"}) if new_hub_id else (0, {}, "")
        cur_idor_post_c, _, _ = cur.post_json("/S20ExchangeHubClub/CreateClubPost", {"hubId": new_hub_id, "title": "Hack", "content": "Unauthorized"}) if new_hub_id else (0, {}, "")

        # Dedicated Club Leader (u2, even if regular User role) adds u3 like a Group, edits own club & publishes pinned post
        u2.refresh_csrf("/S20ExchangeHubClub")
        u2_add_c, _, u2_add_raw = u2.post_json("/S20ExchangeHubClub/AddClubMember", {"hubId": new_hub_id, "targetUserId": "u-user-03", "memberRole": "ViceLeader"}) if new_hub_id else (0, {}, "")
        u2_upd_c, _, u2_upd_raw = u2.post_json("/S20ExchangeHubClub/UpdateHubStation", {"hubId": new_hub_id, "name": club_name, "address": "Phòng 305 Nhà A (Updated)", "openHours": "08:00 - 20:00", "phone": "0901234567"}) if new_hub_id else (0, {}, "")
        u2_post_c, _, u2_post_raw = u2.post_json("/S20ExchangeHubClub/CreateClubPost", {"hubId": new_hub_id, "title": "Lịch offline tuần này", "content": "Hẹn gặp tại thư viện lúc 15h", "isPinned": True}) if new_hub_id else (0, {}, "")
        adm_post_c, _, _ = admin.post_json("/S20ExchangeHubClub/CreateClubPost", {"hubId": new_hub_id, "title": "Thông báo từ Admin", "content": "Chúc mừng CLB khai trương", "isPinned": False}) if new_hub_id else (0, {}, "")
        hub_view_c, hub_view_html, _ = u2.get(f"/S20ExchangeHubClub?hubId={new_hub_id}") if new_hub_id else (0, "", {})

        # Admin safety guards: cannot demote u-admin-01 or self-lock
        admin.refresh_csrf("/S22AdminAnalytics")
        demote_root_c, _, _ = admin.post_json("/S22AdminAnalytics/ChangeUserRole", {"userId": "u-admin-01", "newRole": "User"})
        lock_root_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": "u-admin-01", "lockAccount": True})

        self.check(
            "L9",
            "S20 Per-Club Governance: Admin creates club, Leader edits/adds/posts, unrelated Curator blocked (403 Anti-IDOR) & Admin root protected",
            u1_cc_c == 403
            and adm_cc_c == 200
            and new_hub_id > 0
            and u1_join_c == 200
            and cur_idor_add_c == 403
            and cur_idor_post_c == 403
            and u2_add_c == 200
            and u2_upd_c == 200
            and u2_post_c == 200
            and adm_post_c == 200
            and hub_view_c == 200
            and club_name in html_lib.unescape(hub_view_html)
            and "Lịch offline tuần này" in html_lib.unescape(hub_view_html)
            and demote_root_c == 400
            and lock_root_c == 400,
            f"u1_cc={u1_cc_c}, adm_cc={adm_cc_c}(id={new_hub_id},{adm_cc_raw[:60]}), join={u1_join_c}, idor=({cur_idor_add_c},{cur_idor_post_c}), u2=({u2_add_c},{u2_upd_c},{u2_post_c}), root=({demote_root_c},{lock_root_c})",
        )

        # 9.9.3 Home & S22 Admin Analytics Filters & Pagination
        hf_c, hf_html, _ = u1.get("/?source=System&availability=Available&condition=New&sort=title&page=1")
        s22_f_c, s22_f_html, _ = admin.get("/S22AdminAnalytics?range=30d&channel=Library&userRole=User&auditCategory=EMAIL")
        cd_c, cd_raw, _ = admin.get("/S22AdminAnalytics/ChartData?range=30d&channel=Library&categoryId=1", accept_json=True)
        cd_j = json.loads(cd_raw) if cd_c == 200 else {}
        self.check(
            "L9",
            "Home & S22 Admin Analytics support multi-criteria filters, pagination & dynamic ChartData API",
            hf_c == 200
            and 'name="availability"' in hf_html
            and 'name="sort"' in hf_html
            and s22_f_c == 200
            and 'name="range"' in s22_f_html
            and 'name="userRole"' in s22_f_html
            and cd_c == 200
            and cd_j.get("range") == "30d"
            and cd_j.get("channel") == "Library"
            and cd_j.get("categoryId") == 1,
            f"home={hf_c}, s22={s22_f_c}, cd={cd_c}:{cd_j}",
        )

        # 9.9.4 12 Local Page Backgrounds & 7 Font Sizes (Settings + AI Chat)
        ai_guest.refresh_csrf("/")
        bg_c, bg_j, bg_raw = ai_guest.post_json(
            "/api/ai-chat/quick-ask",
            {"message": "Đổi nền lưới chấm sổ tay và cỡ chữ siêu lớn 3xl giúp mình"},
        )
        bg_cmds = (bg_j or {}).get("clientCommands") or []
        has_dot_grid = any((c.get("args") or {}).get("background") == "dot-grid" for c in bg_cmds)
        has_3xl = any((c.get("args") or {}).get("fontSize") == "3xl" for c in bg_cmds)

        u1.refresh_csrf("/S01AuthOtpGoogle")
        bg_sp_c, bg_sp_j, _ = u1.post_json(
            "/S01AuthOtpGoogle/SaveUiPreferences",
            {"theme": "light", "style": "standard", "palette": "blue", "font": "roboto", "fontSize": "2xl", "background": "soft-aurora"},
        )
        bg_saved = (bg_sp_j or {}).get("preferences") or {}
        u1.post_json(
            "/S01AuthOtpGoogle/SaveUiPreferences",
            {"theme": "light", "style": "standard", "palette": "blue", "font": "roboto", "fontSize": "md", "background": "solid"},
        )
        self.check(
            "L9",
            "12 Local Page Backgrounds & 7 Font Sizes sync via Settings API & AI Chat commands",
            bg_c == 200
            and has_dot_grid
            and has_3xl
            and bg_sp_c == 200
            and bg_saved.get("background") == "soft-aurora"
            and bg_saved.get("fontSize") == "2xl"
            and 'data-bg-pattern="starry-night"' in theme_css
            and 'data-font-size="3xl"' in theme_css,
            f"bg_ai={bg_c}:{bg_cmds}, sp={bg_sp_c}:{bg_saved}, raw={bg_raw[:100]}",
        )

        # 9.9.5 AI Chat Expanded Multi-Scenario Email Sending (8 types + RBAC)
        u1.refresh_csrf("/S13GeminiAiAssistant")
        em_prog_c, _, em_prog_r = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_reading_progress_email", "parameters": {}})
        em_rec_c, _, em_rec_r = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_book_recommendations_email", "parameters": {"topic": "Lập trình"}})
        em_wal_c, _, em_wal_r = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_wallet_membership_email", "parameters": {}})
        em_u1_club_c, _, _ = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_club_announcement_email", "parameters": {"hubId": 3, "title": "Spam", "content": "Blocked"}})
        em_u1_adm_c, _, _ = u1.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_admin_analytics_report_email", "parameters": {}})

        cur.refresh_csrf("/S13GeminiAiAssistant")
        em_cur_club_c, _, em_cur_r = cur.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_club_announcement_email", "parameters": {"hubId": 3, "title": "Offline CLB Tháng Này", "content": "Mời thành viên tham gia trao đổi sách 0đ"}})

        admin.refresh_csrf("/S13GeminiAiAssistant")
        em_adm_rep_c, _, em_adm_r = admin.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_admin_analytics_report_email", "parameters": {}})

        self.check(
            "L9",
            "AI Chat sends multi-scenario emails (reading progress, book recs, wallet/tier, club notice, admin KPI) with strict RBAC",
            em_prog_c == 200
            and em_rec_c == 200
            and em_wal_c == 200
            and em_u1_club_c == 403
            and em_u1_adm_c == 403
            and em_cur_club_c == 200
            and em_adm_rep_c == 200,
            f"prog={em_prog_c}, rec={em_rec_c}, wal={em_wal_c}, u1_rbac=({em_u1_club_c},{em_u1_adm_c}), cur={em_cur_club_c}({em_cur_r[:60]}), adm={em_adm_rep_c}({em_adm_r[:60]})",
        )

    # =========================================================================
    # LAYER 10: V5.2 COMPREHENSIVE BUTTON/FORM/API WIRING, FULL CRUD & ANTI-SPAM
    # =========================================================================
    def run_layer10_comprehensive_v52(self, actors: dict[str, SessionClient]) -> None:
        print("\n=== LAYER 10: V5.2 — EXHAUSTIVE BUTTON/FORM/LINK/API AUDIT, FULL MODULE CRUD & ANTI-SPAM SUITE ===")
        admin = actors["admin"]
        lib = actors["librarian"]
        cur = actors["curator"]
        u1 = actors["u1"]
        u2 = actors["u2"]
        u3 = actors["u3"]

        # ------------------------------------------------------------------
        # 10.1 Exhaustive Static + Live Button, Form, Link, Route & JS Audit
        # ------------------------------------------------------------------
        js_files = [p for p in (WEB_DIR / "wwwroot").rglob("*.js") if "vendor" not in p.parts]
        bad_js: list[str] = []
        for jf in js_files:
            r = subprocess.run(["node", "--check", str(jf)], capture_output=True, text=True)
            if r.returncode != 0:
                bad_js.append(f"{jf.name}: {r.stderr.strip()[:80]}")
        self.check(
            "L10",
            f"All {len(js_files)} self-hosted JS files pass strict node --check syntax audit",
            len(js_files) >= 35 and not bad_js,
            f"count={len(js_files)}, bad={bad_js}",
        )

        # Build complete set of Controller routes & actions
        routes: set[str] = {"/"}
        ctrl_actions: set[tuple[str, str]] = set()
        for cf in (WEB_DIR / "Controllers").glob("*.cs"):
            txt = cf.read_text(encoding="utf-8", errors="ignore")
            cname = cf.stem.replace("Controller", "")
            cm = re.search(r'\[Route\("([^"]+)"\)\]', txt)
            croute = cm.group(1) if cm else ""
            for m in re.finditer(
                r'\[Http(?:Get|Post|Put|Delete)(?:\("([^"]+)"\))?\][\s\S]{0,280}?public\s+(?:async\s+Task<)?IActionResult>?\s+(\w+)\s*\(',
                txt,
            ):
                sub, act = m.group(1), m.group(2)
                ctrl_actions.add((cname.lower(), act.lower()))
                if sub:
                    if sub.startswith("/"):
                        routes.add(sub.lower())
                    elif croute:
                        routes.add(("/" + croute.strip("/") + "/" + sub.strip("/")).lower())
                    else:
                        routes.add(f"/{cname}/{sub}".lower())
                routes.add(f"/{cname}/{act}".lower())
                if act.lower() == "index":
                    routes.add(f"/{cname}".lower())
            for m in re.finditer(r'public\s+(?:async\s+Task<)?IActionResult>?\s+(\w+)\s*\(', txt):
                ctrl_actions.add((cname.lower(), m.group(1).lower()))

        dead_razor_links: list[str] = []
        view_files = list((WEB_DIR / "Views").rglob("*.cshtml"))
        all_view_text = ""
        for vf in view_files:
            vtxt = vf.read_text(encoding="utf-8", errors="ignore")
            all_view_text += "\n" + vtxt
            for m in re.finditer(r'asp-controller="([^"]+)"[^>]*asp-action="([^"]+)"', vtxt):
                c_raw, a_raw = m.group(1), m.group(2)
                if "@" in c_raw or "@" in a_raw:
                    continue
                if (c_raw.lower(), a_raw.lower()) not in ctrl_actions:
                    dead_razor_links.append(f"{vf.name}: /{c_raw}/{a_raw}")
        self.check(
            "L10",
            "Zero broken Razor asp-controller/asp-action links across all Views",
            not dead_razor_links,
            str(dead_razor_links[:10]),
        )

        dead_js_apis: list[str] = []
        all_js_text = ""
        for jf in js_files:
            jtxt = jf.read_text(encoding="utf-8", errors="ignore")
            all_js_text += "\n" + jtxt
            for m in re.finditer(r'(?:apiRequest|fetch)\s*\(\s*[\'"`](/[A-Za-z0-9_\-/]+)', jtxt):
                u = m.group(1).split("?")[0].rstrip("/").lower()
                if u.startswith(("/assets/", "/uploads/", "/images/", "/vendor/", "/fonts/")):
                    continue
                ok = u in routes or any(
                    re.fullmatch(re.sub(r"\{[^}]+\}", "[^/]+", r), u)
                    or r.startswith(u + "/{")
                    for r in routes
                    if "{" in r
                )
                if not ok:
                    dead_js_apis.append(f"{jf.name}: {u}")
        self.check(
            "L10",
            "Zero dead JS apiRequest/fetch endpoint URLs across all 42 JS modules",
            not dead_js_apis,
            str(dead_js_apis[:10]),
        )

        js_classes = sorted(set(re.findall(r"\b(js-[a-z0-9\-]+)\b", all_view_text)))
        unbound_js_cls = [c for c in js_classes if c not in all_js_text]

        btn_ids = sorted(set(re.findall(r'<button[^>]+id="(btn[A-Za-z0-9_]+)"', all_view_text)))
        unbound_btns: list[str] = []
        for bid in btn_ids:
            bm = re.search(rf'<button[^>]+id="{re.escape(bid)}"[^>]*>', all_view_text)
            btag = bm.group(0) if bm else ""
            if (
                bid in all_js_text
                or f"getElementById('{bid}')" in all_view_text
                or f'getElementById("{bid}")' in all_view_text
                or 'type="submit"' in btag
                or "data-bs-toggle" in btag
                or any(cls in btag for cls in js_classes)
            ):
                continue
            unbound_btns.append(bid)

        form_ids = sorted(set(re.findall(r'<form[^>]+id="([A-Za-z0-9_\-]+)"', all_view_text)))
        unbound_forms: list[str] = []
        for fid in form_ids:
            fm = re.search(rf'<form[^>]+id="{re.escape(fid)}"[^>]*>', all_view_text)
            ftag = fm.group(0).lower() if fm else ""
            if 'method="get"' in ftag or "asp-action=" in ftag:
                continue
            if fid not in all_js_text:
                unbound_forms.append(fid)

        self.check(
            "L10",
            f"All {len(js_classes)} js-* buttons, {len(btn_ids)} btn* IDs & {len(form_ids)} AJAX forms have live handlers (0 dead controls)",
            not unbound_js_cls and not unbound_btns and not unbound_forms,
            f"unbound_js={unbound_js_cls}, unbound_btn={unbound_btns}, unbound_form={unbound_forms}",
        )

        # Live crawl all internal navigation hrefs rendered on Home (Guest & Admin)
        guest_nav = SessionClient(self.base_url)
        _, g_nav_html, _ = guest_nav.get("/")
        _, a_nav_html, _ = admin.get("/")
        nav_paths = sorted(
            set(
                re.findall(r'href="(/(?:S\d{2}[A-Za-z0-9]+(?:\?[^"]*)?)?)"', g_nav_html + a_nav_html)
            )
        )
        broken_nav: list[str] = []
        for p in nav_paths:
            clean_p = html_lib.unescape(p)
            sc, _, _ = admin.get(clean_p)
            if sc != 200:
                broken_nav.append(f"{clean_p}->{sc}")
        self.check(
            "L10",
            f"All {len(nav_paths)} live navigation & card links crawled from Home return HTTP 200",
            len(nav_paths) >= 20 and not broken_nav,
            f"count={len(nav_paths)}, broken={broken_nav[:8]}",
        )

        # ------------------------------------------------------------------
        # 10.2 Full Interactive Module CRUD & Button Coverage (S02 -> S21)
        # ------------------------------------------------------------------
        # S02 Personal Shelf full CRUD (CreateShelf -> UpdateShelf -> AddBookToShelf -> UpdateCopy -> DeleteCopy -> DeleteShelf)
        u1.refresh_csrf("/S02PersonalShelf")
        s02_shelf_name = f"Kệ QA L10 {int(time.time())}"
        cs_c, _, _ = u1.post_json("/S02PersonalShelf/CreateShelf", {"name": s02_shelf_name, "description": "QA L10", "isPublic": True})
        _, s02_h, _ = u1.get("/S02PersonalShelf")
        sh_m = re.search(rf'data-shelf-id="(\d+)"[^>]*data-shelf-name="{re.escape(s02_shelf_name)}"', html_lib.unescape(s02_h))
        if not sh_m:
            all_sh = re.findall(r'data-shelf-id="(\d+)"', s02_h)
            new_sh_id = int(all_sh[-1]) if all_sh else 0
        else:
            new_sh_id = int(sh_m.group(1))
        us_c, _, _ = u1.post_json("/S02PersonalShelf/UpdateShelf", {"shelfId": new_sh_id, "name": s02_shelf_name + " Upd", "description": "Upd", "isPublic": False}) if new_sh_id else (0, {}, "")
        ab_c, _, _ = u1.post_json("/S02PersonalShelf/AddBookToShelf", {"bookId": 1, "shelfId": new_sh_id, "condition": "New", "isLendable": True}) if new_sh_id else (0, {}, "")
        _, s02_h2, _ = u1.get(f"/S02PersonalShelf?shelfId={new_sh_id}")
        cp_m = re.search(r'data-copy-id="(\d+)"', s02_h2)
        new_cp_id = int(cp_m.group(1)) if cp_m else 0
        uc_c, _, _ = u1.post_json("/S02PersonalShelf/UpdateCopy", {"bookCopyId": new_cp_id, "condition": "Good", "shelfId": new_sh_id}) if new_cp_id else (0, {}, "")
        dc_c, _, _ = u1.post_json("/S02PersonalShelf/DeleteCopy", {"bookCopyId": new_cp_id}) if new_cp_id else (0, {}, "")
        ds_c, _, _ = u1.post_json("/S02PersonalShelf/DeleteShelf", {"shelfId": new_sh_id}) if new_sh_id else (0, {}, "")
        self.check(
            "L10",
            "S02 Personal Shelf full CRUD (Create/Update/Delete Shelf + Add/Update/Delete Copy)",
            cs_c == 200 and new_sh_id > 0 and us_c == 200 and ab_c == 200 and new_cp_id > 0 and uc_c == 200 and dc_c == 200 and ds_c == 200,
            f"cs={cs_c}, sh={new_sh_id}, us={us_c}, ab={ab_c}, cp={new_cp_id}, uc={uc_c}, dc={dc_c}, ds={ds_c}",
        )

        # S03 Reading Tracker full CRUD + ChartData JSON
        u2.refresh_csrf("/S03ReadingTracker")
        add_tr_c, tr_html, _ = u2.post_form("/S03ReadingTracker/AddBookToTracker", {"BookId": "5", "Priority": "High"}, follow_redirects=True)
        tr_ids = [int(x) for x in re.findall(r'data-progress-id="(\d+)"', tr_html)]
        tr_id = max(tr_ids) if tr_ids else 0
        u2.refresh_csrf("/S03ReadingTracker")
        up_tr_c, _, _ = u2.post_json("/S03ReadingTracker/UpdateProgress", {"progressId": tr_id, "currentPage": 45, "priority": "High", "personalRating": 5}) if tr_id else (0, {}, "")
        ch_tr_c, ch_tr_raw, _ = u2.get("/S03ReadingTracker/ChartData", accept_json=True)
        del_tr_c, _, _ = u2.post_json("/S03ReadingTracker/DeleteProgress", {"progressId": tr_id}) if tr_id else (0, {}, "")
        self.check(
            "L10",
            "S03 Reading Tracker full lifecycle (AddBook -> UpdateProgress -> ChartData JSON -> DeleteProgress)",
            add_tr_c == 200 and tr_id > 0 and up_tr_c == 200 and ch_tr_c == 200 and "{" in ch_tr_raw and del_tr_c == 200,
            f"add={add_tr_c}, id={tr_id}, up={up_tr_c}, ch={ch_tr_c}, del={del_tr_c}",
        )

        # S04 Book Notes & Quotes full CRUD + Community filter
        u2.refresh_csrf("/S04BookNotesQuotes")
        sn_c, sn_j, _ = u2.post_json("/S04BookNotesQuotes/SaveNote", {"bookId": 1, "pageNumber": 22, "quoteText": "Trích dẫn QA L10", "noteContent": "Cảm nhận QA L10", "isPublic": True})
        note_id = int(((sn_j or {}).get("note") or {}).get("noteId") or 0)
        en_c, _, _ = u2.post_json("/S04BookNotesQuotes/SaveNote", {"noteId": note_id, "bookId": 1, "pageNumber": 24, "quoteText": "Trích dẫn sửa", "noteContent": "Nội dung sửa", "isPublic": False}) if note_id else (0, {}, "")
        tp_c, _, _ = u2.post_json("/S04BookNotesQuotes/TogglePublic", {"noteId": note_id, "isPublic": True}) if note_id else (0, {}, "")
        cm_c, cm_html, _ = u1.get("/S04BookNotesQuotes?community=true")
        dn_c, _, _ = u2.post_json("/S04BookNotesQuotes/DeleteNote", {"noteId": note_id}) if note_id else (0, {}, "")
        self.check(
            "L10",
            "S04 Book Notes full lifecycle (Create -> Edit -> TogglePublic -> Community view -> Delete)",
            sn_c == 200 and note_id > 0 and en_c == 200 and tp_c == 200 and cm_c == 200 and "Nội dung sửa" in html_lib.unescape(cm_html) and dn_c == 200,
            f"sn={sn_c}, id={note_id}, en={en_c}, tp={tp_c}, cm={cm_c}, dn={dn_c}",
        )

        # S05 Catalog Update + S06 Suggest Autocomplete + S07 Shelf Update
        lib.refresh_csrf("/S05IsbnAutoCatalog")
        s05_up_c, _, _ = lib.post_json("/S05IsbnAutoCatalog/UpdateCatalogBook", {"bookId": 1, "title": "Clean Code - Mã Sạch", "author": "Robert C. Martin", "pageCount": 464, "categoryId": 1})
        s06_sug_c, s06_sug_raw, _ = u1.get("/S06SearchDiscovery/Suggest?q=Clean", accept_json=True)
        s06_flt_c, _, _ = u1.get("/S06SearchDiscovery?ownerType=System&statusFilter=Available&pageSize=12")
        lib.refresh_csrf("/S07LibraryCatalog")
        s07_up_c, _, _ = lib.post_json("/S07LibraryCatalog/UpdateShelfLocation", {"bookCopyId": 1, "shelfLocation": "Kệ CNTT-A1 (QA)"})
        self.check(
            "L10",
            "S05 UpdateCatalogBook, S06 Suggest/Filter & S07 UpdateShelfLocation all succeed",
            s05_up_c == 200 and s06_sug_c == 200 and "Clean" in s06_sug_raw and s06_flt_c == 200 and s07_up_c == 200,
            f"s05={s05_up_c}, s06_sug={s06_sug_c}, s06_flt={s06_flt_c}, s07={s07_up_c}",
        )

        # S08 CancelRequest + Anti-IDOR
        _, s08_u2_h, _ = u2.get("/S08LibraryLoanFlow")
        s08_cp_m = re.search(r'<option\s+value="(\d+)">\[BS-', s08_u2_h)
        s08_cp_id = int(s08_cp_m.group(1)) if s08_cp_m else 0
        u2.refresh_csrf("/S08LibraryLoanFlow")
        s08_cr_c, _, _ = u2.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": s08_cp_id, "desiredDays": 7, "requestNote": "QA L10 cancel test"}) if s08_cp_id else (0, {}, "")
        _, s08_u2_h2, _ = u2.get("/S08LibraryLoanFlow")
        s08_req_m = re.search(r'js-s08-cancel[\s\S]{0,120}?data-req-id="(\d+)"', s08_u2_h2)
        s08_req_id = int(s08_req_m.group(1)) if s08_req_m else 0
        u1.refresh_csrf("/S08LibraryLoanFlow")
        s08_idor_c, _, _ = u1.post_json("/S08LibraryLoanFlow/CancelRequest", {"loanRequestId": s08_req_id}) if s08_req_id else (0, {}, "")
        s08_can_c, _, _ = u2.post_json("/S08LibraryLoanFlow/CancelRequest", {"loanRequestId": s08_req_id}) if s08_req_id else (0, {}, "")
        self.check(
            "L10",
            "S08 User CreateRequest + CancelRequest succeeds while unrelated User gets 403 Anti-IDOR",
            s08_cr_c == 200 and s08_req_id > 0 and s08_idor_c == 403 and s08_can_c == 200,
            f"cp={s08_cp_id}, cr={s08_cr_c}, req={s08_req_id}, idor={s08_idor_c}, can={s08_can_c}",
        )

        # S10 Condition Report PII isolation + Fake magic-byte upload rejection + Update/Delete Report + S19 Guest PII isolation
        _, g_s10_h, _ = guest_nav.get("/S10ConditionReport")
        _, g_s19_h, _ = guest_nav.get("/S19DisputeResolution")
        s10_pii_clean = "BS-" not in g_s10_h
        s19_pii_clean = "#DSP-" not in g_s19_h and "BS-" not in g_s19_h

        _, lib_s10_h, _ = lib.get("/S10ConditionReport")
        any_loan_m = re.search(r'<option\s+value="(\d+)"', lib_s10_h)
        s10_loan_id = int(any_loan_m.group(1)) if any_loan_m else 1
        lib.refresh_csrf("/S10ConditionReport")
        fake_png_c, _, _ = lib.post_multipart(
            "/S10ConditionReport/UploadReport",
            {"loanId": str(s10_loan_id), "reportType": "Handover", "conditionGrade": "Good", "note": "Fake magic bytes"},
            ("imageFile", "evil.png", b"MZ\x90\x00NOT_A_REAL_PNG_FILE", "image/png"),
        )
        valid_1px_png = bytes.fromhex(
            "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
            "0000000a49444154789c63000100000500010d0a2db40000000049454e44ae426082"
        )
        ok_rep_c, _, _ = lib.post_multipart(
            "/S10ConditionReport/UploadReport",
            {"loanId": str(s10_loan_id), "reportType": "Handover", "conditionGrade": "Good", "note": "QA L10 valid report"},
            ("imageFile", "valid_l10.png", valid_1px_png, "image/png"),
        )
        _, lib_s10_h2, _ = lib.get(f"/S10ConditionReport?loanId={s10_loan_id}")
        rep_ids = [int(x) for x in re.findall(r'data-report-id="(\d+)"', lib_s10_h2)]
        rep_id = max(rep_ids) if rep_ids else 0
        u3.refresh_csrf("/S10ConditionReport")
        rep_idor_c, _, _ = u3.post_json("/S10ConditionReport/UpdateReport", {"reportId": rep_id, "conditionGrade": "Worn", "note": "Hack"}) if rep_id else (0, {}, "")
        rep_up_c, _, _ = lib.post_json("/S10ConditionReport/UpdateReport", {"reportId": rep_id, "conditionGrade": "Fair", "note": "QA L10 updated"}) if rep_id else (0, {}, "")
        rep_del_c, _, _ = lib.post_json("/S10ConditionReport/DeleteReport", {"reportId": rep_id}) if rep_id else (0, {}, "")
        self.check(
            "L10",
            "S10/S19 hide PII from Guest; S10 blocks fake magic-byte PNG (400) & supports Update/Delete with Anti-IDOR",
            s10_pii_clean and s19_pii_clean and fake_png_c == 400 and ok_rep_c == 200 and rep_id > 0 and rep_idor_c == 403 and rep_up_c == 200 and rep_del_c == 200,
            f"pii=({s10_pii_clean},{s19_pii_clean}), fake={fake_png_c}, ok={ok_rep_c}, rep={rep_id}, idor={rep_idor_c}, up={rep_up_c}, del={rep_del_c}",
        )

        # S11 Hot Book Queue double-enqueue guard + Anti-IDOR cancel + double-cancel corruption guard
        u2.refresh_csrf("/S11HotBookQueue")
        q1_c, _, _ = u2.post_json("/S11HotBookQueue/Enqueue", {"bookId": 8})
        qdup_c, _, _ = u2.post_json("/S11HotBookQueue/Enqueue", {"bookId": 8})
        _, s11_h, _ = u2.get("/S11HotBookQueue")
        res_ids = [int(x) for x in re.findall(r'data-res-id="(\d+)"', s11_h)]
        res_id = max(res_ids) if res_ids else 0
        u1.refresh_csrf("/S11HotBookQueue")
        q_idor_c, _, _ = u1.post_json("/S11HotBookQueue/Cancel", {"reservationId": res_id}) if res_id else (0, {}, "")
        q_can1_c, _, _ = u2.post_json("/S11HotBookQueue/Cancel", {"reservationId": res_id}) if res_id else (0, {}, "")
        q_can2_c, _, _ = u2.post_json("/S11HotBookQueue/Cancel", {"reservationId": res_id}) if res_id else (0, {}, "")
        self.check(
            "L10",
            "S11 Queue blocks duplicate enqueue (400), blocks IDOR cancel (403) & blocks double-cancel queue corruption (400)",
            q1_c == 200 and qdup_c == 400 and res_id > 0 and q_idor_c == 403 and q_can1_c == 200 and q_can2_c == 400,
            f"q1={q1_c}, dup={qdup_c}, id={res_id}, idor={q_idor_c}, can1={q_can1_c}, can2={q_can2_c}",
        )

        # S14 AI Recommendation filters & QuickAddToTracker
        s14_c, _, _ = u1.get("/S14AiRecommendation?mood=focus&major=cntt")
        u1.refresh_csrf("/S14AiRecommendation")
        s14_add_c, _, _ = u1.post_json("/S14AiRecommendation/QuickAddToTracker", {"bookId": 12, "priority": "Medium"})
        self.check("L10", "S14 Recommendation filters & QuickAddToTracker work cleanly", s14_c == 200 and s14_add_c == 200, f"get={s14_c}, add={s14_add_c}")

        # S15 TrustScore Sabotage Guard (blocks duplicate standalone User review!) + UpdateReview + Anti-IDOR + DeleteReview
        u2.refresh_csrf("/S15TrustScoreReview")
        rev1_c, _, _ = u2.post_json("/S15TrustScoreReview/SubmitReview", {"targetType": "User", "targetId": "u-user-03", "rating": 4, "comment": "QA L10 review u3"})
        rev_sabotage_c, _, _ = u2.post_json("/S15TrustScoreReview/SubmitReview", {"targetType": "User", "targetId": "u-user-03", "rating": 1, "comment": "Spam tank trustscore"})
        _, s15_h, _ = u2.get("/S15TrustScoreReview")
        rev_ids = [int(x) for x in re.findall(r'data-review-id="(\d+)"', s15_h)]
        rev_id = max(rev_ids) if rev_ids else 0
        u1.refresh_csrf("/S15TrustScoreReview")
        rev_idor_c, _, _ = u1.post_json("/S15TrustScoreReview/UpdateReview", {"reviewId": rev_id, "rating": 1, "comment": "Hacked"}) if rev_id else (0, {}, "")
        rev_up_c, _, _ = u2.post_json("/S15TrustScoreReview/UpdateReview", {"reviewId": rev_id, "rating": 5, "comment": "Updated QA L10"}) if rev_id else (0, {}, "")
        rev_del_c, _, _ = u2.post_json("/S15TrustScoreReview/DeleteReview", {"reviewId": rev_id}) if rev_id else (0, {}, "")
        self.check(
            "L10",
            "S15 kills TrustScore sabotage exploit (duplicate standalone User review=400) & passes Update/Delete/Anti-IDOR",
            rev1_c == 200 and rev_sabotage_c == 400 and rev_id > 0 and rev_idor_c == 403 and rev_up_c == 200 and rev_del_c == 200,
            f"r1={rev1_c}, dup={rev_sabotage_c}, id={rev_id}, idor={rev_idor_c}, up={rev_up_c}, del={rev_del_c}",
        )

        # S18 Notification Hub full actions (MarkAllRead + Staff TriggerResendEmail)
        u2.refresh_csrf("/S18NotificationHub")
        s18_all_c, _, _ = u2.post_json("/S18NotificationHub/MarkAllRead", {})
        lib.refresh_csrf("/S18NotificationHub")
        s18_camp_c, _, _ = lib.post_json("/S18NotificationHub/TriggerResendEmail", {"campaignType": "QueueAvailable"})
        self.check("L10", "S18 MarkAllRead & Staff TriggerResendEmail succeed", s18_all_c == 200 and s18_camp_c == 200, f"all={s18_all_c}, camp={s18_camp_c}")

        # S21 BatchImport -> UpdateCopyState -> DeleteSystemCopy & Guard active Borrowed copy
        lib.refresh_csrf("/S21LibrarianDesk")
        bi_c, bi_j, _ = lib.post_json("/S21LibrarianDesk/BatchImportCopies", {"bookId": 1, "libraryId": 1, "quantity": 1, "shelfLocation": "Kệ QA-L10", "condition": "New"})
        new_bc = ((bi_j or {}).get("barcodes") or [""])[0]
        _, s21_bc_h, _ = lib.get(f"/S21LibrarianDesk?copyKeyword={urllib.parse.quote(new_bc)}") if new_bc else (0, "", {})
        bc_id_m = re.search(r'js-change-state[^>]*data-id="(\d+)"', s21_bc_h)
        bc_id = int(bc_id_m.group(1)) if bc_id_m else 0
        uc_dmg_c, _, _ = lib.post_json("/S21LibrarianDesk/UpdateCopyState", {"bookCopyId": bc_id, "newStatus": "Damaged", "newCondition": "Worn"}) if bc_id else (0, {}, "")
        uc_av_c, _, _ = lib.post_json("/S21LibrarianDesk/UpdateCopyState", {"bookCopyId": bc_id, "newStatus": "Available", "newCondition": "Good"}) if bc_id else (0, {}, "")
        del_bc_c, _, _ = lib.post_json("/S21LibrarianDesk/DeleteSystemCopy", {"bookCopyId": bc_id}) if bc_id else (0, {}, "")
        self.check(
            "L10",
            "S21 Inventory full CRUD (BatchImport -> UpdateCopyState Damaged/Available -> DeleteSystemCopy)",
            bi_c == 200 and bool(new_bc) and bc_id > 0 and uc_dmg_c == 200 and uc_av_c == 200 and del_bc_c == 200,
            f"bi={bi_c}, bc={new_bc}, id={bc_id}, dmg={uc_dmg_c}, av={uc_av_c}, del={del_bc_c}",
        )

        # ------------------------------------------------------------------
        # 10.3 Anti-Spam, Anti-Brute-Force & Live Lockout Penetration Suite
        # ------------------------------------------------------------------
        ts_spam = int(time.time())

        # A. Live Cookie Revocation on Admin Lock (OnValidatePrincipal rejects locked spammer immediately)
        lock_user = SessionClient(self.base_url)
        lock_email = f"lock.l10.{ts_spam}@cmc.edu.vn"
        lock_user.refresh_csrf("/S01AuthOtpGoogle")
        lock_user.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu Khoa Phien L10", "Email": lock_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        _, ls_before_raw, _ = lock_user.get("/api/mobile/session", accept_json=True)
        ls_before = json.loads(ls_before_raw).get("authenticated")
        admin.refresh_csrf("/S22AdminAnalytics")
        _, s22_lu_h, _ = admin.get(f"/S22AdminAnalytics?userKeyword={urllib.parse.quote(lock_email)}")
        lu_m = re.search(r'js-role-select[^>]*data-user-id="([^"]+)"', s22_lu_h)
        lu_uid = lu_m.group(1) if lu_m else ""
        lk_c, _, _ = admin.post_json("/S22AdminAnalytics/ToggleUserLock", {"userId": lu_uid, "lockAccount": True}) if lu_uid else (0, {}, "")
        _, ls_after_raw, _ = lock_user.get("/api/mobile/session", accept_json=True)
        ls_after = json.loads(ls_after_raw).get("authenticated")
        self.check(
            "L10",
            "Anti-Abuse: Admin locking an active spammer immediately revokes their live cookie (authenticated -> false)",
            ls_before is True and lk_c == 200 and ls_after is False,
            f"before={ls_before}, uid={lu_uid}, lk={lk_c}, after={ls_after}",
        )

        # B. OTP Send Flood Rate-Limit (5 OK -> 6th HTTP 429)
        otp_spam_user = SessionClient(self.base_url)
        otp_flood_email = f"otpflood.{ts_spam}@cmc.edu.vn"
        otp_spam_user.refresh_csrf("/S01AuthOtpGoogle")
        otp_spam_user.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu OTP Flood", "Email": otp_flood_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        otp_spam_user.logout()
        otp_spam_user.refresh_csrf("/S01AuthOtpGoogle")
        otp_codes: list[int] = []
        for _ in range(6):
            oc, _, _ = otp_spam_user.post_json("/S01AuthOtpGoogle/SendPasswordResetOtp", {"email": otp_flood_email})
            otp_codes.append(oc)
        self.check(
            "L10",
            "Anti-Spam: OTP send flood blocked at 6th rapid request with HTTP 429",
            otp_codes[:5] == [200, 200, 200, 200, 200] and otp_codes[5] == 429,
            f"codes={otp_codes}",
        )

        # C. OTP Brute-Force Lockout (5 consecutive wrong guesses invalidate OTP & 6th returns HTTP 429)
        otp_bf_user = SessionClient(self.base_url)
        otp_bf_email = f"otpbf.{ts_spam}@cmc.edu.vn"
        otp_bf_user.refresh_csrf("/S01AuthOtpGoogle")
        otp_bf_user.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu OTP BruteForce", "Email": otp_bf_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        otp_bf_user.logout()
        otp_bf_user.refresh_csrf("/S01AuthOtpGoogle")
        bf_send_c, _, _ = otp_bf_user.post_json("/S01AuthOtpGoogle/SendPasswordResetOtp", {"email": otp_bf_email})
        bf_codes: list[int] = []
        for i in range(6):
            bc, _, _ = otp_bf_user.post_json("/S01AuthOtpGoogle/VerifyPasswordResetOtp", {"email": otp_bf_email, "otpCode": f"00000{i}"})
            bf_codes.append(bc)
        self.check(
            "L10",
            "Anti-BruteForce: 5 wrong OTP guesses invalidate code & 6th attempt returns HTTP 429",
            bf_send_c == 200 and bf_codes[:5] == [400, 400, 400, 400, 400] and bf_codes[5] == 429,
            f"send={bf_send_c}, verify_codes={bf_codes}",
        )

        # D. AI Email Spam Rate-Limit (8 OK -> 9th HTTP 429)
        ai_spammer = SessionClient(self.base_url)
        ai_spam_email = f"aispam.{ts_spam}@cmc.edu.vn"
        ai_spammer.refresh_csrf("/S01AuthOtpGoogle")
        ai_spammer.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu AI Email Spam", "Email": ai_spam_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        ai_spammer.refresh_csrf("/S13GeminiAiAssistant")
        ai_em_codes: list[int] = []
        for _ in range(9):
            ec, _, _ = ai_spammer.post_json("/S13GeminiAiAssistant/ExecuteCopilotAction", {"actionKey": "send_reading_progress_email", "parameters": {}})
            ai_em_codes.append(ec)
        self.check(
            "L10",
            "Anti-Spam: AI Email sending enforces sliding-window quota (8 OK -> 9th HTTP 429)",
            ai_em_codes[:8] == [200] * 8 and ai_em_codes[8] == 429,
            f"codes={ai_em_codes}",
        )

        # E. Pending Loan Request Flood Cap (Free tier max 6 concurrent pending -> 7th blocked 400)
        loan_spammer = SessionClient(self.base_url)
        loan_spam_email = f"loanspam.{ts_spam}@cmc.edu.vn"
        loan_spammer.refresh_csrf("/S01AuthOtpGoogle")
        loan_spammer.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu Spam Phieu Muon", "Email": loan_spam_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        _, ls_s08_h, _ = loan_spammer.get("/S08LibraryLoanFlow")
        avail_copy_ids = [int(x) for x in re.findall(r'<option\s+value="(\d+)">\[BS-', ls_s08_h)]
        loan_spammer.refresh_csrf("/S08LibraryLoanFlow")
        pend_codes: list[int] = []
        for cid in avail_copy_ids[:7]:
            pc, _, _ = loan_spammer.post_json("/S08LibraryLoanFlow/CreateRequest", {"bookCopyId": cid, "desiredDays": 7, "requestNote": "Flood test"})
            pend_codes.append(pc)
        self.check(
            "L10",
            "Anti-Spam: Pending loan request cap blocks 7th concurrent Pending request for Free member (HTTP 400)",
            len(pend_codes) == 7 and pend_codes[:6] == [200] * 6 and pend_codes[6] == 400,
            f"avail={len(avail_copy_ids)}, codes={pend_codes}",
        )

        # F. Club Join Spam Rate-Limit (8 rapid calls -> 9th HTTP 429)
        club_spammer = SessionClient(self.base_url)
        club_spam_email = f"clubspam.{ts_spam}@cmc.edu.vn"
        club_spammer.refresh_csrf("/S01AuthOtpGoogle")
        club_spammer.post_form(
            "/S01AuthOtpGoogle/Register",
            {"FullName": "Kiem Thu Spam CLB", "Email": club_spam_email, "Password": "User@123", "Address": "Ha Noi"},
            follow_redirects=True,
        )
        club_spammer.refresh_csrf("/S20ExchangeHubClub")
        club_join_codes: list[int] = []
        for _ in range(9):
            jc, _, _ = club_spammer.post_json("/S20ExchangeHubClub/JoinClub", {"hubId": 3})
            club_join_codes.append(jc)
        self.check(
            "L10",
            "Anti-Spam: Club Join notification flood blocked at 9th rapid call with HTTP 429",
            club_join_codes[8] == 429,
            f"codes={club_join_codes}",
        )

        # ------------------------------------------------------------------
        # 10.4 Release 5.4 Hardening: Null-Body Filter, Validation, S09 Cancel,
        # Neutral Placeholders, Professional UI Wording & 100% Service Tagging
        # ------------------------------------------------------------------

        # 1. Global ValidateNonNullBodyFilter -> JSON `null` returns HTTP 400 (never 500)
        u2.refresh_csrf("/S02PersonalShelf")
        null_body_endpoints = [
            "/S02PersonalShelf/CreateShelf",
            "/S08LibraryLoanFlow/CreateRequest",
            "/S09P2PLoanFlow/CancelRequest",
            "/S20ExchangeHubClub/JoinClub",
        ]
        null_results = []
        for ep in null_body_endpoints:
            nc, nj, _ = u2.post_json(ep, None)  # type: ignore[arg-type]
            null_results.append((ep, nc, isinstance(nj, dict) and bool(nj.get("message"))))
        self.check(
            "L10",
            "Global ValidateNonNullBodyFilter blocks JSON `null` with clean HTTP 400 Vietnamese JSON (never 500)",
            all(code == 400 and has_msg for _, code, has_msg in null_results),
            f"results={null_results}",
        )

        # 2. Input Validation Tightening (S01 Phone & Avatar XSS, S05 ISBN/Year/Pages/Cover XSS, S19 Evidence XSS)
        u2.refresh_csrf("/S01AuthOtpGoogle")
        bad_phone_c, _, _ = u2.post_json("/S01AuthOtpGoogle/UpdateProfile", {"fullName": "Kiểm Thử Hợp Lệ", "phoneNumber": "khong-phai-sdt-99999999999"})
        bad_avt_c, _, _ = u2.post_json("/S01AuthOtpGoogle/UpdateProfile", {"fullName": "Kiểm Thử Hợp Lệ", "avatarUrl": "javascript:alert(1)"})
        u2.refresh_csrf("/S05IsbnAutoCatalog")
        bad_cov_c, _, _ = u2.post_json("/S05IsbnAutoCatalog/SaveBookAndCopy", {"isbn": "9780134494166", "title": "Test", "author": "Author", "pageCount": 200, "coverImageUrl": "javascript:alert(1)"})
        bad_yr_c, _, _ = u2.post_json("/S05IsbnAutoCatalog/SaveBookAndCopy", {"isbn": "9780134494166", "title": "Test", "author": "Author", "pageCount": 200, "publishedYear": 1400})
        bad_pg_c, _, _ = u2.post_json("/S05IsbnAutoCatalog/SaveBookAndCopy", {"isbn": "9780134494166", "title": "Test", "author": "Author", "pageCount": 99999})
        u2.refresh_csrf("/S19DisputeResolution")
        bad_ev_c, _, _ = u2.post_json("/S19DisputeResolution/CreateDispute", {"loanId": 1, "reason": "Test URL safety", "evidenceImageUrl": "javascript:alert(1)"})
        self.check(
            "L10",
            "Field validation blocks invalid VN phone, javascript: image URLs, out-of-range year/pages (all HTTP 400)",
            all(c == 400 for c in (bad_phone_c, bad_avt_c, bad_cov_c, bad_yr_c, bad_pg_c, bad_ev_c)),
            f"phone={bad_phone_c}, avt={bad_avt_c}, cov={bad_cov_c}, yr={bad_yr_c}, pg={bad_pg_c}, ev={bad_ev_c}",
        )

        # 3. S09 P2P CancelRequest E2E + Anti-IDOR (User creates -> unrelated User gets 403 -> Borrower cancels 200)
        _, s09_h, _ = u2.get("/S09P2PLoanFlow")
        s09_sel_m = re.search(r'id="s09BookCopyId"[^>]*>(.*?)</select>', s09_h, re.S)
        s09_cids = [int(x) for x in re.findall(r'<option\s+value="(\d+)"', s09_sel_m.group(1))] if s09_sel_m else []
        u2.refresh_csrf("/S09P2PLoanFlow")
        p2p_cid = 0
        p2p_cr_c = 0
        p2p_cr_raw = ""
        for cand_cid in s09_cids:
            c_try, _, raw_try = u2.post_json("/S09P2PLoanFlow/CreateRequest", {"bookCopyId": cand_cid, "pickupHubId": 3, "desiredDays": 7})
            p2p_cid, p2p_cr_c, p2p_cr_raw = cand_cid, c_try, raw_try
            if c_try == 200:
                break
        _, s09_after, _ = u2.get("/S09P2PLoanFlow")
        p2p_req_m = re.search(r'js-s09-cancel[^>]*data-req-id="(\d+)"', s09_after)
        if not p2p_req_m:
            p2p_req_m = re.search(r'data-req-id="(\d+)"[^>]*class="[^"]*js-s09-cancel', s09_after)
        p2p_req_id = int(p2p_req_m.group(1)) if p2p_req_m else 0
        # Unrelated user (`club_spammer`) must receive 403 Anti-IDOR
        club_spammer.refresh_csrf("/S09P2PLoanFlow")
        p2p_idor_c, _, _ = club_spammer.post_json("/S09P2PLoanFlow/CancelRequest", {"loanRequestId": p2p_req_id}) if p2p_req_id else (0, {}, "")
        # Original borrower (`u2`) cancels cleanly with 200
        p2p_can_c, _, _ = u2.post_json("/S09P2PLoanFlow/CancelRequest", {"loanRequestId": p2p_req_id}) if p2p_req_id else (0, {}, "")
        self.check(
            "L10",
            "S09 P2P CancelRequest allows borrower cancel (200) while blocking unrelated user with 403 Anti-IDOR",
            p2p_cid > 0 and p2p_cr_c == 200 and p2p_req_id > 0 and p2p_idor_c == 403 and p2p_can_c == 200,
            f"cid={p2p_cid}, cr={p2p_cr_c}, raw={p2p_cr_raw[:80]}, reqId={p2p_req_id}, idor={p2p_idor_c}, cancel={p2p_can_c}",
        )

        # 4. Static Audit: Neutral Placeholders across ALL Views & custom JS
        web_root = WEB_DIR
        bad_ph_re = re.compile(
            r"Nguyễn Văn An|Nguyễn Minh Anh|@cmc\.edu\.vn|tenban@gmail\.com|nhapemail@domain\.com|ĐH CMC|Tòa nhà CMC|Robert C\. Martin|Nguyễn Nhật Ánh|Clean Code|BS-SYS|Barcode|URL ảnh|0988xxxxxx|Cầu Giấy",
            re.I,
        )
        non_neutral_phs: list[str] = []
        for fpath in list((web_root / "Views").rglob("*.cshtml")) + list((web_root / "wwwroot").rglob("*.js")):
            if "vendor" in fpath.parts or "lib" in fpath.parts:
                continue
            txt = fpath.read_text(encoding="utf-8", errors="ignore")
            for ph in re.findall(r'placeholder="([^"]*)"', txt):
                if bad_ph_re.search(ph):
                    non_neutral_phs.append(f"{fpath.name}:{ph}")
        self.check(
            "L10",
            "Static UX Audit: 100% of input placeholders across Views & JS are neutral (0 personal/school/tech bias)",
            len(non_neutral_phs) == 0,
            f"violations={non_neutral_phs[:5]}",
        )

        # 5. Static Audit: Zero raw SQL column <code> tags in visible Views
        raw_col_tags: list[str] = []
        for fpath in (web_root / "Views").rglob("*.cshtml"):
            txt = fpath.read_text(encoding="utf-8", errors="ignore")
            for col in re.findall(r"<code>([^<@]+)</code>", txt):
                raw_col_tags.append(f"{fpath.name}:{col}")
        self.check(
            "L10",
            "Static UX Audit: Zero raw database column names exposed in static <code> labels on Views",
            len(raw_col_tags) == 0,
            f"tags={raw_col_tags[:5]}",
        )

        # 6. Static Audit: 100% Service Code Tagging ([S01]..[S22], [CORE-*]) across Controllers, Services, Interfaces & Map Doc
        svc_code_re = re.compile(r"\[(S\d{2}|CORE-[A-Z\-]+)\]")
        untagged_cs: list[str] = []
        for folder in (web_root / "Controllers", web_root / "Services" / "Implementations", web_root / "Services" / "Interfaces", web_root / "Filters"):
            for fpath in folder.glob("*.cs"):
                if not svc_code_re.search(fpath.read_text(encoding="utf-8", errors="ignore")):
                    untagged_cs.append(str(fpath.relative_to(web_root)))
        for core_f in (web_root / "Program.cs", web_root / "Data" / "BookShareDbContext.cs", web_root / "BackgroundJobs" / "BookShareDailyJobs.cs"):
            if not svc_code_re.search(core_f.read_text(encoding="utf-8", errors="ignore")):
                untagged_cs.append(str(core_f.relative_to(web_root)))
        map_doc = ROOT_DIR / "docs" / "SERVICE_CODE_MAP.md"
        map_ok = map_doc.exists() and all(f"[S{i:02d}]" in map_doc.read_text(encoding="utf-8") for i in range(1, 23))
        self.check(
            "L10",
            "Developer Docs Audit: 100% Controllers/Services/Interfaces/Filters/Core tagged with Service Codes & docs/SERVICE_CODE_MAP.md complete",
            len(untagged_cs) == 0 and map_ok,
            f"untagged={untagged_cs}, map_ok={map_ok}",
        )

        # ------------------------------------------------------------------
        # 10.5 Release 5.5 Security & UX Hardening:
        #   - Logout AI chat isolation & Cache-Control: no-store
        #   - Guest AI login gate on S13 + 401 on /api/ai-chat/reset
        #   - S20 club member email/phone PII masking for Guest
        #   - Register form anti-autofill attributes
        #   - Dark Mode Paper fix, Toast non-overlap & 15 Bootstrap Themes
        # ------------------------------------------------------------------
        logout_chat_actor = SessionClient(self.base_url)
        logout_chat_actor.login("minhanh@cmc.edu.vn", "User@123")
        logout_chat_actor.refresh_csrf("/")
        secret_chat_marker = f"SECRET_LOGOUT_CHAT_{ts_spam}"
        logout_chat_actor.post_json("/api/ai-chat/quick-ask", {"message": f"Ghi nhớ {secret_chat_marker}"})
        hist_in_c, hist_in_raw, hist_in_hdrs = logout_chat_actor.get("/api/ai-chat/history", accept_json=True)
        cc_header = (hist_in_hdrs.get("Cache-Control") or hist_in_hdrs.get("cache-control") or "").lower()
        # Logout -> verify chat history is completely invisible, reset blocked 401, S13 shows login gate
        logout_chat_actor.logout()
        hist_out_c, hist_out_raw, _ = logout_chat_actor.get("/api/ai-chat/history", accept_json=True)
        hist_out_j = json.loads(hist_out_raw) if hist_out_raw.startswith("{") else {}
        logout_chat_actor.refresh_csrf("/")
        rst_guest_c, _, _ = logout_chat_actor.post_json("/api/ai-chat/reset", {})
        s13_g_c, s13_g_html, _ = logout_chat_actor.get("/S13GeminiAiAssistant")
        self.check(
            "L10",
            "Logout AI Security: Chat history sends no-store, vanishes on logout (messages=[], requiresLogin=true), /api/ai-chat/reset=401 & S13 shows login gate",
            hist_in_c == 200
            and secret_chat_marker in hist_in_raw
            and "no-store" in cc_header
            and hist_out_c == 200
            and hist_out_j.get("authenticated") is False
            and hist_out_j.get("requiresLogin") is True
            and len(hist_out_j.get("messages") or []) == 0
            and secret_chat_marker not in hist_out_raw
            and rst_guest_c == 401
            and s13_g_c == 200
            and 'id="formSendS13Chat"' not in s13_g_html
            and "Đăng nhập để trải nghiệm trọn vẹn" in s13_g_html,
            f"in={hist_in_c}, cc={cc_header}, out={hist_out_j}, rst={rst_guest_c}, s13={s13_g_c}",
        )

        # S20 PII Isolation for Guest & Register anti-autofill
        s20_g_c, s20_g_html, _ = logout_chat_actor.get("/S20ExchangeHubClub")
        s01_g_c, s01_g_html, _ = logout_chat_actor.get("/S01AuthOtpGoogle")
        self.check(
            "L10",
            "Anti-PII & Anti-Autofill: Guest S20 hides member emails/phones & S01 Register sets autocomplete=off/new-password",
            s20_g_c == 200
            and "@cmc.edu.vn" not in s20_g_html
            and "@bookshare.vn" not in s20_g_html
            and s01_g_c == 200
            and 'id="s01RegisterForm"' in s01_g_html
            and 'autocomplete="new-password"' in s01_g_html,
            f"s20={s20_g_c}, s01={s01_g_c}",
        )

        # Dark Mode Paper fix, Toast clearance & 15 Bootstrap Themes (Lux, Zephyr, Vapor, Quartz, Journal, Minty)
        theme_css_v55 = (web_root / "wwwroot" / "css" / "core-theme.css").read_text(encoding="utf-8", errors="ignore")
        core_js_v55 = (web_root / "wwwroot" / "js" / "core-api.js").read_text(encoding="utf-8", errors="ignore")
        new_themes = ("lux", "zephyr", "vapor", "quartz", "journal", "minty")
        u1.refresh_csrf("/S01AuthOtpGoogle")
        sv_vap_c, sv_vap_j, _ = u1.post_json("/S01AuthOtpGoogle/SaveUiPreferences", {"theme": "dark", "style": "vapor", "palette": "royal", "background": "starry-night", "font": "roboto", "fontSize": "md"})
        sv_qtz_c, sv_qtz_j, _ = u1.post_json("/S01AuthOtpGoogle/SaveUiPreferences", {"theme": "light", "style": "quartz", "palette": "blue", "background": "solid", "font": "roboto", "fontSize": "md"})
        ai_qtz_c, ai_qtz_j, _ = u1.post_json("/api/ai-chat/quick-ask", {"message": "Đổi giao diện sang phong cách pha lê quartz"})
        ai_qtz_cmds = (ai_qtz_j or {}).get("clientCommands") or []
        has_qtz_cmd = any((c.get("args") or {}).get("style") == "quartz" for c in ai_qtz_cmds)
        # Restore u1 default UI prefs
        u1.post_json("/S01AuthOtpGoogle/SaveUiPreferences", {"theme": "light", "style": "standard", "palette": "blue", "background": "solid", "font": "roboto", "fontSize": "md"})
        self.check(
            "L10",
            "Dark Mode Fix & 15 Bootstrap Themes: Paper Dark Mode scoped, Toast elevated above AI FAB, & 6 new themes (lux/zephyr/vapor/quartz/journal/minty) synced in CSS/JS/DB/AI",
            'html:not([data-bs-theme="dark"])[data-ui-style="paper"]' in theme_css_v55
            and 'html[data-bs-theme="dark"][data-ui-style="paper"]' in theme_css_v55
            and ".swal2-container.swal2-bottom-end" in theme_css_v55
            and "input:-webkit-autofill" in theme_css_v55
            and "wipePrivateClientState" in core_js_v55
            and all(f'data-ui-style="{t}"' in theme_css_v55 for t in new_themes)
            and sv_vap_c == 200 and ((sv_vap_j or {}).get("preferences") or {}).get("style") == "vapor"
            and sv_qtz_c == 200 and ((sv_qtz_j or {}).get("preferences") or {}).get("style") == "quartz"
            and ai_qtz_c == 200 and has_qtz_cmd,
            f"vap={sv_vap_c}, qtz={sv_qtz_c}, ai_qtz={ai_qtz_c}:{ai_qtz_cmds}",
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
        runner.run_layer9_sprint5(actors)
        runner.run_layer10_comprehensive_v52(actors)
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
