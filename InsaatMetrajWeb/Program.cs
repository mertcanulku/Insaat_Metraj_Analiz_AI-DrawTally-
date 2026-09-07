using InsaatMetrajWeb.Components;
using InsaatMetrajWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Uygulama genelinde tek bir örneği paylaşılan veri deposu (demo amaçlı in-memory).
// İleride bu servis, gerçek bir veritabanı (PostgreSQL/SQL Server) ile değiştirilecek.
builder.Services.AddSingleton<VeriDeposu>();

// PDF/DWG çizim analizi (oda adı + alan çıkarımı) için Claude API'sine bağlanan servis.
builder.Services.AddHttpClient<AiSiniflandirmaServisi>();

// PDF/DWG çizimlerinden oda/alan çıkaran ortak analiz servisi (AiSiniflandirmaServisi + VeriDeposu üzerine kurulu).
builder.Services.AddScoped<CizimAnalizServisi>();

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
