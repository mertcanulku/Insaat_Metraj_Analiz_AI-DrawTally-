using System.Text.RegularExpressions;
using InsaatMetrajWeb.Models;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// Bölüm 4 — çizimden çıkarılan metinler (ve varsa DWG katman adları) üzerinden projenin
/// hangi disipline (statik/mimari/ısıtma/sıhhi/elektrik) ait olduğunu ağırlıklı anahtar
/// kelime/format skoruyla tahmin eder. Disiplinler kelime paylaşabildiğinden (ör. mimari
/// bodrum planında "hidrofor" geçebilir) tek bir kelime tek başına yeterli kanıt sayılmaz —
/// en yüksek skorlu disiplin, minimum bir eşiği geçiyorsa döndürülür, aksi halde Bilinmiyor.
/// </summary>
public static class DisiplinTespitServisi
{
    private const int MinimumSkorEsigi = 3;

    private readonly record struct Sinyal(Regex? Desen, string? AnahtarKelime, int Agirlik)
    {
        public bool Eslesiyor(string metin) =>
            Desen != null ? Desen.IsMatch(metin) : metin.Contains(AnahtarKelime!, StringComparison.OrdinalIgnoreCase);
    }

    // 4.1.1 — pafta/başlıkta disiplin doğrudan yazıyorsa en güçlü sinyal, direkt karar verdirir
    private static readonly (string AnahtarKelime, ProjeDisiplini Disiplin)[] PaftaBaslikSinyalleri =
    {
        ("STATİK MALZEME", ProjeDisiplini.Statik),
        ("STATİK PROJE", ProjeDisiplini.Statik),
        ("ISITMA TESİSATI PROJESİ", ProjeDisiplini.Isitma),
        ("SIHHİ TESİSAT PROJESİ", ProjeDisiplini.Sihhi),
        ("ELEKTRİK TESİSATI UYGULAMA PROJESİ", ProjeDisiplini.Elektrik),
        ("MİMARİ PROJE", ProjeDisiplini.Mimari),
    };

    // 4.1.2 — DWG katman adı önekleri (ör. "S-KOLON", "A-DUVAR", "M-TESİSAT", "E-ELEKTRİK")
    private static readonly (string Onek, ProjeDisiplini Disiplin)[] KatmanOnekSinyalleri =
    {
        ("S-", ProjeDisiplini.Statik),
        ("A-", ProjeDisiplini.Mimari),
        ("M-", ProjeDisiplini.Isitma),
        ("E-", ProjeDisiplini.Elektrik),
    };

