using InsaatMetrajWeb.Models;
using InsaatMetrajWeb.Services;

namespace InsaatMetrajWeb.Tests;

/// <summary>FiyatFarkiHesaplamaServisi'nin doğru endeks dönemlerini seçtiğini ve oranı doğru
/// hesapladığını doğrular — bkz. Services/FiyatFarkiHesaplamaServisi.cs.</summary>
public class FiyatFarkiHesaplamaServisiTesti
{
    private static readonly List<EndeksDonemi> OrnekEndeksler = new()
    {
        new() { Yil = 2025, Ay = 1, TufeYiUfeDegeri = 1000m },
        new() { Yil = 2026, Ay = 9, TufeYiUfeDegeri = 1250m },
        new() { Yil = 2026, Ay = 8, TufeYiUfeDegeri = 1200m },
    };

    [Fact]
    public void DogruEndeksDonemleriniSeçerVeOraniHesaplar()
    {
        var sozlesmeTarihi = new DateOnly(2025, 1, 15);
        var hakedisTarihi = new DateOnly(2026, 9, 5);

        var sonuc = FiyatFarkiHesaplamaServisi.Hesapla(sozlesmeTarihi, hakedisTarihi, imalatTutari: 10_000m, OrnekEndeksler);

        Assert.True(sonuc.Basarili);
        Assert.Equal(2025, sonuc.TemelDonem!.Yil);
        Assert.Equal(1, sonuc.TemelDonem!.Ay);
        Assert.Equal(2026, sonuc.HakedisDonemi!.Yil);
        Assert.Equal(9, sonuc.HakedisDonemi!.Ay);

        // (1250/1000 - 1) * 100 = %25
        Assert.Equal(25m, sonuc.Oran);
        Assert.Equal(2_500m, sonuc.Tutar);
        Assert.Contains("2025-01", sonuc.Ozet);
        Assert.Contains("2026-09", sonuc.Ozet);
    }

    [Fact]
    public void SozlesmeTarihiYoksa_BasarisizVeAcikliyiciMesajDoner()
    {
        var sonuc = FiyatFarkiHesaplamaServisi.Hesapla(null, new DateOnly(2026, 9, 5), 10_000m, OrnekEndeksler);

        Assert.False(sonuc.Basarili);
        Assert.Contains("sözleşme tarihi", sonuc.Mesaj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemelDonemEndeksiEksikse_BasarisizDoner()
    {
        var sozlesmeTarihi = new DateOnly(2020, 1, 1); // listede yok
        var hakedisTarihi = new DateOnly(2026, 9, 5);

        var sonuc = FiyatFarkiHesaplamaServisi.Hesapla(sozlesmeTarihi, hakedisTarihi, 10_000m, OrnekEndeksler);

        Assert.False(sonuc.Basarili);
        Assert.Contains("2020-01", sonuc.Mesaj);
    }

    [Fact]
    public void HakedisDonemiEndeksiEksikse_BasarisizDoner()
    {
        var sozlesmeTarihi = new DateOnly(2025, 1, 1);
        var hakedisTarihi = new DateOnly(2027, 3, 1); // listede yok

        var sonuc = FiyatFarkiHesaplamaServisi.Hesapla(sozlesmeTarihi, hakedisTarihi, 10_000m, OrnekEndeksler);

        Assert.False(sonuc.Basarili);
        Assert.Contains("2027-03", sonuc.Mesaj);
    }

    [Fact]
    public void EndeksAyniIseOranSifirVeTutarSifirOlur()
    {
        var sozlesmeTarihi = new DateOnly(2026, 8, 1);
        var hakedisTarihi = new DateOnly(2026, 8, 20);

        var sonuc = FiyatFarkiHesaplamaServisi.Hesapla(sozlesmeTarihi, hakedisTarihi, 10_000m, OrnekEndeksler);

        Assert.True(sonuc.Basarili);
        Assert.Equal(0m, sonuc.Oran);
        Assert.Equal(0m, sonuc.Tutar);
    }
}
