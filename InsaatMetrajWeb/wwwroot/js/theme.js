// DrawTally tema seçimi — varsayılan olarak işletim sistemi tercihini (prefers-color-scheme)
// izler; kullanıcı sağ üstteki düğmeyle açık/koyu seçtiğinde tercih localStorage'a yazılır ve
// bir daha o tercih kazanır. Bu script <head> içinde stylesheet'ten önce, senkron (defer/async
// olmadan) yüklenir ki <html> üzerindeki data-theme niteliği ilk boyamadan önce set edilsin
// (aksi halde koyu tema kısa süreliğine açık görünüp "flaş" yapar).
(function () {
    "use strict";

    var STORAGE_KEY = "drawtally-theme";
    var root = document.documentElement;

    function storedTheme() {
        try {
            var value = window.localStorage.getItem(STORAGE_KEY);
            return value === "light" || value === "dark" ? value : null;
        } catch (err) {
            return null;
        }
    }

    function systemPrefersDark() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    function effectiveTheme() {
        return storedTheme() || (systemPrefersDark() ? "dark" : "light");
    }

    function updateToggleButtons(theme) {
        var buttons = document.querySelectorAll("[data-theme-toggle]");
        for (var i = 0; i < buttons.length; i++) {
            buttons[i].setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
            buttons[i].setAttribute("aria-label", theme === "dark" ? "Açık moda geç" : "Koyu moda geç");
            buttons[i].title = theme === "dark" ? "Açık moda geç" : "Koyu moda geç";
        }
    }

    function updateThemeColorMeta(theme) {
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.setAttribute("content", theme === "dark" ? "#0B1220" : "#F6F7FA");
        }
    }

    function apply(theme) {
        root.setAttribute("data-theme", theme);
        root.style.colorScheme = theme;
        updateToggleButtons(theme);
        updateThemeColorMeta(theme);
    }

    function toggle() {
        var next = effectiveTheme() === "dark" ? "light" : "dark";
        try {
            window.localStorage.setItem(STORAGE_KEY, next);
        } catch (err) {
            /* localStorage kapalıysa (gizli sekme vb.) tercih kalıcı olmaz, sorun değil */
        }
        apply(next);
    }

    // Kullanıcı hiçbir şey seçmediyse sistem tercihi değişirse (ör. OS gece moduna geçtiyse)
    // sayfa açıkken de anlık takip etsin.
    if (window.matchMedia) {
        window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function () {
            if (!storedTheme()) {
                apply(effectiveTheme());
            }
        });
    }

    window.DrawTallyTheme = { toggle: toggle };

    apply(effectiveTheme());
})();
