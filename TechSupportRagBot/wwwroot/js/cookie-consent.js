(() => {
    const banner = document.querySelector('[data-cookie-banner]');
    if (!banner) return;
    const key = 'classeng-cookie-choice';
    const show = () => { banner.hidden = false; };
    try { if (!localStorage.getItem(key)) show(); } catch { show(); }
    document.querySelector('[data-cookie-settings]')?.addEventListener('click', show);
    banner.querySelectorAll('[data-cookie-choice]').forEach(button => {
        button.addEventListener('click', () => {
            try { localStorage.setItem(key, button.dataset.cookieChoice); } catch {}
            banner.hidden = true;
        });
    });
})();
