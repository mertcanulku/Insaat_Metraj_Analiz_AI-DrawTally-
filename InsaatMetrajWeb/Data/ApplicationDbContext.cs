using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InsaatMetrajWeb.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProjeKaydi> Projeler => Set<ProjeKaydi>();
    public DbSet<MetrajKalemiKaydi> MetrajKalemleri => Set<MetrajKalemiKaydi>();

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
    }
}
