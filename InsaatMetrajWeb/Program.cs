using InsaatMetrajWeb.Components;
using InsaatMetrajWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Uygulama genelinde tek bir örneği paylaşılan veri deposu (demo amaçlı in-memory).
// İleride bu servis, gerçek bir veritabanı (PostgreSQL/SQL Server) ile değiştirilecek.
builder.Services.AddSingleton<VeriDeposu>();

// DWG/DXF katman sınıflandırması: appsettings.json'daki "AiProvider" ("Anthropic" | "Nvidia")
// ayarına göre hangi AI sağlayıcısının kullanılacağını seçer.
if (builder.Configuration["AiProvider"] == "Nvidia")
{
    builder.Services.AddHttpClient<IAiClassificationProvider, NvidiaNimProvider>();
}
else
{
    builder.Services.AddHttpClient<IAiClassificationProvider, AnthropicClassificationProvider>();
}
builder.Services.AddTransient<AiSiniflandirmaServisi>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