    // 4.2 — skorlama tablosu
    private static readonly Dictionary<ProjeDisiplini, Sinyal[]> IcerikSinyalleri = new()
    {
        [ProjeDisiplini.Statik] = new Sinyal[]
        {
            new(new Regex(@"^[A-Z]{1,2}\d{1,3}\s+[A-Z]{0,2}\d+[,.]?\d*x\d+[,.]?\d*(x\d+[,.]?\d*)?$", RegexOptions.Compiled), null, 3),
            new(null, "DİNAMİK ANALİZ PARAMETRELERİ", 3),
            new(null, "Sds", 2),
            new(null, "Sd1", 2),
            new(null, "PAS PAYI", 2),
            new(null, "KOLON KALIP PLANI", 3),
            new(null, "KİRİŞ KALIP PLANI", 3),
            new(null, "DONATI", 2),
            new(null, "ETRİYE AÇILIMLARI", 3),
            new(null, "APLİKASYON PLANI", 2),
        },
        [ProjeDisiplini.Mimari] = new Sinyal[]
        {
            new(new Regex(@"Alan\s*[:=]?\s*\d+[.,]\d+\s*m[²2]", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 2),
            new(new Regex(@"^[A-Z]{1,2}\d?\s*[:\s]?\d{2,3}[/x]\d{2,3}$", RegexOptions.Compiled), null, 2),
            new(null, "SALON", 1),
            new(null, "MUTFAK", 1),
            new(null, "YATAK ODASI", 2),
            new(null, "SERAMİK KAPLAMA", 2),
            new(null, "PLASTİK BOYA", 2),
            new(null, "GAZ BETON", 2),
            new(null, "KABA SIVA", 2),
            new(null, "SU YALITIMI MEMBRAN", 2),
            new(null, "BETONARME PERDE", 2),
        },
        [ProjeDisiplini.Isitma] = new Sinyal[]
        {
            new(new Regex(@"Qı?:\s*[\d.,]+\s*Watt", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 3),
            new(new Regex(@"L:\d+\s*mt\s*M:\d+cm", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 3),
            new(new Regex(@"\d+\s*AĞIZ", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 2),
            new(null, "KOLLEKTÖR", 2),
            new(null, "PE-Xa BORU", 2),
            new(null, "YERDEN ISITMA", 3),
            new(null, "HERMETİK KOMBİ", 3),
            new(null, "SİRKÜLASYON POMPASI", 2),
        },
        [ProjeDisiplini.Sihhi] = new Sinyal[]
        {
            new(new Regex(@"Ø\s*\d+", RegexOptions.Compiled), null, 1), // zayıf sinyal — Ø ısıtma/elektrik şemalarında da geçebilir
            new(new Regex(@"\d+([.,]\d+)?\s*(YB|SB)\s*Ø\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 3),
            new(null, "PİSSU BORUSU", 3),
            new(null, "ROGAR", 2),
            new(null, "HİDROFOR", 2),
            new(null, "KLOZET", 2),
            new(null, "SİFON", 2),
        },
        [ProjeDisiplini.Elektrik] = new Sinyal[]
        {
            new(new Regex(@"\d+x\d+\s*(A|kA)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 3),
            new(new Regex(@"\d+x[\d,]+\s*(mm[²2]|NYM|NYY)", RegexOptions.Compiled | RegexOptions.IgnoreCase), null, 3),
            new(new Regex(@"^[A-Z]-\d+\s*W$", RegexOptions.Compiled), null, 2),
            new(null, "PANO", 1),
            new(null, "SİGORTA", 2),
            new(null, "TOPRAKLAMA", 2),
            new(null, "ARMATÜR", 1),
            new(null, "KOLON ŞEMASI", 2), // "KOLON" tek başına statikte de geçer; tüm ifade elektrik bağlamı ("KOLON ŞEMASI" = riser diyagramı)
        },
    };

    public static ProjeDisiplini TespitEt(IEnumerable<string> metinler, IEnumerable<string>? katmanAdlari = null)
    {
        var hepsi = metinler.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (hepsi.Count == 0) return ProjeDisiplini.Bilinmiyor;

        foreach (var metin in hepsi)
        {
            foreach (var (anahtar, disiplin) in PaftaBaslikSinyalleri)
            {
                if (metin.Contains(anahtar, StringComparison.OrdinalIgnoreCase))
                    return disiplin;
            }
        }

        var puanlar = Enum.GetValues<ProjeDisiplini>()
            .Where(d => d != ProjeDisiplini.Bilinmiyor)
            .ToDictionary(d => d, _ => 0);

        if (katmanAdlari != null)
        {
            foreach (var katman in katmanAdlari)
            {
                foreach (var (onek, disiplin) in KatmanOnekSinyalleri)
                {
                    if (katman.StartsWith(onek, StringComparison.OrdinalIgnoreCase))
                        puanlar[disiplin] += 2;
                }
            }
        }

        foreach (var metin in hepsi)
        {
            foreach (var (disiplin, sinyaller) in IcerikSinyalleri)
            {
                foreach (var sinyal in sinyaller)
                {
                    if (sinyal.Eslesiyor(metin)) puanlar[disiplin] += sinyal.Agirlik;
                }
            }
        }

        var enYuksek = puanlar.OrderByDescending(kv => kv.Value).First();
        return enYuksek.Value >= MinimumSkorEsigi ? enYuksek.Key : ProjeDisiplini.Bilinmiyor;
    }

    public static string GosterimAdi(ProjeDisiplini disiplin) => disiplin switch
    {
        ProjeDisiplini.Statik => "Statik / Yapısal",
        ProjeDisiplini.Mimari => "Mimari",
        ProjeDisiplini.Isitma => "Isıtma Tesisatı",
        ProjeDisiplini.Sihhi => "Sıhhi Tesisat",
        ProjeDisiplini.Elektrik => "Elektrik Tesisatı",
        _ => "Bilinmiyor"
    };
}
