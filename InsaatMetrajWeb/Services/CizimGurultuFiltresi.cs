using System.Text.RegularExpressions;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// DWG/PDF çiziminden çıkarılan ham metinler arasından, yapay zeka sınıflandırmasına
/// hiç gönderilmemesi gereken "gürültüyü" (kot, ölçü zinciri, aks/eksen etiketi,
/// pafta/antet bilgisi, tekrar eden bloklar) ayıklar. AI'ya gitmeden önce çalışan
/// saf regex/tekrar tabanlı bir ön filtre katmanıdır — sınıflandırma kararı vermez,
/// sadece gürültüyü elemeye çalışır.
/// </summary>
public static class CizimGurultuFiltresi
{
    private const int VarsayilanTekrarEsigi = 4; // art arda bu sayının üzerinde tekrar eden satır tek örneğe indirilir

    // 2.1 — Kot (yükseklik) işaretleri: ±0.00, +0.77(ZA), 1380.62, ±0.00 (1408.20)(F10) vb.
    private static readonly Regex KotDeseni = new(
        @"^(±\s*0[.,]00|[+-]\d{1,2}[.,]\d{2}(\s*\(ZA\))?|\d{4}[.,]\d{2}|[+-]\d{1,2}[.,]\d{2}\s*\(\d{4}[.,]\d{2}\)\s*\([A-Z0-9]+\))$",
        RegexOptions.Compiled);

    private static readonly string[] KotAnahtarKelimeleri =
    {
        "TESVİYE ZEMİN KOTU", "TABİİ ZEMİN KOTU", "TABİ ARAZİ KOTU", "KOTUNDAN", "KOTUNA", "EĞİMLE"
    };

    // 2.2 — Ölçü zinciri: sadece rakam ve boşluktan oluşan bir satır ("500 550 400 1250 1800")
    private static readonly Regex OlcuZinciriDeseni = new(@"^[\d\s]+$", RegexOptions.Compiled);

