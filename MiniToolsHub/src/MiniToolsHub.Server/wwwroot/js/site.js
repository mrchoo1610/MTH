// Copy-to-clipboard buttons: <button data-copy="#selector" data-copied="Copied">
document.addEventListener('click', async function (e) {
  var btn = e.target.closest ? e.target.closest('[data-copy]') : null;
  if (!btn) return;
  var src = document.querySelector(btn.getAttribute('data-copy'));
  if (!src) return;
  var text = src.textContent.trim();
  var original = btn.textContent;
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
    } else {
      var ta = document.createElement('textarea');
      ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove();
    }
    btn.textContent = btn.getAttribute('data-copied') || 'OK';
    setTimeout(function () { btn.textContent = original; }, 1500);
  } catch (err) { /* clipboard unavailable: ignore */ }
});
