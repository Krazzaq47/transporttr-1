using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MT.Data;
using Microsoft.Extensions.DependencyInjection;

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

// ====== MVC / Razor ======
builder.Services.AddControllersWithViews();

// IMPORTANT: allow the Identity UI to be anonymous to avoid redirect loops
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.AllowAnonymousToAreaFolder("Identity", "/");
        options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
        // or, to be extra tight, list individual pages:
        // options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Login");
        // options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Logout");
        // options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Register");
    });

// Global: require auth everywhere unless explicitly allowed above
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// ====== Ensure database is up to date (auto-migrate) ======
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}
catch (Exception)
{
    // In Development, the developer exception page will show details on first DB access
}

// ====== Seed Roles & Admin User ======
try
{
    using var scope = app.Services.CreateScope();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    string[] roles = new[] { "Admin", "DocumentVerifier", "FinalApprover", "MinistryOfficer", "Owner" };
    foreach (var r in roles)
    {
        if (!await roleMgr.RoleExistsAsync(r))
            await roleMgr.CreateAsync(new IdentityRole(r));
    }

    // Admin from configuration if present, else fallbacks for development
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
catch (Exception)
{
    // ignore seeding failures in dev; they will surface in logs if critical
}

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

app.UseAuthentication();   // must be before UseAuthorization
app.UseAuthorization();

// ====== Endpoints ======
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
