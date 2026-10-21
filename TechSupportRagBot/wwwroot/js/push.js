(() => {
    const form = document.querySelector('[data-push-form]');
    if (!form) return;

    const button = document.querySelector('[data-push-toggle]');
    const status = document.querySelector('[data-push-status]');
    const key = form.dataset.publicKey;
    const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    let registration;
    let currentSubscription;
    const t = name => form.dataset[name] || '';
    const setStatus = (text, enabled) => {
        if (!button || !status) return;
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
    const isEnabledForCurrentAccount = async endpoint => {
        const response = await fetch(`/Push?handler=Status&endpoint=${encodeURIComponent(endpoint)}`,
            { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error('request');
        return (await response.json()).enabled === true;
    };
    const decodeKey = value => {
        const normalized = value.replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '='));
        return Uint8Array.from(raw, character => character.charCodeAt(0));
    };
    const withTimeout = (promise, milliseconds) => Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), milliseconds))
    ]);
    const refresh = async () => {
        if (button) button.disabled = true;
        if (!key || !supported) {
            setStatus(t('unsupported'), false);
            return;
        }

        registration = await withTimeout(button
            ? navigator.serviceWorker.register('/push-sw.js', { scope: '/' })
            : navigator.serviceWorker.getRegistration('/'), 12000);
        currentSubscription = registration
            ? await withTimeout(registration.pushManager.getSubscription(), 12000) : null;

        if (currentSubscription) {
            const enabled = await isEnabledForCurrentAccount(currentSubscription.endpoint);
            if (!enabled) {
                const details = currentSubscription.toJSON();
                try {
                    await post('ReleaseOtherAccount', {
                        endpoint: details.endpoint, p256dh: details.keys.p256dh, auth: details.keys.auth
                    });
                } finally {
                    await currentSubscription.unsubscribe();
                    currentSubscription = null;
                }
            }
        }

        setStatus(currentSubscription && Notification.permission === 'granted'
            ? t('enabled') : Notification.permission === 'denied' ? t('denied') : t('disabled'),
            !!currentSubscription && Notification.permission === 'granted');
        if (button) button.disabled = false;
    };

    if (button) {
        button.addEventListener('click', async () => {
            button.disabled = true;
            try {
                if (button.dataset.enabled === 'true') {
                    if (currentSubscription) {
                        await post('Unsubscribe', { endpoint: currentSubscription.endpoint });
                        await currentSubscription.unsubscribe();
                        currentSubscription = null;
                    }
                    setStatus(t('disabled'), false);
                } else {
                    const permission = await Notification.requestPermission();
                    if (permission !== 'granted') {
                        setStatus(t('denied'), false);
                    } else {
                        currentSubscription = currentSubscription || await registration.pushManager.subscribe({
                            userVisibleOnly: true,
                            applicationServerKey: decodeKey(key)
                        });
                        const details = currentSubscription.toJSON();
                        await post('Subscribe', {
                            endpoint: details.endpoint, p256dh: details.keys.p256dh, auth: details.keys.auth
                        });
                        setStatus(t('enabled'), true);
                    }
                }
            } catch {
                setStatus(t('error'), false);
            } finally {
                button.disabled = false;
            }
        });
    }

    document.querySelectorAll('[data-push-logout]').forEach(logoutForm => {
        logoutForm.addEventListener('submit', async event => {
            if (!supported) return;
            event.preventDefault();
            let subscription;
            try {
                const registered = await withTimeout(navigator.serviceWorker.getRegistration('/'), 2500);
                subscription = registered ? await withTimeout(registered.pushManager.getSubscription(), 2500) : null;
                if (subscription) {
                    await withTimeout(post('Unsubscribe', { endpoint: subscription.endpoint }), 2500);
                }
            } catch {
                // Signing out remains available if the push service is offline.
            } finally {
                if (subscription) {
                    try { await withTimeout(subscription.unsubscribe(), 2500); } catch { /* Continue signing out. */ }
                }
                HTMLFormElement.prototype.submit.call(logoutForm);
            }
        });
    });

    refresh().catch(() => {
        setStatus(t('error'), false);
        if (button) button.disabled = true;
    });
})();
