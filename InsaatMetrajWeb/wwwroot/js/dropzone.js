window.dropzoneVisual = function (dropId) {
    const el = document.getElementById(dropId);
    if (!el || el.dataset.dzBound) return;
    el.dataset.dzBound = "1";

    ["dragenter", "dragover"].forEach(evt => el.addEventListener(evt, e => {
        e.preventDefault();
        el.classList.add("dropzone-active");
    }));

    ["dragleave", "drop"].forEach(evt => el.addEventListener(evt, () => {
        el.classList.remove("dropzone-active");
    }));
};