    // 2.3 — Aks/eksen etiketi: x1, x4, y10, y19
    private static readonly Regex EksenEtiketiDeseni = new(@"^[xy]\d{1,2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2.4 — Pafta/antet: konum tabanlı tespit çağıran tarafın sorumluluğunda (sayfa alt %10-15'i
    // vb.); burada sadece anahtar-kelime tabanlı tespit yapılır.
    private static readonly string[] PaftaAnahtarKelimeleri =
    {
        "PAFTA NO", "PROJE MÜELLİFİ", "VERGİ NO", "YAPI DENETİM ONAYI", "BELEDİYE ONAYI",
        "İLÇE", "MAHALLE", "ADA", "PARSEL", "T.C.KİMLİK NO", "ODA SİC.NO", "ÖLÇEK :",
        "YİBF NO", "DİPLOMA NO", "ODA SİCİL NO"
    };

    // 2.6 — Diğer gürültü: yalnız sayfa numarası, TS standart referansı
    private static readonly Regex SayfaNoDeseni = new(@"^\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex StandartRefDeseni = new(@"^TS\s*\d{3,5}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 2.7 — Genel not/şartname bloğu: bir oda etiketi (ör. "YATAK ODASI" + "Alan: 14.2 m²")
    // gerçekte neredeyse hiç 1-3 satırı/birkaç yüz karakteri geçmez. Çizimlerdeki "GENEL NOTLAR"
    // paragrafları (yangın dolabı/kapısı ölçüleri, malzeme şartnamesi, yalıtım/sıva/boya notları vb.)
    // ise onlarca kısa satırın küçük dikey boşluklarla art arda dizilmesinden oluşur — PDF'te
    // MetinKumeleriOlustur'un satır-birleştirme sezgisi bunları tek bir dev kümede toplayabilir,
    // DWG'de ise tek bir MTEXT olarak tüm paragrafı taşıyabilir. Bu durumda AI'ya "bu bir oda mı"
    // diye sormanın (Ilgili alanı) güvenilirliği modelin talimata uyumuna bağlı kalıyor — bunun
    // yerine burada deterministik bir uzunluk/satır eşiğiyle en baştan elenir.
    private const int GenelNotMaksimumSatir = 6;
    private const int GenelNotMaksimumKarakter = 350;

    public static bool GenelNotBlokuMu(string metin)
    {
        var m = metin.Trim();
        if (m.Length == 0) return false;
        var satirSayisi = m.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return satirSayisi > GenelNotMaksimumSatir || m.Length > GenelNotMaksimumKarakter;
    }

    public static bool KotMu(string metin)
    {
        var m = metin.Trim();
        if (m.Length == 0) return false;
        return KotDeseni.IsMatch(m) || KotAnahtarKelimeleri.Any(k => m.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static bool OlcuZinciriMi(string metin)
    {
        var m = metin.Trim();
        return m.Length > 0 && OlcuZinciriDeseni.IsMatch(m);
    }

    public static bool EksenEtiketiMi(string metin) => EksenEtiketiDeseni.IsMatch(metin.Trim());

    public static bool PaftaBilgisiMi(string metin)
    {
        var m = metin.Trim();
        return m.Length > 0 && PaftaAnahtarKelimeleri.Any(k => m.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static bool DigerGurultuMu(string metin)
    {
        var m = metin.Trim();
        return SayfaNoDeseni.IsMatch(m) || StandartRefDeseni.IsMatch(m);
    }

    /// <summary>Tek bir metin parçasının (kelime/satır/blok) kesinlikle gürültü olup olmadığını söyler.
    /// Aks/eksen etiketleri (x1, y10) kasıtlı olarak burada YOK — silinmiyor, ayrı kategori (2.3 notu).</summary>
    public static bool GurultuMu(string metin) =>
        KotMu(metin) || PaftaBilgisiMi(metin) || DigerGurultuMu(metin) || GenelNotBlokuMu(metin);

    /// <summary>Bir metin bloğunun (birleştirilmiş satır kümesi) ölçü zinciri olup olmadığını
    /// ayrıca kontrol eder — tek kelime düzeyinde değil, birleşik satır/küme metninde uygulanmalı.</summary>
    public static bool OlcuZinciriBlokMu(string birlesikMetin) => OlcuZinciriMi(birlesikMetin.Replace("\n", " "));

    /// <summary>
    /// 2.5 — Aynı metnin ardışık olarak <paramref name="esik"/> defadan fazla tekrarlandığı
    /// bir dizi (OCR damga/kaşe artefaktı gibi) tek bir örneğe indirilir. Parsing pipeline'ının
    /// en başında, başka hiçbir filtreden önce uygulanmalıdır.
    /// </summary>
    public static List<T> ArdisikTekrariColaps<T>(IReadOnlyList<T> ogeler, Func<T, string> metinSecici, int esik = VarsayilanTekrarEsigi)
    {
        var sonuc = new List<T>();
        string? oncekiMetin = null;
        int tekrarSayisi = 0;

        foreach (var oge in ogeler)
        {
            var metin = metinSecici(oge).Trim();
            if (metin == oncekiMetin)
            {
                tekrarSayisi++;
                if (tekrarSayisi < esik) sonuc.Add(oge);
                // esiği aşan tekrarlar tamamen atlanır — tek örnek zaten eklenmişti
            }
            else
            {
                oncekiMetin = metin;
                tekrarSayisi = 1;
                sonuc.Add(oge);
            }
        }

        return sonuc;
    }

    /// <summary>
    /// 2.4 (tekrar tabanlı) — bir metin bloğu, projedeki sayfa/parçaların %<paramref name="oran"/>+'inde
    /// aynen (normalize edilmiş haliyle) tekrarlıyorsa pafta/antet/legend kabul edilir ve döndürülen
    /// kümeye eklenir; çağıran taraf bu kümedeki metinleri AI'ya göndermemelidir.
    /// </summary>
    public static HashSet<string> TekrarEdenBloklariBul(IReadOnlyList<string> sayfaBazliBloklar, double oran = 0.8)
    {
        if (sayfaBazliBloklar.Count == 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sayimlar = sayfaBazliBloklar
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var esik = Math.Max(2, sayfaBazliBloklar.Count * oran);
        return sayimlar.Where(kv => kv.Value >= esik).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
