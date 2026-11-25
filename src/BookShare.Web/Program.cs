// =============================================================================
// [CORE-BOOTSTRAP] ĐIỂM KHỞI ĐỘNG HỆ THỐNG BOOKSHARE WEB PORTAL (Program.cs)
// Kiến trúc: ASP.NET Core MVC + Strict Service Layer (22 phân hệ [S01]..[S22] + [CORE-*])
// Thành phần chính:
//   1. Nạp biến môi trường (.env) & đăng ký EF Core BookShareDbContext (SQL Server)
//   2. Xác thực Cookie (có thu hồi phiên tức thì khi tài khoản bị khóa) & Google OAuth 2.0
//   3. Phân quyền 4 vai trò (Admin, Librarian, Curator, User) + Quyền Trưởng/Phó CLB (S20)
//   4. Đăng ký toàn bộ Dịch vụ Nghiệp vụ [S01]..[S22] & [CORE-*] vào DI Container
//   5. Bộ lọc toàn cục ValidateNonNullBodyFilter (chặn [FromBody] null -> HTTP 400)
//   6. Middleware bắt ngoại lệ tập trung (429 Anti-Spam, 403 Anti-IDOR, 400 JSON/DB, 409 Concurrency)
//   7. Khởi chạy Hangfire Server & đăng ký 3 tác vụ nền định kỳ (BookShareDailyJobs)
// Tài liệu ánh xạ mã dịch vụ đầy đủ: docs/SERVICE_CODE_MAP.md
// =============================================================================
using System.Security.Claims;
using System.Text.Json;
using BookShare.Web.BackgroundJobs;
using BookShare.Web.Data;
using BookShare.Web.Filters;
using BookShare.Web.Services.Implementations;
using BookShare.Web.Services.Interfaces;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// 1. Tự động nạp biến môi trường từ file .env ở thư mục gốc Solution
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// 2. Đăng ký BookShareDbContext (Sinh tự động từ Database-First Scaffold)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=BookShareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";

builder.Services.AddDbContext<BookShareDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2.5. Lưu bền vững khóa DataProtection (tránh lỗi CorrelationCookie Google OAuth khi container khởi động lại)
var dpKeysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dpkeys");
try
{
    Directory.CreateDirectory(dpKeysDir);
    builder.Services.AddDataProtection()
        .SetApplicationName("BookShare.Portal")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir));
}
catch
{
    builder.Services.AddDataProtection().SetApplicationName("BookShare.Portal");
}

// 3. Cấu hình xác thực người dùng & Google OAuth 2.0
var (googleClientId, googleClientSecret) = AuthConfigHelper.GetGoogleOAuthCredentials(builder.Configuration);
var isGoogleOAuthConfigured = AuthConfigHelper.IsGoogleOAuthConfigured(builder.Configuration);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(AuthConfigHelper.ExternalCookieScheme, options =>
{
    options.Cookie.Name = "BookShare.ExternalCookie";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
})
.AddCookie(options =>
{
    options.Cookie.Name = "BookShare.AuthCookie";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/S01AuthOtpGoogle/Index";
    options.AccessDeniedPath = "/Home/StatusCode/403";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            try
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<BookShareDbContext>();
                var userState = await db.AspNetUsers
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Id, u.LockoutEnd })
                    .FirstOrDefaultAsync();
                if (userState == null || (userState.LockoutEnd.HasValue && userState.LockoutEnd.Value > DateTimeOffset.UtcNow))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
            catch
            {
                // Không làm gián đoạn luồng nếu CSDL đang khởi tạo lại
            }
        },
        OnRedirectToAccessDenied = async context =>
        {
            if (IsApiOrAjaxRequest(context.HttpContext))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                var payload = JsonSerializer.Serialize(new
                {
                    success = false,
                    statusCode = StatusCodes.Status403Forbidden,
                    title = "Không đủ quyền truy cập",
                    message = "Tài khoản hiện tại của bạn không có quyền thực hiện thao tác này.",
                    requestId = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                });
                await context.Response.WriteAsync(payload);
                return;
            }

            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Response.Redirect($"/Home/StatusCode?code=403&returnUrl={Uri.EscapeDataString(returnUrl)}");
        },
        OnRedirectToLogin = async context =>
        {
            if (IsApiOrAjaxRequest(context.HttpContext))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                var payload = JsonSerializer.Serialize(new
                {
                    success = false,
                    statusCode = StatusCodes.Status401Unauthorized,
                    title = "Yêu cầu đăng nhập",
                    message = "Vui lòng đăng nhập tài khoản để thực hiện thao tác này.",
                    requestId = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                });
                await context.Response.WriteAsync(payload);
                return;
            }
            context.Response.Redirect(context.RedirectUri);
        }
    };
});

