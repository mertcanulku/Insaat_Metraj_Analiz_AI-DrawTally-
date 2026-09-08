using InsaatMetrajWeb.Components;
using InsaatMetrajWeb.Components.Account;
using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// WebApplication.CreateBuilder yalnızca ASPNETCORE_ENVIRONMENT=Development iken
// user-secrets'ı otomatik yükler; Production'da da (ör. bu demo dev sunucusunda)
// yüklensin diye açıkça ekliyoruz — appsettings.json'a secret yazmaktan kaçınmak için.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://localhost:{port}");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Poz/Rayiç/Alias kütüphanesi (ÇŞB verisi, tüm kullanıcılar arasında ortak) — uygulama
// ömrü boyunca bir kez veritabanından yüklenip bellekte tutulur, bkz. aşağıdaki seed bloğu.
builder.Services.AddSingleton<PozKutuphanesi>();

// Kullanıcıya özel projeler veritabanından okunur; ApplicationDbContext scoped olduğu için
// VeriDeposu de scoped olmalı (poz kütüphanesi kısmı yukarıdaki singleton'a delege eder).
builder.Services.AddScoped<VeriDeposu>();

// PDF/DWG çizim analizi (oda adı + alan çıkarımı): appsettings.json'daki "AiProvider"
// ("Anthropic" | "Nvidia") ayarına göre hangi AI sağlayıcısının kullanılacağını seçer.
if (builder.Configuration["AiProvider"] == "Nvidia")
{
    builder.Services.AddHttpClient<IAiClassificationProvider, NvidiaNimProvider>();
}
else
{
    builder.Services.AddHttpClient<IAiClassificationProvider, AnthropicClassificationProvider>();
}
builder.Services.AddTransient<AiSiniflandirmaServisi>();

// PDF/DWG çizimlerinden oda/alan çıkaran ortak analiz servisi (AiSiniflandirmaServisi + VeriDeposu üzerine kurulu).
builder.Services.AddScoped<CizimAnalizServisi>();

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

    // İlk açılışta (veya boş bir veritabanında) ÇŞB rayiç/poz kütüphanesini
    // Data/PozSeed/*.json dosyalarından bir kez içe aktarır, ardından belleğe yükler.
    var pozKutuphanesi = scope.ServiceProvider.GetRequiredService<PozKutuphanesi>();
    await pozKutuphanesi.SeedVeYukleAsync(db, app.Environment.ContentRootPath);
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
