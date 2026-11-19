<#
.SYNOPSIS
    [BS-TEST-E2E] Script kiem thu tu dong E2E toan dien 7 nhom nghiep vu BookShare (ASP.NET Core 10 + SQL Server).
.DESCRIPTION
    Kiem thu 7 nhom:
      1. 25 trang Web (Home + 22 dich vu + Reading Studio + Mobile Super-App): tat ca 200 OK, 0 loi 500.
      2. Luong Thue sach P2P -> Khoa coc Escrow -> Ghi phien doc Pomodoro -> Tra sach nguyen ven -> Hoan coc 100% + Giai ngan chu sach + Chan muon qua han.
      3. QR Cross-Login (Mobile -> Web PC): init -> guest approve bi chan 403 -> mobile approve 200 -> PC ClaimQrLogin cap Cookie -> Claim lan 2 bi chan 400 (anti-replay).
      4. Vi Bao Lanh BookShare Pay: topup (kiem tra so du + HMAC-SHA512) -> pay-qr sai PIN (400, khong tru tien) -> pay-qr dung PIN 123456 (200, tru dung) -> pay-qr trung paymentRef (400, chong tru lap).
      5. AI Copilot Hanh Dong & Chat Thu Thu + OTP Email Subject Hygiene (khong lo ma 6 so o tieu de).
      6. Phan quyen RBAC 403 (Khach & User thuong goi API Admin/Librarian/Curator deu bi chan 403 Forbidden).
      7. Kiem tra tinh ma nguon: 0 Web Audio API (AudioContext, webkitAudioContext, new Audio(, <audio, createOscillator) trong Views & wwwroot.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\run-e2e-verify.ps1 -Autostart
    powershell -ExecutionPolicy Bypass -File scripts\run-e2e-verify.ps1 -BaseUrl "http://127.0.0.1:5087"
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:5098",
    [switch]$Autostart
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Script:RepoRoot = Split-Path -Parent $PSScriptRoot
$Script:PassCount = 0
$Script:FailCount = 0
$Script:Failures = New-Object System.Collections.Generic.List[string]
$Script:ServerProc = $null

function Write-SuiteHeader([string]$Title) {
    Write-Host ""
    Write-Host "========================================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "========================================================================" -ForegroundColor Cyan
}

function Assert-Check([bool]$Condition, [string]$Name, [string]$Detail = "") {
    if ($Condition) {
        $Script:PassCount++
        Write-Host "  [PASS] $Name" -ForegroundColor Green
    } else {
        $Script:FailCount++
        $msg = if ([string]::IsNullOrWhiteSpace($Detail)) { $Name } else { "$Name => $Detail" }
        $Script:Failures.Add($msg)
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
    }
}

function Invoke-RawHttp {
    param(
        [string]$Method = "GET",
        [string]$Path,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session = $null,
        [string]$JsonBody = $null,
        [hashtable]$ExtraHeaders = @{}
    )

    $url = "$BaseUrl$Path"
    $headers = @{ "Accept" = "application/json, text/html;q=0.9" }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }

    $params = @{
        Uri             = $url
        Method          = $Method
        Headers         = $headers
        UseBasicParsing = $true
        TimeoutSec      = 25
    }
    if ($null -ne $Session) { $params["WebSession"] = $Session }
    if (-not [string]::IsNullOrEmpty($JsonBody)) {
        $params["ContentType"] = "application/json; charset=utf-8"
        $params["Body"] = [System.Text.Encoding]::UTF8.GetBytes($JsonBody)
    }

    try {
        $resp = Invoke-WebRequest @params
        $rawText = [string]$resp.Content
        if ($resp.RawContentStream -and $resp.RawContentStream.Length -gt 0) {
            try {
                $resp.RawContentStream.Position = 0
                $ms = New-Object System.IO.MemoryStream
                $resp.RawContentStream.CopyTo($ms)
                $rawText = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
            } catch { }
        }
        $jsonObj = $null
        try { $jsonObj = $rawText | ConvertFrom-Json } catch { }
        return [PSCustomObject]@{
            StatusCode = [int]$resp.StatusCode
            Content    = $rawText
            Json       = $jsonObj
            Headers    = $resp.Headers
        }
    } catch {
        $status = 0
        $body = ""
        $ex = $_.Exception
        if ($ex.Response) {
            try { $status = [int]$ex.Response.StatusCode } catch { }
            try {
                if ($ex.Response -is [System.Net.HttpWebResponse]) {
                    $sr = New-Object System.IO.StreamReader($ex.Response.GetResponseStream())
                    $body = $sr.ReadToEnd()
                    $sr.Close()
                } elseif ($ex.Response.Content) {
                    $body = $ex.Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                }
            } catch { }
        }
        if ([string]::IsNullOrWhiteSpace($body) -and $_.ErrorDetails -and $_.ErrorDetails.Message) {
            $body = $_.ErrorDetails.Message
        }
        $jsonObj = $null
        try { $jsonObj = $body | ConvertFrom-Json } catch { }
        return [PSCustomObject]@{
            StatusCode = $status
            Content    = [string]$body
            Json       = $jsonObj
            Headers    = @{}
        }
    }
}

