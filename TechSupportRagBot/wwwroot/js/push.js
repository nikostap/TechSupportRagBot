(() => {
    const form = document.querySelector('[data-push-form]');
    const button = document.querySelector('[data-push-toggle]');
    const status = document.querySelector('[data-push-status]');
    if (!form || !button || !status) return;
    const key = form.dataset.publicKey;
    const t = name => form.dataset[name] || '';
    const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const setStatus = (text, enabled) => {
        status.textContent = text;
        button.textContent = enabled ? t('disable') : t('enable');
        button.dataset.enabled = enabled ? 'true' : 'false';
    };
    const post = async (handler, fields) => {
        const body = new FormData(form);
        Object.entries(fields).forEach(([name, value]) => body.set(name, value));
        const response = await fetch(`/Push?handler=${handler}`, { method: 'POST', body, credentials: 'same-origin' });
        if (!response.ok) throw new Error('request');
        const result = await response.json();
        if (!result.ok) throw new Error('request');
    };
    const decodeKey = value => {
        const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '='));
        return Uint8Array.from(raw, character => character.charCodeAt(0));
    };
    const refresh = async () => {
        if (!key || !supported) {
            setStatus(t('unsupported'), false);
            button.disabled = true;
            return;
        }
        const registration = await navigator.serviceWorker.register('/push-sw.js', { scope: '/' });
        const subscription = await registration.pushManager.getSubscription();
        if (subscription && Notification.permission === 'granted') {
            const details = subscription.toJSON();
            await post('Subscribe', { endpoint: details.endpoint, p256dh: details.keys.p256dh, auth: details.keys.auth });
            setStatus(t('enabled'), true);
        } else {
            setStatus(Notification.permission === 'denied' ? t('denied') : t('disabled'), false);
        }
    };
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            if (button.dataset.enabled === 'true') {
                const registration = await navigator.serviceWorker.ready;
                const existing = await registration.pushManager.getSubscription();
                if (existing) {
                    await post('Unsubscribe', { endpoint: existing.endpoint });
                    await existing.unsubscribe();
                }
                setStatus(t('disabled'), false);
            } else {
                const permission = await Notification.requestPermission();
                if (permission !== 'granted') {
                    setStatus(t('denied'), false);
                } else {
                    const registration = await navigator.serviceWorker.ready;
                    const existing = await registration.pushManager.getSubscription();
                    const subscription = existing || await registration.pushManager.subscribe({
                        userVisibleOnly: true,
                        applicationServerKey: decodeKey(key)
                    });
                    const details = subscription.toJSON();
                    await post('Subscribe', { endpoint: details.endpoint, p256dh: details.keys.p256dh, auth: details.keys.auth });
                    setStatus(t('enabled'), true);
                }
            }
        } catch {
            setStatus(t('error'), false);
        } finally {
            button.disabled = false;
        }
    });
    refresh().catch(() => setStatus(t('error'), false));
})();