if (isGoogleOAuthConfigured)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = AuthConfigHelper.ExternalCookieScheme;
        options.CallbackPath = "/signin-google";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRemoteFailure = context =>
        {
            var errText = "Không thể hoàn tất đăng nhập Google (vui lòng kiểm tra cấu hình Redirect URI hoặc thử lại).";
            context.Response.Redirect($"/S01AuthOtpGoogle/Index?oauthError={Uri.EscapeDataString(errText)}");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

// 4. Phân quyền 4 Vai trò (Admin, Librarian, Curator, User)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireLibrarian", policy => policy.RequireRole("Librarian", "Admin"));
    options.AddPolicy("RequireCurator", policy => policy.RequireRole("Curator", "Admin"));
    options.AddPolicy("RequireUser", policy => policy.RequireRole("User", "Curator", "Librarian", "Admin"));
});

// 5. Đăng ký HttpClient, MemoryCache & Tầng Business Services (Controller không gọi thẳng DbContext)
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IAntiSpamRateLimiterService, AntiSpamRateLimiterService>();
builder.Services.AddScoped<IDatabaseBootstrapService, DatabaseBootstrapService>();
builder.Services.AddScoped<ISystemIconService, SystemIconService>();
// [BOT-GUARD] Đăng ký dịch vụ xác thực Google reCAPTCHA v3 (đọc ReCaptcha:SiteKey & SecretKey từ .env)
builder.Services.AddScoped<IRecaptchaService, RecaptchaService>();
builder.Services.AddScoped<IResendEmailService, ResendEmailService>();
builder.Services.AddScoped<ILoanFlowCoordinatorService, LoanFlowCoordinatorService>();
builder.Services.AddScoped<IS01AuthOtpService, S01AuthOtpService>();
builder.Services.AddScoped<IS02PersonalShelfService, S02PersonalShelfService>();
builder.Services.AddScoped<IS03ReadingTrackerService, S03ReadingTrackerService>();
builder.Services.AddScoped<IS04BookNotesService, S04BookNotesService>();
builder.Services.AddScoped<IS05IsbnCatalogService, S05IsbnCatalogService>();
builder.Services.AddScoped<IS06SearchDiscoveryService, S06SearchDiscoveryService>();
builder.Services.AddScoped<IS07LibraryCatalogService, S07LibraryCatalogService>();
builder.Services.AddScoped<IS08LibraryLoanService, S08LibraryLoanService>();
builder.Services.AddScoped<IS09P2PLoanService, S09P2PLoanService>();
builder.Services.AddScoped<IS10ConditionReportService, S10ConditionReportService>();
builder.Services.AddScoped<IS11HotBookQueueService, S11HotBookQueueService>();
builder.Services.AddScoped<IS12LoanRenewalFineService, S12LoanRenewalFineService>();
builder.Services.AddScoped<IS13GeminiAiService, S13GeminiAiService>();
builder.Services.AddScoped<IS14AiRecommendationService, S14AiRecommendationService>();
builder.Services.AddScoped<IS15TrustScoreReviewService, S15TrustScoreReviewService>();
builder.Services.AddScoped<IS16MembershipTierService, S16MembershipTierService>();
builder.Services.AddScoped<IS17SandboxPaymentService, S17SandboxPaymentService>();
builder.Services.AddScoped<IS18NotificationHubService, S18NotificationHubService>();
builder.Services.AddScoped<IS19DisputeResolutionService, S19DisputeResolutionService>();
builder.Services.AddScoped<IS20ExchangeHubClubService, S20ExchangeHubClubService>();
builder.Services.AddScoped<IS21LibrarianDeskService, S21LibrarianDeskService>();
builder.Services.AddScoped<IS22AdminAnalyticsService, S22AdminAnalyticsService>();
builder.Services.AddScoped<BookShareDailyJobs>();
builder.Services.AddScoped<IPortalDiscoveryService, PortalDiscoveryService>();
builder.Services.AddScoped<IBookHandoverQrService, BookHandoverQrService>();
builder.Services.AddScoped<IQrAuthService, QrAuthService>();
builder.Services.AddScoped<IMobileCompanionService, MobileCompanionService>();

// 6. Cấu hình Hangfire Background Jobs
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString));
builder.Services.AddHangfireServer();

// Đăng ký MVC Controllers/Views kèm bộ lọc toàn cục chặn [FromBody] null -> trả HTTP 400 chuẩn
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ValidateNonNullBodyFilter>();
});

// 7. Cấu hình ForwardedHeaders cho Nginx Reverse Proxy (Nhận diện đúng https:// cho Google OAuth /signin-google)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Đồng bộ schema mở rộng, 4 vai trò gốc, tài khoản Quản trị viên (Admin@123) và 18 biểu tượng mặc định ngay khi khởi động
using (var scope = app.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<IDatabaseBootstrapService>();
    var bootLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupBootstrap");
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            await bootstrap.EnsureInitializedAsync();
            break;
        }
        catch (Exception ex)
        {
            bootLogger.LogWarning(ex, "Database bootstrap attempt {Attempt}/3 failed.", attempt);
            if (attempt < 3)
            {
                await Task.Delay(1000);
            }
        }
    }
}

