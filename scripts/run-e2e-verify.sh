#!/usr/bin/env bash
# [BS-TEST-E2E] Script kiem thu tu dong E2E toan dien 7 nhom nghiep vu BookShare tren Linux / VPS Docker.
# Cach dung:
#   bash scripts/run-e2e-verify.sh http://127.0.0.1:5087
#   bash scripts/run-e2e-verify.sh --autostart

set -euo pipefail

BASE_URL="http://127.0.0.1:5098"
AUTOSTART=0
for arg in "$@"; do
  if [[ "$arg" == "--autostart" ]]; then
    AUTOSTART=1
  elif [[ "$arg" == http://* || "$arg" == https://* ]]; then
    BASE_URL="${arg%/}"
  fi
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SERVER_PID=""
TMP_DIR="$(mktemp -d)"
PASS_COUNT=0
FAIL_COUNT=0

cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "[AUTOSTART] Dung BookShare.Web (PID=$SERVER_PID)..."
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

assert_check() {
  local ok="$1"
  local label="$2"
  local detail="${3:-}"
  if [[ "$ok" == "1" ]]; then
    PASS_COUNT=$((PASS_COUNT + 1))
    echo "  [PASS] $label"
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    echo "  [FAIL] $label ${detail:+=> $detail}"
  fi
}

if [[ "$AUTOSTART" == "1" ]]; then
  echo "[AUTOSTART] Khoi dong tam BookShare.Web tai $BASE_URL ..."
  (
    cd "$REPO_ROOT/src/BookShare.Web"
    dotnet run --project BookShare.Web.csproj --no-build --no-launch-profile -- --urls "$BASE_URL" \
      >"$TMP_DIR/server.out" 2>"$TMP_DIR/server.err"
  ) &
  SERVER_PID=$!

  READY=0
  for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null "$BASE_URL/"; then
      READY=1
      break
    fi
    sleep 0.7
  done
  if [[ "$READY" != "1" ]]; then
    echo "[ERROR] Server khong san sang sau 42s!"
    cat "$TMP_DIR/server.err" || true
    exit 1
  fi
  echo "[AUTOSTART] BookShare.Web da san sang (HTTP 200 OK)."
fi

echo "=== SUITE 1: Kiem tra 25 trang Web & Mobile Super-App (HTTP 200 OK) ==="
PAGES=(
  "/" "/S01AuthOtpGoogle" "/S02PersonalShelf" "/S03ReadingTracker" "/S03ReadingTracker/Studio"
  "/S04BookNotesQuotes" "/S05IsbnAutoCatalog" "/S06SearchDiscovery" "/S07LibraryCatalog"
  "/S08LibraryLoanFlow" "/S09P2PLoanFlow" "/S10ConditionReport" "/S11HotBookQueue"
  "/S12LoanRenewalFine" "/S13GeminiAiAssistant" "/S14AiRecommendation" "/S15TrustScoreReview"
  "/S16MembershipTier" "/S17SandboxPayment" "/S18NotificationHub" "/S19DisputeResolution"
  "/S20ExchangeHubClub" "/S21LibrarianDesk" "/S22AdminAnalytics" "/MobileApp"
)
for p in "${PAGES[@]}"; do
  code="$(curl -sS -o "$TMP_DIR/page.html" -w '%{http_code}' -H "Accept: text/html" "$BASE_URL$p")"
  bad="$(grep -E -c 'An unhandled exception occurred|NullReferenceException|SqlException' "$TMP_DIR/page.html" || true)"
  if [[ "$code" == "200" && "$bad" == "0" ]]; then
    assert_check 1 "GET $p => HTTP 200 OK"
  else
    assert_check 0 "GET $p => HTTP 200 OK" "Status=$code Unhandled=$bad"
  fi
done

echo "=== SUITE 2..6: Chay kiem thu JSON/Cookie E2E qua Python3 ==="
python3 - "$BASE_URL" "$REPO_ROOT" <<'PYEOF'
import json, re, sys, uuid
from http.cookiejar import CookieJar
from urllib import request, error

base = sys.argv[1].rstrip('/')
repo = sys.argv[2]
passed = 0
failed = 0

def check(cond, name, detail=""):
    global passed, failed
    if cond:
        passed += 1
        print(f"  [PASS] {name}")
    else:
        failed += 1
        print(f"  [FAIL] {name}" + (f" => {detail}" if detail else ""))

def new_opener():
    cj = CookieJar()
    return request.build_opener(request.HTTPCookieProcessor(cj)), cj

def req(opener, method, path, body=None, headers=None):
    hdrs = {"Accept": "application/json, text/html;q=0.9"}
    if headers:
        hdrs.update(headers)
    data = None
    if body is not None:
        hdrs["Content-Type"] = "application/json; charset=utf-8"
        data = json.dumps(body).encode("utf-8")
    r = request.Request(base + path, data=data, headers=hdrs, method=method)
    try:
        with opener.open(r, timeout=25) as resp:
            txt = resp.read().decode("utf-8", errors="replace")
            try: js = json.loads(txt)
            except Exception: js = None
            return resp.status, txt, js
    except error.HTTPError as e:
        txt = e.read().decode("utf-8", errors="replace")
        try: js = json.loads(txt)
        except Exception: js = None
        return e.code, txt, js

def sign_in(email):
    op, cj = new_opener()
    st, txt, js = req(op, "POST", "/MobileApp/SignIn", {"email": email, "password": "BookShare@123"})
    return op, cj, st

def get_csrf(op, path):
    _, html, _ = req(op, "GET", path, headers={"Accept": "text/html"})
    m = re.search(r'<meta\s+name="csrf-token"\s+content="([^"]+)"', html, re.I)
    return m.group(1) if m else ""

# SUITE 2: P2P Rental -> Escrow -> Return Intact -> 100% Deposit Refund & Payout
b_op, b_cj, b_st = sign_in("nguyenhung@cmc.edu.vn")
l_op, l_cj, l_st = sign_in("minhanh@cmc.edu.vn")
check(b_st == 200 and l_st == 200, "Dang nhap Borrower & Lender")
req(b_op, "POST", "/api/mobile/wallet/topup", {"amount": 200000, "note": "E2E Prep"})

_, s09_html, _ = req(b_op, "GET", "/S09P2PLoanFlow", headers={"Accept": "text/html"})
b_csrf = get_csrf(b_op, "/S09P2PLoanFlow")
l_csrf = get_csrf(l_op, "/S09P2PLoanFlow")

chosen = None
for m in re.finditer(r'<option\s+value="(\d+)"\s+data-daily-fee="(\d+)"\s+data-deposit="(\d+)">([^<]+)</option>', s09_html):
    cid, df, dp, lbl = int(m.group(1)), int(m.group(2)), int(m.group(3)), m.group(4)
    if df > 0 and dp > 0 and "Minh Anh" in lbl:
        chosen = (cid, df, dp)
        break
check(chosen is not None, f"Tim ban sao P2P co phi + coc Escrow: {chosen}")

if chosen:
    cid, df, dp = chosen
    days = 7
    fee = df * days
    hold = fee + dp
    b0 = float(req(b_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])
    l0 = float(req(l_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])

    st, txt, js = req(b_op, "POST", "/S09P2PLoanFlow/CreateRequest",
                      {"bookCopyId": cid, "pickupHubId": 3, "desiredDays": days},
                      {"RequestVerificationToken": b_csrf})
    m_id = re.search(r"#(\d+)", (js or {}).get("message", ""))
    lr_id = int(m_id.group(1)) if m_id else 0
    check(st == 200 and lr_id > 0, f"CreateRequest P2P #{lr_id}", txt)

    st_a, _, _ = req(l_op, "POST", "/S09P2PLoanFlow/ApproveOrReject",
                     {"loanRequestId": lr_id, "approve": True, "pickupHubId": 3},
                     {"RequestVerificationToken": l_csrf})
    check(st_a == 200, f"Approve P2P #{lr_id}")

    st_h, _, _ = req(l_op, "POST", "/S09P2PLoanFlow/AdvanceStage",
                     {"loanRequestId": lr_id, "stage": "HandoverBorrowed"},
                     {"RequestVerificationToken": l_csrf})
    b1 = float(req(b_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])
    check(st_h == 200 and b1 == b0 - hold, f"HandoverBorrowed khoa Escrow (-{hold})", f"{b0}->{b1}")

    st_p, _, _ = req(b_op, "POST", "/api/mobile/reading/log-session",
                     {"bookId": 6, "durationMinutes": 25, "pagesRead": 5})
    check(st_p == 200, "Reading Studio log-session 200 OK")

    st_r, _, _ = req(l_op, "POST", "/S09P2PLoanFlow/AdvanceStage",
                     {"loanRequestId": lr_id, "stage": "Returned"},
                     {"RequestVerificationToken": l_csrf})
    b2 = float(req(b_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])
    l2 = float(req(l_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])
    check(st_r == 200 and b2 == b1 + dp, f"Hoan coc 100% (+{dp})", f"{b1}->{b2}")
    check(l2 == l0 + fee, f"Giai ngan phi thue cho chu sach (+{fee})", f"{l0}->{l2}")

# SUITE 3: QR Cross-Login
pc_op, pc_cj = new_opener()
st_i, _, js_i = req(pc_op, "POST", "/api/mobile/qr-login/init", {})
tok = (js_i or {}).get("token", "")
check(st_i == 200 and tok.startswith("BSQR-"), f"QR init: {tok}")
check(req(new_opener()[0], "POST", "/api/mobile/qr-login/approve", {"token": tok})[0] == 403, "Guest approve bi chan 403")
check(req(b_op, "POST", "/api/mobile/qr-login/approve", {"token": tok})[0] == 200, "Mobile approve 200 OK")
check(req(pc_op, "GET", f"/api/mobile/qr-login/status?token={tok}")[2]["status"] == "Approved", "QR status == Approved")
st_c1, _, _ = req(pc_op, "POST", "/S01AuthOtpGoogle/ClaimQrLogin", {"token": tok})
has_ck = any(c.name == "BookShare.AuthCookie" for c in pc_cj)
check(st_c1 == 200 and has_ck, "PC ClaimQrLogin lan 1 => 200 + AuthCookie")
check(req(new_opener()[0], "POST", "/S01AuthOtpGoogle/ClaimQrLogin", {"token": tok})[0] == 400, "Anti-Replay: ClaimQrLogin lan 2 => 400")

# SUITE 4: Wallet Pay
wb0 = float(req(b_op, "GET", "/api/mobile/wallet/summary")[2]["balance"])
st_t, _, js_t = req(b_op, "POST", "/api/mobile/wallet/topup", {"amount": 150000, "note": "Bash E2E"})
wb1 = float((js_t or {}).get("balance", 0))
check(st_t == 200 and wb1 == wb0 + 150000 and len((js_t or {}).get("hmacSignature", "")) == 128, "TopUp +150k & HMAC-SHA512")
ref = "BASH-QR-" + uuid.uuid4().hex[:8]
check(req(b_op, "POST", "/api/mobile/wallet/pay-qr", {"amount": 25000, "pin": "000000", "paymentRef": ref})[0] == 400, "Wrong PIN => 400")
st_ok, _, js_ok = req(b_op, "POST", "/api/mobile/wallet/pay-qr", {"amount": 25000, "pin": "123456", "paymentRef": ref})
check(st_ok == 200 and float(js_ok["balance"]) == wb1 - 25000, "Right PIN => 200 & tru dung")
check(req(b_op, "POST", "/api/mobile/wallet/pay-qr", {"amount": 25000, "pin": "123456", "paymentRef": ref})[0] == 400, "Duplicate ref => 400")

# SUITE 5: AI Copilot & OTP Subject & Support Chat
st_ai1, _, js_ai1 = req(b_op, "POST", "/api/ai-chat/quick-ask", {"message": "Bật chế độ tối và đổi theme sang Emerald"})
acts1 = (js_ai1 or {}).get("actions", [])
check(st_ai1 == 200 and any(a.get("type") == "SET_DARK_MODE" for a in acts1) and any(a.get("type") == "SET_THEME" and a.get("value") == "emerald" for a in acts1),
      "AI Copilot SET_DARK_MODE & SET_THEME=emerald")
st_ai2, _, js_ai2 = req(b_op, "POST", "/api/ai-chat/quick-ask", {"message": "Gửi OTP xác minh email"})
subj = (js_ai2 or {}).get("emailSubject", "")
check(st_ai2 == 200 and bool(subj) and not re.search(r"\b\d{6}\b", subj), f"OTP Subject an toan: {subj}")
check(req(b_op, "POST", "/api/mobile/support-chat/send", {"channel": "Librarian", "messageText": "Bash E2E chat"})[0] == 200, "Support chat send 200")

# SUITE 6: RBAC 403
u_csrf = get_csrf(b_op, "/S01AuthOtpGoogle")
for ep, body in [
    ("/S22AdminAnalytics/ManageSampleData", {"action": "SeedSample"}),
    ("/S21LibrarianDesk/BatchImportCopies", {"bookId": 1, "libraryId": 1, "quantity": 1, "shelfLocation": "K1"}),
    ("/S19DisputeResolution/ResolveDispute", {"disputeId": 1, "resolution": "Resolved", "adminNote": "x"}),
    ("/S20ExchangeHubClub/VerifyPhysicalBook", {"bookCopyId": 33, "condition": "Good", "note": "x"})
]:
    check(req(new_opener()[0], "POST", ep, body)[0] == 403, f"RBAC Guest {ep} => 403")
    check(req(b_op, "POST", ep, body, {"RequestVerificationToken": u_csrf})[0] == 403, f"RBAC User {ep} => 403")

print(f"PY_SUMMARY: PASS={passed} FAIL={failed}")
sys.exit(0 if failed == 0 else 1)
PYEOF

echo "=== SUITE 7: Kiem tra tinh — 0 Web Audio API trong Views & wwwroot ==="
AUDIO_HITS="$(grep -R -E 'AudioContext|webkitAudioContext|new[[:space:]]+Audio[[:space:]]*\(|<audio\b|createOscillator' \
  "$REPO_ROOT/src/BookShare.Web/Views" \
  "$REPO_ROOT/src/BookShare.Web/wwwroot/js" \
  "$REPO_ROOT/src/BookShare.Web/wwwroot/modules" 2>/dev/null || true)"
if [[ -z "$AUDIO_HITS" ]]; then
  assert_check 1 "0 Web Audio API / <audio> trong Views & wwwroot"
else
  assert_check 0 "0 Web Audio API / <audio> trong Views & wwwroot" "$AUDIO_HITS"
fi

echo "========================================================================"
echo "  BASH E2E HOAN TAT: $PASS_COUNT PASS / $FAIL_COUNT FAIL (cong them Python Suites)"
echo "========================================================================"
[[ "$FAIL_COUNT" == "0" ]]
