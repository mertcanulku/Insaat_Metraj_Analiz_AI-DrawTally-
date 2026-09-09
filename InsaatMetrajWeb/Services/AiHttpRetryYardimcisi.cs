using System.Net;

namespace InsaatMetrajWeb.Services;

/// <summary>
/// AI sağlayıcı HTTP çağrılarında (NVIDIA NIM, Anthropic) tekrar eden 429 (Too Many Requests) /
/// 503 (Service Unavailable) hatalarını ve geçici zaman aşımlarını üstel geri çekilme (exponential
/// backoff) ile otomatik yeniden deneyen ortak yardımcı.
///
/// Neden gerekli: DWG poz önerisi akışı (CizimAnalizServisi.DwgPozOnerileriniEkleAsync) tek bir
/// dosya için onlarca satırı sınırlı paralellikle (AiEsZamanliIstekLimiti) ama yine de aynı anda
/// birden fazla istek göndererek işler; sağlayıcının dakika/saniye başı istek limitini aşınca
/// sağlayıcı 429 döner. Yeniden deneme olmadan bu tek bir satırın "hata" olarak işaretlenip
/// kullanıcıya "API 429 döndü" gibi ham bir hata olarak sızmasına yol açıyordu.
/// </summary>
internal static class AiHttpRetryYardimcisi
{
    private const int MaksimumDeneme = 5;

    /// <summary><paramref name="istekOlustur"/> her denemede YENİ bir HttpRequestMessage üretmeli
    /// (bir HttpRequestMessage/içeriği yalnızca bir kez gönderilebilir). 429/503 yanıtlarında ve
    /// istek zaman aşımlarında (TaskCanceledException) sağlayıcının "Retry-After" başlığı varsa ona,
    /// yoksa üstel geri çekilmeye (+ jitter) göre bekleyip tekrar dener. Son denemede de geçici hata
    /// alınırsa o yanıt/istisna olduğu gibi çağırana döner/fırlatılır — nihai hata yorumlaması
    /// (ör. "API 429 döndü" mesajı) çağıran tarafın sorumluluğunda kalır.</summary>
    public static async Task<HttpResponseMessage> GonderYenidenDenemeli(
        HttpClient http, Func<HttpRequestMessage> istekOlustur, TimeSpan denemeBasinaZamanAsimi)
    {
        for (int deneme = 1; ; deneme++)
        {
            using var istek = istekOlustur();
            using var cts = new CancellationTokenSource(denemeBasinaZamanAsimi);

            HttpResponseMessage cevap;
            try
            {
                cevap = await http.SendAsync(istek, cts.Token);
            }
            catch (TaskCanceledException) when (deneme < MaksimumDeneme)
            {
                await Task.Delay(GeriCekilmeSuresi(deneme, retryAfter: null));
                continue;
            }

            bool geciciHata = cevap.StatusCode == HttpStatusCode.TooManyRequests || (int)cevap.StatusCode == 503;
            if (geciciHata && deneme < MaksimumDeneme)
            {
                var bekleme = GeriCekilmeSuresi(deneme, cevap.Headers.RetryAfter);
                cevap.Dispose();
                await Task.Delay(bekleme);
                continue;
            }

            return cevap;
        }
    }

    private static TimeSpan GeriCekilmeSuresi(int deneme, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter != null)
        {
            if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta;
            if (retryAfter.Date is DateTimeOffset tarih)
            {
                var kalan = tarih - DateTimeOffset.UtcNow;
                if (kalan > TimeSpan.Zero) return kalan;
            }
        }

        // Sağlayıcı Retry-After vermediyse üstel geri çekilme: ~1s, 2s, 4s, 8s (+ küçük jitter,
        // aynı anda geri çekilen paralel isteklerin tekrar aynı anda çarpışmasını önlemek için).
        var taban = Math.Pow(2, deneme - 1);
        var jitter = Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(taban + jitter);
    }
}
