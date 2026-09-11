using System.Globalization;
using InsaatMetrajWeb.Components;
using InsaatMetrajWeb.Components.Account;
using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Models;
using InsaatMetrajWeb.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

// QuestPDF (PDF dışa aktarım için) — Community lisansı, küçük/orta ölçekli kullanım için ücretsizdir.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// WebApplication.CreateBuilder yalnızca ASPNETCORE_ENVIRONMENT=Development iken
// user-secrets'ı otomatik yükler; Production'da da (ör. bu demo dev sunucusunda)
// yüklensin diye açıkça ekliyoruz — appsettings.json'a secret yazmaktan kaçınmak için.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// 0.0.0.0 üzerinden dinlenir (sadece localhost değil) — aynı ağdaki (LAN) başka bilgisayar ve
// telefonlar, bu makinenin yerel IP adresi üzerinden (ör. http://192.168.1.23:5000) erişebilsin diye.
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Para birimi tespiti: ziyaretçinin tarayıcısının Accept-Language'ına göre CultureInfo.CurrentCulture
// set edilir (bkz. app.UseRequestLocalization altta) — ParaFormatlayici bunu kullanarak tutarları
// TL/USD/EUR vb. hangi ülkeden bağlanılıyorsa o ülkenin para birimi sembolüyle gösterir. Varsayılan
// (Accept-Language okunamazsa) Türkiye — uygulamanın asıl kitlesi.
var desteklenenKulturler = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("tr-TR");
    options.SupportedCultures = desteklenenKulturler;
    options.SupportedUICultures = desteklenenKulturler;
});

// Poz/Rayiç/Alias kütüphanesi (ÇŞB verisi, tüm kullanıcılar arasında ortak) — uygulama
// ömrü boyunca bir kez veritabanından yüklenip bellekte tutulur, bkz. aşağıdaki seed bloğu.
builder.Services.AddSingleton<PozKutuphanesi>();

// Kullanıcıya özel projeler veritabanından okunur; ApplicationDbContext scoped olduğu için
// VeriDeposu de scoped olmalı (poz kütüphanesi kısmı yukarıdaki singleton'a delege eder).
builder.Services.AddScoped<VeriDeposu>();

// PDF/DWG çizim analizi (oda adı + alan çıkarımı): appsettings.json'daki "AiProvider"
// ("Anthropic" | "Nvidia" | "Gemini" | "Groq") ayarına göre hangi AI sağlayıcısının kullanılacağını seçer.
switch (builder.Configuration["AiProvider"])
{
    case "Nvidia":
        builder.Services.AddHttpClient<IAiClassificationProvider, NvidiaNimProvider>();
        break;
    case "Gemini":
        builder.Services.AddHttpClient<IAiClassificationProvider, GeminiClassificationProvider>();
        break;
    case "Groq":
        builder.Services.AddHttpClient<IAiClassificationProvider, GroqProvider>();
        break;
    default:
        builder.Services.AddHttpClient<IAiClassificationProvider, AnthropicClassificationProvider>();
        break;
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
builder.Services.AddScoped<SifreSifirlamaServisi>();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authBuilder.AddIdentityCookies();

// "Google ile devam et" — appsettings.json'daki Google:ClientId/ClientSecret boşsa (henüz
// Google Cloud Console'da bir OAuth istemcisi oluşturulmadıysa) bu sağlayıcı hiç kaydedilmez;
// Login/Register sayfalarındaki buton da bu durumda otomatik gizlenir (bkz. GoogleGirisAktifMi).
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/giris";
    options.LogoutPath = "/account/cikis";
    options.AccessDeniedPath = "/account/giris";
});

