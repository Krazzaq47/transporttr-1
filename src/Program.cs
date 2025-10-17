using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MT.Data;
using System.Globalization;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

var builder = WebApplication.CreateBuilder(args);

// ====== Data ======
var cs = builder.Configuration.GetConnectionString("DefaultConnection")
         ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(cs));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ====== Identity ======
builder.Services
    .AddDefaultIdentity<IdentityUser>(o =>
    {
        o.SignIn.RequireConfirmedAccount = false;   // easier in dev
        o.Password.RequireDigit = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredLength = 4;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// ====== Localization ======
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// ====== MVC / Razor ======
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// Sessions (for OTP flow)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Allow Identity UI anonymously (avoid redirect loops)
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.AllowAnonymousToAreaFolder("Identity", "/");
        options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
    });

// Global: require auth everywhere unless explicitly allowed
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});


// Initialize Firebase Admin using the JSON in the root folder
try
{
    var jsonPath = builder.Configuration["Firebase:ServiceAccountPath"];
    if (!Path.IsPathRooted(jsonPath))
        jsonPath = Path.Combine(builder.Environment.ContentRootPath, jsonPath);

    if (FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(jsonPath),
            ProjectId = "myaspnetotpapp" // 👈 from your JSON file
        });
        Console.WriteLine("✅ Firebase initialized successfully (root JSON).");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Firebase initialization failed: {ex.Message}");
}


var app = builder.Build();

// ====== Ensure database is up to date (auto-migrate) ======
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}
catch { }

// ====== Seed Roles & Admin User ======
try
{
    using var scope = app.Services.CreateScope();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    string[] roles = new[] { "Admin", "DocumentVerifier", "FinalApprover", "MinistryOfficer", "Owner", "VehicleOwner" };
    foreach (var r in roles)
    {
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new IdentityRole(r));
    }

    var adminEmail = builder.Configuration["Admin:Email"] ?? "admin@mt.local";
    var adminPass = builder.Configuration["Admin:Password"] ?? "Admin!2345"; // dev-only default

    var admin = await userMgr.FindByEmailAsync(adminEmail);
    if (admin == null)
    {
        admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        var res = await userMgr.CreateAsync(admin, adminPass);
        if (res.Succeeded)
            await userMgr.AddToRoleAsync(admin, "Admin");
    }
}
catch { }

// ====== Pipeline ======
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Request localization (en, ar)
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};
localizationOptions.RequestCultureProviders.Insert(0, new Microsoft.AspNetCore.Localization.QueryStringRequestCultureProvider());
localizationOptions.RequestCultureProviders.Insert(1, new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());
app.UseRequestLocalization(localizationOptions);

// Session BEFORE endpoints
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// ====== Endpoints ======

// 🔑 Auth-aware root: go to Home/Index if signed in, otherwise Site/Index
app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
        return Results.Redirect("/Home/Index");
    return Results.Redirect("/Site/Index");
}).AllowAnonymous();

// Conventional routes (default controller is Home now)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Identity UI (Razor Pages)
app.MapRazorPages();

app.Run();
