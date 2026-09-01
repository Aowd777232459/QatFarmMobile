using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using QatFarm.Web.Components;
using QatFarm.Web.Data;
using QatFarm.Web.Infrastructure;
using QatFarm.Web.Models;
using QatFarm.Web.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// يسمح للمثبت أو مدير النظام بوضع إعدادات الجهاز في ProgramData دون تعديل ملفات التطبيق.
builder.Configuration.AddJsonFile(
    new PhysicalFileProvider(RuntimePaths.DataDirectory),
    "appsettings.Local.json",
    optional: true,
    reloadOnChange: true);

var desktopMode = builder.Configuration.GetValue("DesktopMode:Enabled", false);
var desktopUrl = builder.Configuration["DesktopMode:Url"] ?? "http://127.0.0.1:5275";
var autoOpenBrowser = builder.Configuration.GetValue("DesktopMode:AutoOpenBrowser", desktopMode);
var localSyncEnabled = builder.Configuration.GetValue("LocalSync:Enabled", desktopMode);
var localSyncUrl = builder.Configuration["LocalSync:Url"] ?? "http://0.0.0.0:5276";

if (desktopMode && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls(localSyncEnabled ? [desktopUrl, localSyncUrl] : [desktopUrl]);

Mutex? singleInstanceMutex = null;
var ownsSingleInstanceMutex = false;
if (desktopMode && OperatingSystem.IsWindows())
{
    singleInstanceMutex = new Mutex(true, @"Local\QatFarmSystem.SingleInstance", out ownsSingleInstanceMutex);
    if (!ownsSingleInstanceMutex)
    {
        BrowserLauncher.Open(desktopUrl);
        singleInstanceMutex.Dispose();
        return;
    }
}

builder.Logging.AddProvider(new RollingFileLoggerProvider(RuntimePaths.LogsDirectory));

QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.UseEnvironmentFonts = true;

// ضمان اكتشاف خطوط Windows العربية عند التشغيل المحلي أو بعد النشر.
var windowsDirectory = Environment.GetEnvironmentVariable("WINDIR");
if (!string.IsNullOrWhiteSpace(windowsDirectory))
{
    var windowsFontsPath = Path.Combine(windowsDirectory, "Fonts");
    if (Directory.Exists(windowsFontsPath) &&
        !QuestPDF.Settings.FontDiscoveryPaths.Any(x =>
            string.Equals(x, windowsFontsPath, StringComparison.OrdinalIgnoreCase)))
    {
        QuestPDF.Settings.FontDiscoveryPaths.Add(windowsFontsPath);
    }
}

QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = builder.Environment.IsDevelopment();

var connectionString = Environment.GetEnvironmentVariable("QATFARM_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("لم يتم العثور على سلسلة الاتصال DefaultConnection.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(8), null);
        sql.CommandTimeout(60);
    }));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.Name = "QatFarm.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = desktopMode
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<StartupState>();

builder.Services.AddScoped<FarmService>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<CultivationExpenseService>();
builder.Services.AddScoped<CultivationDebtPdfService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<ZakatService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PdfReportService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<DatabaseBackupService>();
builder.Services.AddScoped<UserManagementService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<LocalSyncService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    if (!desktopMode)
        app.UseHsts();
}

if (!desktopMode)
    app.UseHttpsRedirection();

app.UseStaticFiles();

// في حال تعذر الاتصال بقاعدة البيانات يبقى التطبيق مفتوحًا ويعرض شاشة تشخيص واضحة
// بدل الإغلاق المفاجئ أو ظهور استثناء غير مفهوم للمستخدم.
app.Use(async (context, next) =>
{
    var startupState = context.RequestServices.GetRequiredService<StartupState>();
    var path = context.Request.Path;
    var allowedDuringFailure =
        path.StartsWithSegments("/startup-error") ||
        path.StartsWithSegments("/startup/retry") ||
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/api/local-sync") ||
        path.StartsWithSegments("/css") ||
        path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/_framework") ||
        path.StartsWithSegments("/favicon.svg");

    if (!startupState.IsReady && !allowedDuringFailure)
    {
        context.Response.Redirect("/startup-error");
        return;
    }

    await next();
});

app.UseRouting();
app.UseAuthentication();

// يمنع تجاوز تغيير كلمة المرور المؤقتة ويوقف الجلسة إذا عُطّل الحساب.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isAccountPath = path.StartsWithSegments("/account");
    var isFrameworkPath = path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_framework");
    var isHealthPath = path.StartsWithSegments("/health");

    if (context.User.Identity?.IsAuthenticated == true &&
        !isAccountPath && !isFrameworkPath && !isHealthPath)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            context.Response.Redirect("/account/login");
            return;
        }

        if (user.MustChangePassword)
        {
            context.Response.Redirect("/account/change-password");
            return;
        }
    }

    await next();
});

app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();

