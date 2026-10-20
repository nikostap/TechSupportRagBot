self.addEventListener('push', event => {
    if (!event.data) return;
    const data = event.data.json();
    const language = self.navigator.language?.toLowerCase().startsWith('ru') ? 'ru' : 'en';
    const title = language === 'ru' ? data.titleRu : data.titleEn;
    const body = language === 'ru' ? data.bodyRu : data.bodyEn;
    event.waitUntil(self.registration.showNotification(title, {
        body,
        icon: '/images/class-engineering-mark-32.png',
        badge: '/images/class-engineering-mark-32.png',
        tag: data.tag,
        data: { url: data.url }
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const requested = event.notification.data?.url || '/';
    const destination = new URL(requested, self.location.origin);
    if (destination.origin !== self.location.origin) return;
    event.waitUntil((async () => {
        const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        const existing = windows.find(client => new URL(client.url).origin === destination.origin);
        if (existing) {
            await existing.navigate(destination.href);
            await existing.focus();
        } else {
            await clients.openWindow(destination.href);
        }
    })());
});