function Get-CsrfToken {
    param(
        [string]$Path,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session
    )
    $r = Invoke-RawHttp -Method "GET" -Path $Path -Session $Session -ExtraHeaders @{ "Accept" = "text/html" }
    $m = [regex]::Match($r.Content, '<meta\s+name="csrf-token"\s+content="([^"]+)"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($m.Success) { return $m.Groups[1].Value }
    $m2 = [regex]::Match($r.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($m2.Success) { return $m2.Groups[1].Value }
    return ""
}

function New-SignedInSession {
    param([string]$Email, [string]$Password = "BookShare@123")
    $sess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $payload = (@{ email = $Email; password = $Password } | ConvertTo-Json -Compress)
    $r = Invoke-RawHttp -Method "POST" -Path "/MobileApp/SignIn" -Session $sess -JsonBody $payload
    return [PSCustomObject]@{
        Session = $sess
        Result  = $r
    }
}

function Start-TempServer {
    $webDir = Join-Path $Script:RepoRoot "src\BookShare.Web"
    $csproj = Join-Path $webDir "BookShare.Web.csproj"
    Write-Host "[AUTOSTART] Khoi dong tam BookShare.Web tai $BaseUrl ..." -ForegroundColor Yellow

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "run --project `"$csproj`" --no-build --no-launch-profile -- --urls $BaseUrl"
    $psi.WorkingDirectory = $webDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $Script:ServerProc = [System.Diagnostics.Process]::Start($psi)

    $deadline = (Get-Date).AddSeconds(45)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($Script:ServerProc.HasExited) {
            $errText = $Script:ServerProc.StandardError.ReadToEnd()
            $outText = $Script:ServerProc.StandardOutput.ReadToEnd()
            throw "BookShare.Web da thoat som (ExitCode=$($Script:ServerProc.ExitCode)).`nSTDOUT: $outText`nSTDERR: $errText"
        }
        try {
            $ping = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing -TimeoutSec 3
            if ($ping.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            Start-Sleep -Milliseconds 700
        }
    }

    if (-not $ready) {
        throw "Qua thoi gian cho 45s nhung $BaseUrl/ chua san sang."
    }
    Write-Host "[AUTOSTART] May chu BookShare.Web da san sang (HTTP 200 OK)." -ForegroundColor Green
}

try {
    if ($Autostart) {
        Start-TempServer
    }

    # =========================================================================
    # SUITE 1: 25 TRANG WEB (HOME + 22 DICH VU + READING STUDIO + MOBILE APP)
    # =========================================================================
    Write-SuiteHeader "SUITE 1: Kiem tra 25 trang Web & Mobile Super-App (HTTP 200 OK, 0 loi 500)"
    $guestSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $pages = @(
        "/",
        "/S01AuthOtpGoogle",
        "/S02PersonalShelf",
        "/S03ReadingTracker",
        "/S03ReadingTracker/Studio",
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
        "/MobileApp"
    )

    foreach ($p in $pages) {
        $res = Invoke-RawHttp -Method "GET" -Path $p -Session $guestSession -ExtraHeaders @{ "Accept" = "text/html" }
        $hasUnhandled = ($res.Content -match "An unhandled exception occurred|NullReferenceException|SqlException")
        Assert-Check ($res.StatusCode -eq 200 -and -not $hasUnhandled) "GET $p => HTTP 200 OK" "Status=$($res.StatusCode), Unhandled=$hasUnhandled"
    }

    # =========================================================================
    # SUITE 2: LUONG THUE SACH P2P -> KHOA COC ESCROW -> TRA SACH -> HOAN COC 100%
    # =========================================================================
    Write-SuiteHeader "SUITE 2: Thue sach P2P -> Khoa coc Escrow -> Pomodoro -> Tra nguyen ven -> Hoan coc 100%"
    $borrowerAuth = New-SignedInSession -Email "nguyenhung@cmc.edu.vn"
    $lenderAuth = New-SignedInSession -Email "minhanh@cmc.edu.vn"
    Assert-Check ($borrowerAuth.Result.StatusCode -eq 200) "Dang nhap Nguoi thue (nguyenhung@cmc.edu.vn)" "Status=$($borrowerAuth.Result.StatusCode)"
    Assert-Check ($lenderAuth.Result.StatusCode -eq 200) "Dang nhap Chu sach P2P (minhanh@cmc.edu.vn)" "Status=$($lenderAuth.Result.StatusCode)"

    # Dam bao vi nguoi thue co du so du (>= 200,000) truoc khi thue
    $preTop = Invoke-RawHttp -Method "POST" -Path "/api/mobile/wallet/topup" -Session $borrowerAuth.Session -JsonBody '{"amount":200000,"note":"E2E Rental Prep"}'
    Assert-Check ($preTop.StatusCode -eq 200) "Nap du phong Vi nguoi thue +200,000 VND" "Status=$($preTop.StatusCode)"

    $s09Html = (Invoke-RawHttp -Method "GET" -Path "/S09P2PLoanFlow" -Session $borrowerAuth.Session -ExtraHeaders @{ "Accept" = "text/html" }).Content
    $borrowerCsrf = Get-CsrfToken -Path "/S09P2PLoanFlow" -Session $borrowerAuth.Session
    $lenderCsrf = Get-CsrfToken -Path "/S09P2PLoanFlow" -Session $lenderAuth.Session

    # Tim ban sao P2P dang Available thuoc Minh Anh co DailyRentalFee > 0 & DepositAmount > 0
    $optMatches = [regex]::Matches($s09Html, '<option\s+value="(\d+)"\s+data-daily-fee="(\d+)"\s+data-deposit="(\d+)">([^<]+)</option>')
    $chosenCopyId = 0
    $chosenDailyFee = 0
    $chosenDeposit = 0
    foreach ($om in $optMatches) {
        $cid = [int]$om.Groups[1].Value
        $df = [int]$om.Groups[2].Value
        $dp = [int]$om.Groups[3].Value
        $lbl = $om.Groups[4].Value
        if ($df -gt 0 -and $dp -gt 0 -and $lbl -match "Minh Anh") {
            $chosenCopyId = $cid; $chosenDailyFee = $df; $chosenDeposit = $dp; break
        }
    }
    Assert-Check ($chosenCopyId -gt 0) "Tim thay ban sao P2P co phi thue + coc Escrow cua Minh Anh (CopyId=$chosenCopyId, Fee=$chosenDailyFee/ng, Deposit=$chosenDeposit)" "Khong tim thay ban sao phu hop"

    if ($chosenCopyId -gt 0) {
        $days = 7
        $expectedFee = [decimal]($chosenDailyFee * $days)
        $expectedDeposit = [decimal]$chosenDeposit
        $expectedHold = $expectedFee + $expectedDeposit

        $bWal0 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json
        $lWal0 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $lenderAuth.Session).Json
        $bBal0 = [decimal]$bWal0.balance
        $lBal0 = [decimal]$lWal0.balance

        # 2.1 Tao yeu cau thue P2P
        $reqBody = (@{ bookCopyId = $chosenCopyId; pickupHubId = 3; desiredDays = $days; requestNote = "E2E Escrow Verification" } | ConvertTo-Json -Compress)
        $createRes = Invoke-RawHttp -Method "POST" -Path "/S09P2PLoanFlow/CreateRequest" -Session $borrowerAuth.Session -JsonBody $reqBody -ExtraHeaders @{ "RequestVerificationToken" = $borrowerCsrf }
        Assert-Check ($createRes.StatusCode -eq 200) "POST /S09P2PLoanFlow/CreateRequest thanh cong (200)" "Status=$($createRes.StatusCode) Body=$($createRes.Content)"

        $lrId = 0
        $idMatch = [regex]::Match([string]$createRes.Json.message, '#(\d+)')
        if ($idMatch.Success) { $lrId = [int]$idMatch.Groups[1].Value }
        Assert-Check ($lrId -gt 0) "Trich xuat duoc LoanRequestId = #$lrId" "Message=$($createRes.Json.message)"

        if ($lrId -gt 0) {
            # 2.2 Chu sach duyet yeu cau
            $appBody = (@{ loanRequestId = $lrId; approve = $true; pickupHubId = 3 } | ConvertTo-Json -Compress)
            $appRes = Invoke-RawHttp -Method "POST" -Path "/S09P2PLoanFlow/ApproveOrReject" -Session $lenderAuth.Session -JsonBody $appBody -ExtraHeaders @{ "RequestVerificationToken" = $lenderCsrf }
            Assert-Check ($appRes.StatusCode -eq 200) "Chu sach duyet phieu thue P2P #$lrId" "Status=$($appRes.StatusCode) Body=$($appRes.Content)"

            # 2.3 Xac nhan giao sach (HandoverBorrowed) -> Tru Phi thue + Coc Escrow
            $hoBody = (@{ loanRequestId = $lrId; stage = "HandoverBorrowed" } | ConvertTo-Json -Compress)
            $hoRes = Invoke-RawHttp -Method "POST" -Path "/S09P2PLoanFlow/AdvanceStage" -Session $lenderAuth.Session -JsonBody $hoBody -ExtraHeaders @{ "RequestVerificationToken" = $lenderCsrf }
            Assert-Check ($hoRes.StatusCode -eq 200) "Ban giao sach P2P (HandoverBorrowed) & khoa coc Escrow" "Status=$($hoRes.StatusCode) Body=$($hoRes.Content)"

            $bWal1 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json
            $bBal1 = [decimal]$bWal1.balance
            Assert-Check ($bBal1 -eq ($bBal0 - $expectedHold)) "Vi nguoi thue tru dung Escrow (-$expectedHold VND: $bBal0 -> $bBal1)" "Expected=$($bBal0 - $expectedHold), Actual=$bBal1"

            # 2.4 Ghi nhan 1 phien doc Pomodoro im lang trong Reading Studio
            $pomRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/reading/log-session" -Session $borrowerAuth.Session -JsonBody '{"bookId":6,"durationMinutes":25,"pagesRead":5}'
            Assert-Check ($pomRes.StatusCode -eq 200) "Reading Studio: POST /api/mobile/reading/log-session (200 OK)" "Status=$($pomRes.StatusCode) Body=$($pomRes.Content)"

            # 2.5 Tra sach nguyen ven -> Hoan coc 100% cho nguoi thue + Giai ngan phi thue cho chu sach
            $retBody = (@{ loanRequestId = $lrId; stage = "Returned" } | ConvertTo-Json -Compress)
            $retRes = Invoke-RawHttp -Method "POST" -Path "/S09P2PLoanFlow/AdvanceStage" -Session $lenderAuth.Session -JsonBody $retBody -ExtraHeaders @{ "RequestVerificationToken" = $lenderCsrf }
            Assert-Check ($retRes.StatusCode -eq 200) "Tra sach nguyen ven (Returned) & tat toan Escrow" "Status=$($retRes.StatusCode) Body=$($retRes.Content)"

            $bWal2 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json
            $lWal2 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $lenderAuth.Session).Json
            $bBal2 = [decimal]$bWal2.balance
            $lBal2 = [decimal]$lWal2.balance

            Assert-Check ($bBal2 -eq ($bBal1 + $expectedDeposit)) "Hoan coc 100% ve Vi nguoi thue (+$expectedDeposit VND: $bBal1 -> $bBal2)" "Expected=$($bBal1 + $expectedDeposit), Actual=$bBal2"
            Assert-Check ($lBal2 -eq ($lBal0 + $expectedFee)) "Giai ngan doanh thu phi thue ve Vi chu sach (+$expectedFee VND: $lBal0 -> $lBal2)" "Expected=$($lBal0 + $expectedFee), Actual=$lBal2"
        }

        # 2.6 Kiem tra luat chan muon thong minh voi tai khoan dang qua han/tranh chap (hoangnam@cmc.edu.vn)
        $badAuth = New-SignedInSession -Email "hoangnam@cmc.edu.vn"
        $badCsrf = Get-CsrfToken -Path "/S09P2PLoanFlow" -Session $badAuth.Session
        $badReqBody = (@{ bookCopyId = $chosenCopyId; pickupHubId = 3; desiredDays = 7 } | ConvertTo-Json -Compress)
        $badRes = Invoke-RawHttp -Method "POST" -Path "/S09P2PLoanFlow/CreateRequest" -Session $badAuth.Session -JsonBody $badReqBody -ExtraHeaders @{ "RequestVerificationToken" = $badCsrf }
        Assert-Check ($badRes.StatusCode -eq 400) "Luat chan muon thong minh: Chặn hoangnam@cmc.edu.vn (Overdue/Disputed) => 400 BadRequest" "Status=$($badRes.StatusCode) Body=$($badRes.Content)"
    }

    # =========================================================================
    # SUITE 3: QR CROSS-LOGIN (MOBILE -> WEB PC) + CHONG DUNG LAI (ANTI-REPLAY)
    # =========================================================================
    Write-SuiteHeader "SUITE 3: QR Cross-Login (Mobile -> Web PC) & Chong dung lai token 2 lan"
    $pcSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $initRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/qr-login/init" -Session $pcSession -JsonBody "{}"
    $qrToken = [string]$initRes.Json.token
    Assert-Check ($initRes.StatusCode -eq 200 -and $qrToken.StartsWith("BSQR-")) "POST /api/mobile/qr-login/init tao token $qrToken" "Status=$($initRes.StatusCode)"

    # Khach chua dang nhap goi approve => 403 Forbidden
    $anonApprove = Invoke-RawHttp -Method "POST" -Path "/api/mobile/qr-login/approve" -Session (New-Object Microsoft.PowerShell.Commands.WebRequestSession) -JsonBody (@{ token = $qrToken } | ConvertTo-Json -Compress)
    Assert-Check ($anonApprove.StatusCode -eq 403) "Khach chua dang nhap goi /api/mobile/qr-login/approve bi chan 403" "Status=$($anonApprove.StatusCode)"

    # Mobile da dang nhap (borrowerAuth) duyet QR => 200 OK
    $mobApprove = Invoke-RawHttp -Method "POST" -Path "/api/mobile/qr-login/approve" -Session $borrowerAuth.Session -JsonBody (@{ token = $qrToken } | ConvertTo-Json -Compress)
    Assert-Check ($mobApprove.StatusCode -eq 200) "Mobile da dang nhap duyet QR /api/mobile/qr-login/approve => 200 OK" "Status=$($mobApprove.StatusCode) Body=$($mobApprove.Content)"

    # PC polling trang thai => Approved
    $pollRes = Invoke-RawHttp -Method "GET" -Path "/api/mobile/qr-login/status?token=$qrToken" -Session $pcSession
    Assert-Check ($pollRes.StatusCode -eq 200 -and $pollRes.Json.status -eq "Approved") "GET /api/mobile/qr-login/status => Approved" "Status=$($pollRes.StatusCode), State=$($pollRes.Json.status)"

    # PC goi ClaimQrLogin lan 1 => 200 OK & nhan BookShare.AuthCookie
    $claim1 = Invoke-RawHttp -Method "POST" -Path "/S01AuthOtpGoogle/ClaimQrLogin" -Session $pcSession -JsonBody (@{ token = $qrToken } | ConvertTo-Json -Compress)
    $cookies = $pcSession.Cookies.GetCookies([Uri]$BaseUrl)
    $hasAuthCookie = ($null -ne $cookies["BookShare.AuthCookie"])
    Assert-Check ($claim1.StatusCode -eq 200 -and $hasAuthCookie) "PC ClaimQrLogin lan 1 thanh cong (200) & cap BookShare.AuthCookie" "Status=$($claim1.StatusCode), Cookie=$hasAuthCookie"

    # PC goi ClaimQrLogin lan 2 cung token => PHAI chan 400 (Anti-Replay)
    $claim2 = Invoke-RawHttp -Method "POST" -Path "/S01AuthOtpGoogle/ClaimQrLogin" -Session (New-Object Microsoft.PowerShell.Commands.WebRequestSession) -JsonBody (@{ token = $qrToken } | ConvertTo-Json -Compress)
    Assert-Check ($claim2.StatusCode -eq 400) "Anti-Replay: ClaimQrLogin lan 2 cung token bi chan 400" "Status=$($claim2.StatusCode) Body=$($claim2.Content)"

    # =========================================================================
    # SUITE 4: VI BAO LANH BOOKSHARE PAY (TOPUP + PIN + CHONG TRU LAP)
    # =========================================================================
    Write-SuiteHeader "SUITE 4: Vi Bao Lanh BookShare Pay (TopUp HMAC-SHA512 + PIN + Chong tru lap)"
    $w0 = (Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json
    $balBefore = [decimal]$w0.balance

    $topRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/wallet/topup" -Session $borrowerAuth.Session -JsonBody '{"amount":150000,"note":"E2E Suite 4 TopUp"}'
    $balAfterTop = [decimal]$topRes.Json.balance
    $hmacHex = [string]$topRes.Json.hmacSignature
    Assert-Check ($topRes.StatusCode -eq 200 -and $balAfterTop -eq ($balBefore + 150000) -and $hmacHex.Length -eq 128) "TopUp +150,000 VND thanh cong & chu ky HMAC-SHA512 dai 128 hex" "Status=$($topRes.StatusCode), Bal=$balAfterTop, HmacLen=$($hmacHex.Length)"

    $uniqueRef = "E2E-QR-" + [Guid]::NewGuid().ToString("N").Substring(0, 10)
    # 4.1 Sai PIN ("000000") => 400 & khong tru tien
    $wrongPinBody = (@{ amount = 25000; pin = "000000"; paymentRef = $uniqueRef; description = "Wrong PIN test" } | ConvertTo-Json -Compress)
    $wrongPinRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/wallet/pay-qr" -Session $borrowerAuth.Session -JsonBody $wrongPinBody
    $balAfterWrong = [decimal](Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json.balance
    Assert-Check ($wrongPinRes.StatusCode -eq 400 -and $balAfterWrong -eq $balAfterTop) "Pay-QR sai PIN (000000) bi tu choi 400 & khong tru tien" "Status=$($wrongPinRes.StatusCode), Bal=$balAfterWrong"

    # 4.2 Dung PIN ("123456") => 200 & tru dung 25,000
    $rightPinBody = (@{ amount = 25000; pin = "123456"; paymentRef = $uniqueRef; description = "Right PIN test" } | ConvertTo-Json -Compress)
    $rightPinRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/wallet/pay-qr" -Session $borrowerAuth.Session -JsonBody $rightPinBody
    $balAfterRight = [decimal]$rightPinRes.Json.balance
    Assert-Check ($rightPinRes.StatusCode -eq 200 -and $balAfterRight -eq ($balAfterTop - 25000)) "Pay-QR dung PIN (123456) thanh cong & tru dung 25,000 VND" "Status=$($rightPinRes.StatusCode), Bal=$balAfterRight"

    # 4.3 Gui lai dung paymentRef lan 2 => 400 Chong tru lap (Idempotency)
    $dupRes = Invoke-RawHttp -Method "POST" -Path "/api/mobile/wallet/pay-qr" -Session $borrowerAuth.Session -JsonBody $rightPinBody
    $balAfterDup = [decimal](Invoke-RawHttp -Method "GET" -Path "/api/mobile/wallet/summary" -Session $borrowerAuth.Session).Json.balance
    Assert-Check ($dupRes.StatusCode -eq 400 -and $balAfterDup -eq $balAfterRight) "Anti-Double-Charge: Gui lai cung paymentRef bi chan 400 & khong tru tien lan 2" "Status=$($dupRes.StatusCode), Bal=$balAfterDup"

    # =========================================================================
    # SUITE 5: AI COPILOT HANH DONG, CHAT THU THU & BAO MAT TIEU DE EMAIL OTP
    # =========================================================================
    Write-SuiteHeader "SUITE 5: AI Copilot Hanh Dong, Chat Thu Thu & Bao mat Tieu de Email OTP"
    # "Bật chế độ tối và đổi theme sang Emerald"
    $aiThemeJson = '{"message":"B\u1eadt ch\u1ebf \u0111\u1ed9 t\u1ed1i v\u00e0 \u0111\u1ed5i theme sang Emerald"}'
    $ai1 = Invoke-RawHttp -Method "POST" -Path "/api/ai-chat/quick-ask" -Session $borrowerAuth.Session -JsonBody $aiThemeJson
    $hasDark = @($ai1.Json.actions | Where-Object { $_.type -eq "SET_DARK_MODE" -and $_.value -eq "dark" }).Count -ge 1
    $hasEmerald = @($ai1.Json.actions | Where-Object { $_.type -eq "SET_THEME" -and $_.value -eq "emerald" }).Count -ge 1
    Assert-Check ($ai1.StatusCode -eq 200 -and $hasDark -and $hasEmerald) "AI Copilot tra ve actions SET_DARK_MODE=dark & SET_THEME=emerald" "Status=$($ai1.StatusCode), Body=$($ai1.Content)"

    # "Gửi email nhắc hạn trả sách"
    $aiMailJson = '{"message":"G\u1eedi email nh\u1eafc h\u1ea1n tr\u1ea3 s\u00e1ch"}'
    $ai2 = Invoke-RawHttp -Method "POST" -Path "/api/ai-chat/quick-ask" -Session $borrowerAuth.Session -JsonBody $aiMailJson
    $hasSendMail = @($ai2.Json.actions | Where-Object { $_.type -eq "SEND_EMAIL" }).Count -ge 1
    Assert-Check ($ai2.StatusCode -eq 200 -and $hasSendMail) "AI Copilot thuc thi hanh dong SEND_EMAIL nhac han" "Status=$($ai2.StatusCode), Body=$($ai2.Content)"

    # "Gửi OTP xác minh email" => Tieu de email TUYET DOI khong chua 6 chu so OTP
    $aiOtpJson = '{"message":"G\u1eedi OTP x\u00e1c minh email"}'
    $ai3 = Invoke-RawHttp -Method "POST" -Path "/api/ai-chat/quick-ask" -Session $borrowerAuth.Session -JsonBody $aiOtpJson
    $otpSubj = [string]$ai3.Json.emailSubject
    $leaksSixDigits = ($otpSubj -match '\b\d{6}\b')
    Assert-Check ($ai3.StatusCode -eq 200 -and -not [string]::IsNullOrWhiteSpace($otpSubj) -and -not $leaksSixDigits) "Tieu de Email OTP an toan (khong lo ma 6 chu so): '$otpSubj'" "Status=$($ai3.StatusCode), Subject=$otpSubj, Body=$($ai3.Content)"

    # Kiem tra tinh trong ResendEmailService.cs
    $resendPath = Join-Path $Script:RepoRoot "src\BookShare.Web\Services\Implementations\ResendEmailService.cs"
    $resendText = Get-Content -Path $resendPath -Raw
    $resendLeaks = ($resendText -match 'subject\s*=\s*\$"[^"]*otpCode')
    Assert-Check (-not $resendLeaks) "ResendEmailService.cs khong noi suy ma OTP vao Subject email" "Phat hien noi suy otpCode vao subject!"

    # Chat truc tiep Thu thu (SupportChatMessages)
    $chatSend = Invoke-RawHttp -Method "POST" -Path "/api/mobile/support-chat/send" -Session $borrowerAuth.Session -JsonBody '{"channel":"Librarian","messageText":"E2E test tin nhan toi Thu thu"}'
    $chatList = Invoke-RawHttp -Method "GET" -Path "/api/mobile/support-chat/messages?channel=Librarian" -Session $borrowerAuth.Session
    Assert-Check ($chatSend.StatusCode -eq 200 -and $chatList.StatusCode -eq 200 -and $chatList.Content -match "E2E test tin nhan toi Thu thu") "Chat truc tiep Thu thu (POST + GET /api/mobile/support-chat/*) hoat dong" "Send=$($chatSend.StatusCode), Get=$($chatList.StatusCode)"

    # =========================================================================
    # SUITE 6: PHAN QUYEN RBAC 403 FORBIDDEN (KHACH & USER THUONG)
    # =========================================================================
    Write-SuiteHeader "SUITE 6: Phan quyen RBAC 403 Forbidden (Khach & User thuong bi chan API quan tri)"
    $rbacTargets = @(
        @{ Path = "/S22AdminAnalytics/ManageSampleData";    Body = '{"action":"SeedSample"}' },
        @{ Path = "/S21LibrarianDesk/BatchImportCopies";    Body = '{"bookId":1,"libraryId":1,"quantity":1,"shelfLocation":"K1"}' },
        @{ Path = "/S19DisputeResolution/ResolveDispute";   Body = '{"disputeId":1,"resolution":"Resolved","adminNote":"test"}' },
        @{ Path = "/S20ExchangeHubClub/VerifyPhysicalBook"; Body = '{"bookCopyId":33,"condition":"Good","note":"test"}' }
    )

    $userCsrf = Get-CsrfToken -Path "/S01AuthOtpGoogle" -Session $borrowerAuth.Session
    foreach ($t in $rbacTargets) {
        $gRes = Invoke-RawHttp -Method "POST" -Path $t.Path -Session (New-Object Microsoft.PowerShell.Commands.WebRequestSession) -JsonBody $t.Body
        Assert-Check ($gRes.StatusCode -eq 403) "RBAC Guest POST $($t.Path) => 403 Forbidden" "Status=$($gRes.StatusCode)"

        $uRes = Invoke-RawHttp -Method "POST" -Path $t.Path -Session $borrowerAuth.Session -JsonBody $t.Body -ExtraHeaders @{ "RequestVerificationToken" = $userCsrf }
        Assert-Check ($uRes.StatusCode -eq 403) "RBAC User (nguyenhung) POST $($t.Path) => 403 Forbidden" "Status=$($uRes.StatusCode)"
    }

    # =========================================================================
    # SUITE 7: KIEM TRA TINH — TUYET DOI KHONG CO WEB AUDIO API TRONG VIEWS & WWWROOT
    # =========================================================================
    Write-SuiteHeader "SUITE 7: Kiem tra tinh ma nguon — 0 Web Audio API (Reading Studio & Mobile im lang 100%)"
    $scanDirs = @(
        (Join-Path $Script:RepoRoot "src\BookShare.Web\Views"),
        (Join-Path $Script:RepoRoot "src\BookShare.Web\wwwroot\js"),
        (Join-Path $Script:RepoRoot "src\BookShare.Web\wwwroot\modules")
    )
    $audioPattern = 'AudioContext|webkitAudioContext|new\s+Audio\s*\(|<audio\b|createOscillator'
    $audioHits = New-Object System.Collections.Generic.List[string]
    foreach ($dir in $scanDirs) {
        if (Test-Path $dir) {
            Get-ChildItem -Path $dir -Recurse -File -Include *.cshtml,*.js,*.css | ForEach-Object {
                $matches = Select-String -Path $_.FullName -Pattern $audioPattern -AllMatches
                foreach ($m in $matches) {
                    $audioHits.Add("$($_.Name):$($m.LineNumber): $($m.Line.Trim())")
                }
            }
        }
    }
    Assert-Check ($audioHits.Count -eq 0) "Quet Views & wwwroot: 0 lan xuat hien Web Audio API / <audio>" ($audioHits -join " | ")
}
finally {
    if ($null -ne $Script:ServerProc -and -not $Script:ServerProc.HasExited) {
        Write-Host "`n[AUTOSTART] Dang dung tien trinh BookShare.Web (PID=$($Script:ServerProc.Id))..." -ForegroundColor Yellow
        try { $Script:ServerProc.Kill() } catch { }
        try { $Script:ServerProc.WaitForExit(5000) | Out-Null } catch { }
    }
}

Write-Host ""
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host ("  KET QUA KIEM THU E2E BOOKSHARE: {0} PASS / {1} FAIL" -f $Script:PassCount, $Script:FailCount) -ForegroundColor $(if ($Script:FailCount -eq 0) { "Green" } else { "Red" })
Write-Host "========================================================================" -ForegroundColor Cyan

if ($Script:FailCount -gt 0) {
    Write-Host "Danh sach loi:" -ForegroundColor Red
    foreach ($f in $Script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
