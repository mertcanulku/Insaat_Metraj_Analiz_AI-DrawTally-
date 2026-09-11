using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InsaatMetrajWeb.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProjeKaydi> Projeler => Set<ProjeKaydi>();
    public DbSet<MetrajKalemiKaydi> MetrajKalemleri => Set<MetrajKalemiKaydi>();
    public DbSet<HakedisKaydi> Hakedisler => Set<HakedisKaydi>();
    public DbSet<HakedisKalemiKaydi> HakedisKalemleri => Set<HakedisKalemiKaydi>();

    public DbSet<RayicKaydi> Rayicler => Set<RayicKaydi>();
    public DbSet<PozKaydi> Pozlar => Set<PozKaydi>();
    public DbSet<PozAnalizSatiriKaydi> PozAnalizSatirlari => Set<PozAnalizSatiriKaydi>();
    public DbSet<AliasKaydi> Aliaslar => Set<AliasKaydi>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ProjeKaydi>()
            .HasOne(p => p.Sahip)
            .WithMany()
            .HasForeignKey(p => p.SahipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MetrajKalemiKaydi>()
            .HasOne(k => k.ProjeKaydi)
            .WithMany(p => p.MetrajKalemleri)
            .HasForeignKey(k => k.ProjeKaydiId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<HakedisKaydi>()
            .HasOne(h => h.ProjeKaydi)
            .WithMany(p => p.Hakedisler)
            .HasForeignKey(h => h.ProjeKaydiId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<HakedisKalemiKaydi>()
            .HasOne(k => k.HakedisKaydi)
            .WithMany(h => h.Kalemler)
            .HasForeignKey(k => k.HakedisKaydiId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RayicKaydi>()
            .HasIndex(r => r.Kod);

        builder.Entity<PozKaydi>()
            .HasIndex(p => p.PozKodu);

        builder.Entity<PozAnalizSatiriKaydi>()
            .HasOne(s => s.PozKaydi)
            .WithMany(p => p.AnalizSatirlari)
            .HasForeignKey(s => s.PozKaydiId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PozAnalizSatiriKaydi>()
            .HasOne(s => s.RayicKaydi)
            .WithMany()
            .HasForeignKey(s => s.RayicKaydiId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<AliasKaydi>()
            .HasOne(a => a.PozKaydi)
            .WithMany()
            .HasForeignKey(a => a.PozKaydiId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
