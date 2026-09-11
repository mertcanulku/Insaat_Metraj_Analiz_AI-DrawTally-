// DrawTally çerez bildirimi — yalnızca oturum açık kimlik doğrulama çerezi kullanılır, izleme/reklam
// çerezi yok; bu yüzden kabul/red seçimi değil, tek "Anladım" ile kapatılabilen bir bilgilendirme
// şeridi yeterli. Kapatma tercihi localStorage'a yazılır, bir daha gösterilmez.
(function () {
    "use strict";

    var STORAGE_KEY = "drawtally-cerez-bildirimi-kapatildi";

    function dismissed() {
        try {
            return window.localStorage.getItem(STORAGE_KEY) === "1";
        } catch (err) {
            return false;
        }
    }

    function setup() {
        var banner = document.getElementById("cerez-bildirimi");
        if (!banner) return;

        var buton = document.getElementById("cerez-bildirimi-kapat");
        if (buton && !buton.dataset.bound) {
            buton.dataset.bound = "1";
            buton.addEventListener("click", function () {
                banner.hidden = true;
                try {
                    window.localStorage.setItem(STORAGE_KEY, "1");
                } catch (err) {
                    /* localStorage kapalıysa tercih kalıcı olmaz, sorun değil */
                }
            });
        }

        banner.hidden = dismissed();
    }

    setup();

    // Blazor'un enhanced navigation'ı sayfa DOM'unu yeniden birleştirebiliyor; şerit her seferinde
    // localStorage durumuna göre yeniden değerlendirilsin diye aynı theme.js desenini izliyoruz.
    function attachEnhancedLoadHandler() {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            window.Blazor.addEventListener("enhancedload", setup);
        } else {
            setTimeout(attachEnhancedLoadHandler, 50);
        }
    }
    attachEnhancedLoadHandler();
})();
