using InsaatMetrajWeb.Data;
using InsaatMetrajWeb.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InsaatMetrajWeb.Tests;

/// <summary>
/// VeriDeposu.FiyatFarkiniOtomatikHesapla'yı gerçek bir (bellek içi) SQLite veritabanına karşı
/// uçtan uca test eder — proje kaydındaki SozlesmeTarihi ile EndeksDonemleri tablosunun doğru
/// eşleştiğini, servis katmanının (FiyatFarkiHesaplamaServisi) doğru çağrıldığını doğrular.
/// </summary>
public class FiyatFarkiOtomatikHesapEntegrasyonTesti : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly VeriDeposu _veri;

    public FiyatFarkiOtomatikHesapEntegrasyonTesti()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _veri = new VeriDeposu(_db, new PozKutuphanesi());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<string> KullaniciEkle(string id)
    {
        _db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@test.local", Email = $"{id}@test.local" });
        await _db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ProjedeSozlesmeTarihiVeEndeksVarsa_OtomatikHesapBasarili()
    {
        await KullaniciEkle("kullanici-1");
        var proje = new ProjeKaydi { Ad = "Test", SahipId = "kullanici-1", SozlesmeTarihi = new DateOnly(2025, 1, 10) };
        _db.Projeler.Add(proje);
        _db.EndeksDonemleri.Add(new EndeksDonemiKaydi { Yil = 2025, Ay = 1, TufeYiUfeDegeri = 1000m });
        _db.EndeksDonemleri.Add(new EndeksDonemiKaydi { Yil = 2026, Ay = 9, TufeYiUfeDegeri = 1100m });
        await _db.SaveChangesAsync();

        var sonuc = await _veri.FiyatFarkiniOtomatikHesapla("kullanici-1", proje.Id, new DateOnly(2026, 9, 1), imalatTutari: 5_000m);

        Assert.True(sonuc.Basarili);
        Assert.Equal(10m, sonuc.Oran); // (1100/1000 - 1) * 100
        Assert.Equal(500m, sonuc.Tutar);
    }

    [Fact]
    public async Task ProjedeSozlesmeTarihiYoksa_HataMesajiDoner()
    {
        await KullaniciEkle("kullanici-1");
        var proje = new ProjeKaydi { Ad = "Test", SahipId = "kullanici-1" };
        _db.Projeler.Add(proje);
        await _db.SaveChangesAsync();

        var sonuc = await _veri.FiyatFarkiniOtomatikHesapla("kullanici-1", proje.Id, new DateOnly(2026, 9, 1), imalatTutari: 5_000m);

        Assert.False(sonuc.Basarili);
    }

    [Fact]
    public async Task BaskaKullanicininProjesineErisemez()
    {
        await KullaniciEkle("kullanici-1");
        var proje = new ProjeKaydi { Ad = "Test", SahipId = "kullanici-1", SozlesmeTarihi = new DateOnly(2025, 1, 10) };
        _db.Projeler.Add(proje);
        await _db.SaveChangesAsync();

        var sonuc = await _veri.FiyatFarkiniOtomatikHesapla("baska-kullanici", proje.Id, new DateOnly(2026, 9, 1), imalatTutari: 5_000m);

        Assert.False(sonuc.Basarili);
        Assert.Equal("Proje bulunamadı.", sonuc.Mesaj);
    }

    [Fact]
    public async Task EndeksEkleVeyaGuncelle_AyniDonemIcinUpsertYapar()
    {
        await _veri.EndeksEkleVeyaGuncelle(2026, 1, 1000m, null);
        await _veri.EndeksEkleVeyaGuncelle(2026, 1, 1050m, 1.05m); // aynı dönem -> güncelleme, ekleme değil

        var liste = await _veri.EndeksleriListele();

        var tekKayit = Assert.Single(liste);
        Assert.Equal(1050m, tekKayit.TufeYiUfeDegeri);
        Assert.Equal(1.05m, tekKayit.BakanlikKatsayisi);
    }
}