app.UseForwardedHeaders();

app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseHttpsRedirection();
// Phục vụ tệp tĩnh phát sinh lúc chạy (/uploads/icons, /uploads/conditions, ...) mà MapStaticAssets không lập chỉ mục lúc build
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// [CORE-SECURITY] Chống lưu cache trình duyệt (bfcache / disk cache) cho trang đã đăng nhập & lịch sử chat AI
// Đảm bảo sau khi đăng xuất, bấm nút Back hoặc mở lại tab tuyệt đối không lộ lịch sử chat hay dữ liệu tài khoản cũ.
app.Use(async (context, next) =>
{
    bool isPrivatePath = context.User.Identity?.IsAuthenticated == true
        || context.Request.Path.StartsWithSegments("/api/ai-chat", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/S13GeminiAiAssistant", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/S01AuthOtpGoogle/Logout", StringComparison.OrdinalIgnoreCase);

    if (isPrivatePath)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            return Task.CompletedTask;
        });
    }

    await next();
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (RateLimitExceededException ex)
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = ex.RetryAfterSeconds.ToString();
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(new
        {
            success = false,
            statusCode = StatusCodes.Status429TooManyRequests,
            title = "Thao tác quá nhanh (Chống Spam)",
            message = ex.Message,
            retryAfterSeconds = ex.RetryAfterSeconds,
            requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
        });
        await context.Response.WriteAsync(payload);
        return;
    }
    catch (UnauthorizedAccessException ex)
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        if (IsApiOrAjaxRequest(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                statusCode = StatusCodes.Status403Forbidden,
                title = "Từ chối truy cập",
                message = ex.Message,
                requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            });
            await context.Response.WriteAsync(payload);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
    catch (Exception ex) when (ex is BadHttpRequestException or JsonException or ArgumentException or FormatException)
    {
        // [CORE-EXCEPTION] Bắt lỗi định dạng JSON / tham số đầu vào sai -> trả HTTP 400 thân thiện, không nổ HTTP 500
        if (context.Response.HasStarted)
        {
            throw;
        }

        if (IsApiOrAjaxRequest(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                statusCode = StatusCodes.Status400BadRequest,
                title = "Dữ liệu gửi lên chưa hợp lệ",
                message = "Thông tin gửi lên không đúng định dạng hoặc vượt quá giới hạn cho phép. Vui lòng kiểm tra và thử lại.",
                requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            });
            await context.Response.WriteAsync(payload);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
    catch (DbUpdateConcurrencyException)
    {
        // [CORE-EXCEPTION] Xung đột cập nhật đồng thời (Optimistic Concurrency) -> trả HTTP 409 Conflict
        if (context.Response.HasStarted)
        {
            throw;
        }

        if (IsApiOrAjaxRequest(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                statusCode = StatusCodes.Status409Conflict,
                title = "Xung đột cập nhật dữ liệu",
                message = "Bản ghi này vừa được thay đổi bởi một thao tác khác. Vui lòng tải lại trang và thực hiện lại.",
                requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            });
            await context.Response.WriteAsync(payload);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
    }
    catch (DbUpdateException)
    {
        // [CORE-EXCEPTION] Bắt lỗi ràng buộc toàn vẹn CSDL -> tuyệt đối không lộ câu lệnh SQL ra phía người dùng
        if (context.Response.HasStarted)
        {
            throw;
        }

        if (IsApiOrAjaxRequest(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                statusCode = StatusCodes.Status400BadRequest,
                title = "Không thể lưu dữ liệu",
                message = "Dữ liệu gửi lên vi phạm ràng buộc hệ thống hoặc bị trùng lặp. Vui lòng kiểm tra lại và thử lại.",
                requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
            });
            await context.Response.WriteAsync(payload);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapStaticAssets();

// Cấu hình định tuyến mặc định
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Đăng ký 3 Background Jobs định kỳ với Hangfire sau khi Host khởi động (Không chặn luồng Kestrel)
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(() =>
    {
        try
        {
            RecurringJob.AddOrUpdate<BookShareDailyJobs>(
                "bookshare-due-date-reminder",
                job => job.SendDueDateRemindersJobAsync(),
                "0 7 * * *");

            RecurringJob.AddOrUpdate<BookShareDailyJobs>(
                "bookshare-overdue-fine-calculator",
                job => job.CalculateOverdueFinesJobAsync(),
                "5 0 * * *");

            RecurringJob.AddOrUpdate<BookShareDailyJobs>(
                "bookshare-expired-request-cleanup",
                job => job.CancelExpiredRequestsAndReservationsJobAsync(),
                "*/30 * * * *");
        }
        catch
        {
            // Bỏ qua nếu Hangfire Storage đang khởi tạo
        }
    });
});

app.Run();

static bool IsApiOrAjaxRequest(HttpContext context)
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var accept = context.Request.Headers.Accept.ToString();
    if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (string.Equals(context.Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return !HttpMethods.IsGet(context.Request.Method);
}
