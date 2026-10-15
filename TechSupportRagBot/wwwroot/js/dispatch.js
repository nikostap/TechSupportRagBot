(() => {
  const sidebar = document.getElementById('dispatch-sidebar');
  const toggle = document.querySelector('[data-sidebar-toggle]');
  const backdrop = document.querySelector('[data-sidebar-close]');
  if (!sidebar || !toggle || !backdrop) return;

  const setOpen = (open) => {
    sidebar.classList.toggle('is-open', open);
    backdrop.classList.toggle('is-open', open);
    toggle.setAttribute('aria-expanded', String(open));
    document.body.classList.toggle('dispatch-menu-open', open);
    if (open) sidebar.querySelector('a')?.focus();
    else toggle.focus();
  };

  toggle.addEventListener('click', () => setOpen(toggle.getAttribute('aria-expanded') !== 'true'));
  backdrop.addEventListener('click', () => setOpen(false));
  sidebar.addEventListener('click', (event) => {
    if (event.target.closest('a') && window.matchMedia('(max-width: 900px)').matches) setOpen(false);
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && toggle.getAttribute('aria-expanded') === 'true') setOpen(false);
  });
  window.matchMedia('(min-width: 901px)').addEventListener('change', (event) => {
    if (event.matches && toggle.getAttribute('aria-expanded') === 'true') setOpen(false);
  });

  const notification = document.querySelector('.notification-menu');
  const bell = notification?.querySelector('.notification-bell');
  bell?.addEventListener('click', (event) => {
    if (!window.matchMedia('(max-width: 900px)').matches) return;
    event.preventDefault();
    const open = !notification.classList.contains('is-open');
    notification.classList.toggle('is-open', open);
    bell.setAttribute('aria-expanded', String(open));
  });
  document.addEventListener('click', (event) => {
    if (notification?.classList.contains('is-open') && !notification.contains(event.target)) {
      notification.classList.remove('is-open');
      bell?.setAttribute('aria-expanded', 'false');
    }
  });
})();
