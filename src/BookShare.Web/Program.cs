using System.Security.Claims;
using System.Text.Json;
using BookShare.Web.BackgroundJobs;
using BookShare.Web.Data;
using BookShare.Web.Services.Implementations;
using BookShare.Web.Services.Interfaces;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

// 3. Cấu hình xác thực người dùng & Google OAuth 2.0
var (googleClientId, googleClientSecret) = AuthConfigHelper.GetGoogleOAuthCredentials(builder.Configuration);
var isGoogleOAuthConfigured = AuthConfigHelper.IsGoogleOAuthConfigured(builder.Configuration);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
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
                bool exists = await db.AspNetUsers.AsNoTracking().AnyAsync(u => u.Id == userId);
                if (!exists)
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
        options.CallbackPath = "/signin-google";
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

builder.Services.AddControllersWithViews();

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
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    try
    {
        await next();
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