// Not: global bir FallbackPolicy (RequireAuthenticatedUser) kasıtlı olarak KULLANILMIYOR.
// Her Razor sayfası zaten kendi [Authorize] veya [AllowAnonymous] @attribute'unu taşıyor (bkz. her
// sayfanın başı) ve bu, AuthorizeRouteView (Routes.razor) üzerinden zaten uygulanıyor. Bir FallbackPolicy
// eklemek, sayfa bazlı korumaya hiçbir şey katmadan, Blazor Server'ın kendi iç endpoint'lerini
// (ör. _framework/blazor.web.js) da kimlik doğrulama istemeye zorluyor — bu da anonim erişilebilen
// interaktif sayfalarda (ör. Landing) SignalR devresinin hiç açılmamasına yol açıyordu.
builder.Services.AddAuthorization();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("VeritabaniBaglantisi")
        ?? "Data Source=kullanicilar.db"));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
    })
    .AddRoles<IdentityRole>()
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

    // "Admin" rolü ilk açılışta bir kez oluşturulur (kalıcı altyapı — bkz. ApplicationUser/Roller).
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// "Google ile devam et" butonunun post ettiği hedef: Google'ın kendi oturum açma ekranına
// yönlendirir (Challenge), oradan dönüşte Google'ın CallbackPath'i (varsayılan /signin-google)
// external cookie'yi oluşturur, ardından kullanıcı HariciGirisTamamla.razor'a (returnUrl ile)
// yönlendirilir — bkz. o sayfa için ExternalLoginSignInAsync/otomatik kayıt mantığı.
app.MapPost("/account/google-giris", (HttpContext context, SignInManager<ApplicationUser> signInManager, string? returnUrl) =>
{
    var geriDonusYolu = $"/account/harici-giris-tamamla?returnUrl={Uri.EscapeDataString(returnUrl ?? "/projeler")}";
    var ozellikler = signInManager.ConfigureExternalAuthenticationProperties("Google", geriDonusYolu);
    return Results.Challenge(ozellikler, ["Google"]);
});

