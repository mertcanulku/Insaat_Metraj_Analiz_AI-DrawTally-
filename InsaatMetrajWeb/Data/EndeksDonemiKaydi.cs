namespace InsaatMetrajWeb.Data;

/// <summary>
/// Bir ay için TÜİK Yİ-ÜFE (ve varsa Bakanlığın Rayiç-TÜİK Eşleştirme Endeksleri'nden gelen
/// katsayı) değerini tutan kayıt. Resmi bir API olmadığı için bu tablo Endeks Yönetimi
/// sayfasından (bkz. Components/Pages/EndeksYonetimi.razor, sadece Admin rolü) elle,
/// ay ay girilir — ileride otomatik veri çekmeye açık bırakılmıştır.
/// FiyatFarkiHesaplamaServisi, sözleşme (temel) dönemi ile hakediş dönemine denk gelen
/// iki kaydı karşılaştırarak fiyat farkı katsayısını hesaplar.
/// </summary>
public class EndeksDonemiKaydi
{
    public int Id { get; set; }

    public int Yil { get; set; }

    /// <summary>1-12 arası ay.</summary>
    public int Ay { get; set; }

    /// <summary>TÜİK Yİ-ÜFE (yurt içi üretici fiyat endeksi) aylık değeri.</summary>
    public decimal TufeYiUfeDegeri { get; set; }

    /// <summary>Bakanlığın Rayiç-TÜİK Eşleştirme Endeksleri'nden gelen opsiyonel katsayı —
    /// dolduruysa ileride TufeYiUfeDegeri yerine/yanında kullanılabilir, şimdilik sadece bilgi amaçlı saklanır.</summary>
    public decimal? BakanlikKatsayisi { get; set; }
}
