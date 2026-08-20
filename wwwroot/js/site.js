// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// 判讀/找歌的等待遮罩。任何元素加 data-loading="訊息" 即可觸發:
//   <form>          → submit 時顯示(表單真的會送出,遮罩撐到新頁面載入取代整頁)
//   <a> / <button>  → click 時顯示
// 遮罩本身蓋住整頁、擋掉點擊,順帶防止重複送出。data-loading 值留空就用預設文字。
(function () {
    var overlay = document.getElementById('iwLoading');
    if (!overlay) return;
    var textEl = overlay.querySelector('[data-loading-text]');
    var defaultText = textEl ? textEl.textContent : '';

    function show(msg) {
        if (textEl) textEl.textContent = msg || defaultText;
        overlay.classList.add('is-on');
    }

    document.querySelectorAll('[data-loading]').forEach(function (el) {
        var msg = el.getAttribute('data-loading');
        var evt = el.tagName === 'FORM' ? 'submit' : 'click';
        el.addEventListener(evt, function () { show(msg); });
    });
}());
