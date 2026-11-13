using System.Security.Claims;
using System.Text.Json;
using BookShare.Web.BackgroundJobs;
using BookShare.Web.Data;
using BookShare.Web.Services.Implementations;
using BookShare.Web.Services.Interfaces;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

// 1. Tự động nạp biến môi trường từ file .env ở thư mục gốc Solution
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// 2. Đăng ký BookShareDbContext (Sinh tự động từ Database-First Scaffold)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=BookShareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";

builder.Services.AddDbContext<BookShareDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Cấu hình Cookie Authentication + Google OAuth 2.0 chuẩn .NET 10
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

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
    options.AccessDeniedPath = "/S01AuthOtpGoogle/Index";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToAccessDenied = async context =>
        {
            var accept = context.Request.Headers.Accept.ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                !HttpMethods.IsGet(context.Request.Method))
            {
                var currentRole = context.HttpContext.User.FindFirstValue(ClaimTypes.Role) ?? "User";
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                var payload = JsonSerializer.Serialize(new
                {
                    message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{currentRole}] không đủ quyền truy cập chức năng này (Đã khóa bởi [Authorize(Roles)] ở Backend)! Vui lòng đăng nhập tài khoản Curator / Librarian / Admin tại dịch vụ S01."
                });
                await context.Response.WriteAsync(payload);
                return;
            }
            context.Response.Redirect(context.RedirectUri);
        },
        OnRedirectToLogin = async context =>
        {
            var accept = context.Request.Headers.Accept.ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                !HttpMethods.IsGet(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                var payload = JsonSerializer.Serialize(new
                {
                    message = "MÃ 403 FORBIDDEN: Chức năng này yêu cầu xác thực phân quyền [Authorize(Roles)]! Vui lòng đăng nhập tài khoản Curator / Librarian / Admin tại dịch vụ S01."
                });
                await context.Response.WriteAsync(payload);
                return;
            }
            context.Response.Redirect(context.RedirectUri);
        }
    };
});

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
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

// 5. Đăng ký HttpClient & Tầng Business Services (Controller không gọi thẳng DbContext)
builder.Services.AddHttpClient();
builder.Services.AddScoped<IResendEmailService, ResendEmailService>();
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

// 6. Cấu hình Hangfire Background Jobs
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString));
builder.Services.AddHangfireServer();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Cấu hình định tuyến cho 3 Areas (Admin, Librarian, Curator)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

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
