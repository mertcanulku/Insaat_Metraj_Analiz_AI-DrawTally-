using InsaatMetrajWeb.Components;
using InsaatMetrajWeb.Components.Account;
using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://localhost:{port}");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Poz/Rayiç kütüphanesi (ortak, bellek içi) + kullanıcıya özel projeler (veritabanı).
// ApplicationDbContext scoped olduğu için VeriDeposu de scoped olmalı.
builder.Services.AddScoped<VeriDeposu>();

// DWG/DXF katman sınıflandırması için Claude API'sine bağlanan servis.
builder.Services.AddHttpClient<AiSiniflandirmaServisi>();

// --- Kullanıcı hesapları (ASP.NET Core Identity + SQLite) ---
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddCascadingAuthenticationState();

// E-posta doğrulama kodu gönderimi (Smtp:Host boşsa kod ekranda gösterilir, bkz. SmtpEmailGonderici).
builder.Services.AddScoped<IEmailGonderici, SmtpEmailGonderici>();
builder.Services.AddScoped<EmailDogrulamaServisi>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/giris";
    options.LogoutPath = "/account/cikis";
    options.AccessDeniedPath = "/account/giris";
});

builder.Services.AddAuthorization(options =>
{
    // Hesap sayfaları [AllowAnonymous] ile işaretlenmedikçe, tüm sayfalar giriş ister.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("VeritabaniBaglantisi")
        ?? "Data Source=kullanicilar.db"));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
