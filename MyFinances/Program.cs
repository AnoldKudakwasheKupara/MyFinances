using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MyFinances.Data;
using MyFinances.Security;
using MyFinances.Services;
using Npgsql;
using System.Globalization;
using System.IO.Compression;

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-GB");

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? PostgresUrl.ToConnectionString(
        builder.Configuration["DATABASE_URL"]);

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No database was configured. Set a PostgreSQL connection " +
        "string in user secrets (dotnet user-secrets set " +
        "\"ConnectionStrings:DefaultConnection\" \"Host=...;" +
        "Database=...;Username=...;Password=...\"), in the " +
        "ConnectionStrings__DefaultConnection environment variable, " +
        "or as a postgres:// address in DATABASE_URL.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.EnableRetryOnFailure()
    ));

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.Section));

var emailEnabled = builder.Configuration.GetValue<bool>("Email:Enabled");

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;

        options.User.RequireUniqueEmail = true;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // Confirming an address needs an email to reach it. Until mail
        // is configured, new accounts can sign in straight away.
        options.SignIn.RequireConfirmedAccount = emailEnabled;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultUI()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

/// <summary>
/// Cookies that carry a sign-in or a form token are only ever sent
/// over HTTPS. Running locally under Development they follow the
/// request, so the plain http profile still works.
/// </summary>
var cookieSecurity = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

/// <summary>
/// Render ends HTTPS at its own proxy and passes the request on over
/// plain http, saying in X-Forwarded-Proto and X-Forwarded-For how it
/// arrived and from where. Only the last hop is taken, which is the
/// one Render adds, so a visitor cannot pass off an address of their
/// own choosing.
/// </summary>
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAntiforgery(options =>
    options.Cookie.SecurePolicy = cookieSecurity);

builder.Services.Configure<CookieTempDataProviderOptions>(options =>
    options.Cookie.SecurePolicy = cookieSecurity);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    options.Cookie.Name = "MyFinances.Auth";
    options.Cookie.SecurePolicy = cookieSecurity;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

    options.ExpireTimeSpan = SessionTimeout.CookieLifetime;
    options.SlidingExpiration = true;

    var validate = options.Events.OnValidatePrincipal;

    options.Events.OnValidatePrincipal = async context =>
    {
        await validate(context);

        if (context.Principal != null)
            context.ShouldRenew = true;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
        builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("MyFinances");

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<UserSetupService>();
builder.Services.AddScoped<UserPreferences>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<BalanceService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddSingleton<ReceiptStorage>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<TransactionSearch>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<GoalService>();
builder.Services.AddScoped<DebtService>();
builder.Services.AddScoped<BillService>();
builder.Services.AddScoped<NetWorthService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<MyFinances.Services.Reporting.Analytics>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<MyFinances.Services.Reporting.ReportService>();
builder.Services.AddScoped<DataExportService>();
builder.Services.AddHostedService<ReminderEmailWorker>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, IdentityEmailSender>();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;

    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();

    options.MimeTypes = new[]
    {
        "text/css",
        "text/javascript",
        "application/javascript",
        "image/svg+xml",
        "font/woff",
        "font/woff2"
    };
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddRateLimiter(SignInLimits.Configure);

builder.Services.AddControllersWithViews();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToAreaFolder(
        "Identity",
        "/Account");

    options.Conventions.AllowAnonymousToAreaPage(
        "Identity",
        "/Error");

    options.Conventions.AuthorizeAreaFolder(
        "Identity",
        "/Account/Manage");
});

var app = builder.Build();

MyFinances.Services.Reporting.DocumentFonts.Register(app.Environment, app.Logger);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        await db.Database.MigrateAsync();
    }
    catch (NpgsqlException ex)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);

        app.Logger.LogError(
            ex,
            "Could not prepare the database '{Database}' on '{Host}'. " +
            "Check that PostgreSQL is running and that the login in the " +
            "connection string can reach it and create databases. See " +
            "README.md for first-run setup.",
            target.Database,
            target.Host);

        throw;
    }
}

app.UseForwardedHeaders();

// Amounts and dates read the same whatever the server's own locale is:
// a decimal point, never a decimal comma.
var culture = new CultureInfo("en-GB");

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture],
    RequestCultureProviders = []
});

if (!app.Environment.IsDevelopment())
{
    // Unhandled errors are logged by the handler with the request
    // they came from, and the person sees a calm page instead.
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.Use(SecurityHeaders.AddAsync);

app.UseHttpsRedirection();

app.UseResponseCompression();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.GetTypedHeaders().CacheControl =
            new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(7)
            };
    }
});

app.Use(SignInLimits.ReadEmailAsync);

app.UseRateLimiter();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

public partial class Program;
