(() => {
  const button = document.querySelector('[data-theme-toggle]');
  if (!button) return;

  const render = () => {
    const isDark = document.documentElement.dataset.theme === 'dark';
    const label = isDark ? button.dataset.lightLabel : button.dataset.darkLabel;
    button.setAttribute('aria-label', label);
    button.setAttribute('title', label);
    button.setAttribute('aria-pressed', String(isDark));
  };

  button.addEventListener('click', () => {
    const isDark = document.documentElement.dataset.theme !== 'dark';
    if (isDark) {
      document.documentElement.dataset.theme = 'dark';
      document.documentElement.dataset.bsTheme = 'dark';
    } else {
      delete document.documentElement.dataset.theme;
      delete document.documentElement.dataset.bsTheme;
    }
    try { localStorage.setItem('classeng-theme', isDark ? 'dark' : 'light'); } catch {}
    render();
  });

  render();
})();