// Proje keşif özetinin .xlsx olarak indirilmesi — Razor bileşeni yerine düz bir endpoint,
// çünkü dosya indirme cevabı (Content-Disposition) SignalR devresi üzerinden değil, doğrudan
// bir HTTP GET isteğiyle (basit <a href> linki) dönmeli.
app.MapGet("/proje/{id:int}/excel", async (int id, System.Security.Claims.ClaimsPrincipal kullanici, VeriDeposu veri, UserManager<ApplicationUser> userManager, bool logo = true) =>
{
    var sahipId = kullanici.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (sahipId == null) return Results.Challenge();

    var sahip = await userManager.FindByIdAsync(sahipId);
    if (sahip == null || !UyelikServisi.DisaAktarimIzinli(sahip.EtkinPlan))
        return Results.Forbid();

    var proje = await veri.ProjeBul(sahipId, id);
    if (proje == null) return Results.NotFound();

    var dosyaAdi = string.Concat(proje.Ad.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
    if (dosyaAdi.Length == 0) dosyaAdi = "proje";

    var icerik = ExcelDisaAktarimServisi.ProjeyiXlsxOlarakOlustur(proje, logoBasligiEkle: logo, firmaLogosu: sahip.FirmaLogoVerisi);
    return Results.File(icerik,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"{dosyaAdi}-kesif-ozeti.xlsx");
}).RequireAuthorization();

// Proje keşif özetinin .pdf olarak indirilmesi — DrawTally başlığı burada sabit (Excel'deki
// gibi açılıp kapatılabilir değil), çünkü PDF zaten resmi/paylaşılabilir bir rapor formatı.
app.MapGet("/proje/{id:int}/pdf", async (int id, System.Security.Claims.ClaimsPrincipal kullanici, VeriDeposu veri, UserManager<ApplicationUser> userManager) =>
{
    var sahipId = kullanici.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (sahipId == null) return Results.Challenge();

    var sahip = await userManager.FindByIdAsync(sahipId);
    if (sahip == null || !UyelikServisi.DisaAktarimIzinli(sahip.EtkinPlan))
        return Results.Forbid();

    var proje = await veri.ProjeBul(sahipId, id);
    if (proje == null) return Results.NotFound();

    var dosyaAdi = string.Concat(proje.Ad.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
    if (dosyaAdi.Length == 0) dosyaAdi = "proje";

    var icerik = PdfDisaAktarimServisi.ProjeyiPdfOlarakOlustur(proje, firmaLogosu: sahip.FirmaLogoVerisi);
    return Results.File(icerik, "application/pdf", $"{dosyaAdi}-kesif-ozeti.pdf");
}).RequireAuthorization();

// Bir hakedişin .xlsx olarak indirilmesi — /proje/{id}/excel ile aynı sebepten düz endpoint.
app.MapGet("/proje/{id:int}/hakedis/{hakedisId:int}/excel", async (int id, int hakedisId, System.Security.Claims.ClaimsPrincipal kullanici, VeriDeposu veri, UserManager<ApplicationUser> userManager) =>
{
    var sahipId = kullanici.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (sahipId == null) return Results.Challenge();

    var sahip = await userManager.FindByIdAsync(sahipId);
    if (sahip == null || !UyelikServisi.DisaAktarimIzinli(sahip.EtkinPlan))
        return Results.Forbid();

    var proje = await veri.ProjeBul(sahipId, id);
    var hakedis = await veri.HakedisBul(sahipId, id, hakedisId);
    if (proje == null || hakedis == null) return Results.NotFound();

    var dosyaAdi = string.Concat(proje.Ad.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
    if (dosyaAdi.Length == 0) dosyaAdi = "proje";

    var icerik = HakedisExcelDisaAktarimServisi.HakedisiXlsxOlarakOlustur(proje, hakedis, firmaLogosu: sahip.FirmaLogoVerisi);
    return Results.File(icerik,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"{dosyaAdi}-hakedis-{hakedis.HakedisNo}.xlsx");
}).RequireAuthorization();

// Bir hakedişin .pdf olarak indirilmesi.
app.MapGet("/proje/{id:int}/hakedis/{hakedisId:int}/pdf", async (int id, int hakedisId, System.Security.Claims.ClaimsPrincipal kullanici, VeriDeposu veri, UserManager<ApplicationUser> userManager) =>
{
    var sahipId = kullanici.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (sahipId == null) return Results.Challenge();

    var sahip = await userManager.FindByIdAsync(sahipId);
    if (sahip == null || !UyelikServisi.DisaAktarimIzinli(sahip.EtkinPlan))
        return Results.Forbid();

    var proje = await veri.ProjeBul(sahipId, id);
    var hakedis = await veri.HakedisBul(sahipId, id, hakedisId);
    if (proje == null || hakedis == null) return Results.NotFound();

    var dosyaAdi = string.Concat(proje.Ad.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
    if (dosyaAdi.Length == 0) dosyaAdi = "proje";

    var icerik = HakedisPdfDisaAktarimServisi.HakedisiPdfOlarakOlustur(proje, hakedis, firmaLogosu: sahip.FirmaLogoVerisi);
    return Results.File(icerik, "application/pdf", $"{dosyaAdi}-hakedis-{hakedis.HakedisNo}.pdf");
}).RequireAuthorization();

// Firma logosu önizlemesi — Profil.razor'daki <img> burada gösterir. Yalnızca oturum sahibinin
// kendi logosu (parametre almadan doğrudan ClaimsPrincipal'den) döner.
app.MapGet("/profil/logo", async (System.Security.Claims.ClaimsPrincipal kullanici, UserManager<ApplicationUser> userManager) =>
{
    var sahipId = kullanici.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (sahipId == null) return Results.Challenge();

    var sahip = await userManager.FindByIdAsync(sahipId);
    if (sahip?.FirmaLogoVerisi == null || sahip.FirmaLogoIcerikTuru == null) return Results.NotFound();

    return Results.File(sahip.FirmaLogoVerisi, sahip.FirmaLogoIcerikTuru);
}).RequireAuthorization();

app.Run();
