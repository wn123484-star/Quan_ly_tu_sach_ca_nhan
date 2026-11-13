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

app.Run();