app.MapGet("/health", async (IDbContextFactory<ApplicationDbContext> factory, StartupState startupState) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync();
        var connected = await db.Database.CanConnectAsync();

        if (connected && startupState.IsReady)
        {
            return (IResult)Results.Ok(new
            {
                status = "Healthy",
                database = "Connected",
                version = typeof(ApplicationUser).Assembly.GetName().Version?.ToString(),
                time = DateTimeOffset.Now
            });
        }

        return (IResult)Results.Problem(
            startupState.Error?.Message ?? "تعذر الاتصال بقاعدة البيانات.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return (IResult)Results.Problem(
            ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapGet("/startup-error", (StartupState startupState, IConfiguration configuration) =>
{
    var error = startupState.Error;
    var connection = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    var (server, database) = GetSafeDatabaseTarget(connection);
    var message = System.Net.WebUtility.HtmlEncode(error?.Message ?? "لم تكتمل تهيئة النظام بعد.");
    var details = System.Net.WebUtility.HtmlEncode(error?.ToString() ?? string.Empty);
    var logPath = System.Net.WebUtility.HtmlEncode(RuntimePaths.StartupErrorFile);

    var html = $$"""
    <!doctype html>
    <html lang="ar" dir="rtl">
    <head>
      <meta charset="utf-8" />
      <meta name="viewport" content="width=device-width,initial-scale=1" />
      <title>إصلاح تشغيل نظام المزارع</title>
      <style>
        body{font-family:Tahoma,Segoe UI,sans-serif;background:#071d14;color:#eef8f2;margin:0;padding:28px}
        .card{max-width:920px;margin:auto;background:#103425;border:1px solid #2c6b50;border-radius:22px;padding:28px;box-shadow:0 24px 80px #0007}
        h1{margin-top:0;color:#8fe3b7}.error{background:#4a1717;border:1px solid #a94848;padding:16px;border-radius:14px;white-space:pre-wrap}
        .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;margin:18px 0}.item{background:#0a281c;padding:14px;border-radius:12px}
        code{direction:ltr;display:inline-block;color:#d9ffe9}.btn{display:inline-block;background:#28a76b;color:white;text-decoration:none;padding:12px 20px;border-radius:10px;font-weight:bold}
        details{margin-top:18px;background:#061a12;padding:12px;border-radius:12px}pre{white-space:pre-wrap;direction:ltr;text-align:left;font-size:12px;overflow:auto}
      </style>
    </head>
    <body><main class="card">
      <h1>تعذر تجهيز قاعدة بيانات النظام</h1>
      <p>التطبيق يعمل، لكن SQL Server أو إعداد قاعدة البيانات يحتاج إلى تصحيح. لم يتم حذف أي بيانات.</p>
      <div class="error">{{message}}</div>
      <div class="grid">
        <div class="item"><b>خادم SQL</b><br><code>{{System.Net.WebUtility.HtmlEncode(server)}}</code></div>
        <div class="item"><b>قاعدة البيانات</b><br><code>{{System.Net.WebUtility.HtmlEncode(database)}}</code></div>
        <div class="item"><b>سجل الخطأ</b><br><code>{{logPath}}</code></div>
      </div>
      <p>تأكد أن خدمة <code>SQL Server (SQLEXPRESS)</code> تعمل، ثم اضغط إعادة المحاولة.</p>
      <a class="btn" href="/startup/retry">إعادة فحص وإصلاح القاعدة</a>
      <details><summary>التفاصيل التقنية</summary><pre>{{details}}</pre></details>
    </main></body></html>
    """;

    return Results.Content(html, "text/html; charset=utf-8", System.Text.Encoding.UTF8);
}).AllowAnonymous();

app.MapGet("/startup/retry", async (IServiceProvider services, IConfiguration configuration, StartupState startupState) =>
{
    try
    {
        await DbInitializer.InitializeAsync(services, configuration);
        startupState.MarkReady();
        return Results.Redirect("/");
    }
    catch (Exception ex)
    {
        startupState.MarkFailed(ex);
        StartupDiagnostics.WriteFatal(ex);
        return Results.Redirect("/startup-error");
    }
}).AllowAnonymous();

app.MapGet("/system/data-path", () => Results.Ok(new
{
    data = RuntimePaths.DataDirectory,
    logs = RuntimePaths.LogsDirectory,
    backups = RuntimePaths.BackupsDirectory
})).RequireAuthorization(policy => policy.RequireRole("Administrator"));

app.MapGet("/api/local-sync/info", async (HttpContext context, LocalSyncService sync) =>
{
    var key = context.Request.Headers["X-AWAD-SYNC-KEY"].FirstOrDefault();
    if (!await sync.IsPairingKeyValidAsync(key)) return Results.Unauthorized();
    return Results.Ok(new
    {
        application = "AWAD SOFT QatFarm",
        status = "Ready",
        time = DateTimeOffset.UtcNow
    });
}).AllowAnonymous();

app.MapPost("/api/local-sync/sync", async (HttpContext context, LocalSyncBatch batch, LocalSyncService sync) =>
{
    var key = context.Request.Headers["X-AWAD-SYNC-KEY"].FirstOrDefault();
    if (!await sync.IsPairingKeyValidAsync(key)) return Results.Unauthorized();
    try
    {
        return Results.Ok(await sync.SynchronizeAsync(batch));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "فشلت مزامنة Wi-Fi المحلية للجهاز {DeviceId}", batch.DeviceId);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/reports/invoice/{id:long}.pdf", async (HttpContext context, long id, PdfReportService pdf) =>
{
    DisablePdfCaching(context);
    var bytes = await pdf.CreateInvoicePdfAsync(id);
    if (bytes is null)
        return (IResult)Results.NotFound();

    var fileName = SafePdfFileName(await pdf.GetInvoiceFileNameAsync(id));
    return (IResult)Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization();

app.MapGet("/reports/cultivation.pdf", async (HttpContext context, long? farmId, int? year, CultivationDebtPdfService pdf) =>
{
    DisablePdfCaching(context);
    var selectedYear = NormalizeReportYear(year);
    var farmName = await pdf.GetFarmFileLabelAsync(farmId);
    var bytes = await pdf.CreateAnnualPdfAsync(farmId, selectedYear);
    return Results.File(bytes, "application/pdf", SafePdfFileName($"خسائر-التربية-والديون-{farmName}-{selectedYear}.pdf"));
}).RequireAuthorization();

app.MapGet("/reports/sales.pdf", async (HttpContext context, long? farmId, int? year, PdfReportService pdf) =>
{
    DisablePdfCaching(context);
    var selectedYear = NormalizeReportYear(year);
    var farmName = await pdf.GetFarmFileLabelAsync(farmId);
    var bytes = await pdf.CreateSalesReportPdfAsync(farmId, selectedYear);
    return Results.File(bytes, "application/pdf", SafePdfFileName($"فواتير-تفصيلية-{farmName}-{selectedYear}.pdf"));
}).RequireAuthorization();

app.MapGet("/reports/profit.pdf", async (HttpContext context, long? farmId, int? year, PdfReportService pdf) =>
{
    DisablePdfCaching(context);
    var selectedYear = NormalizeReportYear(year);
    var farmName = await pdf.GetFarmFileLabelAsync(farmId);
    var bytes = await pdf.CreateFarmProfitReportPdfAsync(farmId, selectedYear);
    return Results.File(bytes, "application/pdf", SafePdfFileName($"الربح-السنوي-{farmName}-{selectedYear}.pdf"));
}).RequireAuthorization();

app.MapGet("/reports/customer/{id:long}.pdf", async (HttpContext context, long id, PdfReportService pdf) =>
{
    DisablePdfCaching(context);
    var bytes = await pdf.CreateCustomerStatementPdfAsync(id);
    return bytes is null
        ? (IResult)Results.NotFound()
        : (IResult)Results.File(bytes, "application/pdf", SafePdfFileName($"كشف-حساب-عميل-{id}.pdf"));
}).RequireAuthorization();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var startupState = app.Services.GetRequiredService<StartupState>();
try
{
    await DbInitializer.InitializeAsync(app.Services, app.Configuration);
    startupState.MarkReady();
}
catch (Exception ex)
{
    startupState.MarkFailed(ex);
    StartupDiagnostics.WriteFatal(ex);
    app.Logger.LogCritical(ex, "فشل تهيئة قاعدة البيانات. سيبقى التطبيق مفتوحًا في وضع التشخيص. راجع {StartupErrorFile}", RuntimePaths.StartupErrorFile);
}

if (desktopMode && autoOpenBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() => BrowserLauncher.Open(desktopUrl));
}

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    StartupDiagnostics.WriteFatal(ex);
    throw;
}
finally
{
    if (ownsSingleInstanceMutex && singleInstanceMutex is not null)
    {
        try { singleInstanceMutex.ReleaseMutex(); } catch { }
    }
    singleInstanceMutex?.Dispose();
}

static (string Server, string Database) GetSafeDatabaseTarget(string connectionString)
{
    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return (
            string.IsNullOrWhiteSpace(builder.DataSource) ? "غير محدد" : builder.DataSource,
            string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "غير محددة" : builder.InitialCatalog);
    }
    catch
    {
        return ("إعداد غير صالح", "إعداد غير صالح");
    }
}

static int NormalizeReportYear(int? year)
{
    var selectedYear = year ?? DateTime.Today.Year;
    return selectedYear is >= 2000 and <= 2100
        ? selectedYear
        : DateTime.Today.Year;
}

static string SafePdfFileName(string fileName)
{
    var invalid = Path.GetInvalidFileNameChars();
    var safe = new string(fileName
        .Select(ch => invalid.Contains(ch) ? '-' : ch)
        .ToArray());

    return string.IsNullOrWhiteSpace(safe) ? "report.pdf" : safe;
}

static void DisablePdfCaching(HttpContext context)
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
}
