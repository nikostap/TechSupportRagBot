(() => {
    const list = document.getElementById('component-document-list');
    if (!list) return;
    const cards = [...list.querySelectorAll('[data-index-id]')];
    const active = status => status === 'Queued' || status === 'Processing';
    if (!cards.some(card => active(card.dataset.indexStatus))) return;

    const refresh = async () => {
        try {
            const response = await fetch(list.dataset.statusUrl, { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error('status');
            const states = await response.json();
            for (const state of states) {
                const card = cards.find(item => Number(item.dataset.indexId) === state.id);
                if (!card) continue;
                card.dataset.indexStatus = state.indexStatus;
                card.querySelector('[data-index-label]').textContent = state.statusText;
                const progress = card.querySelector('[data-index-progress]');
                const bar = card.querySelector('[data-index-bar]');
                const showProgress = state.indexable && (state.indexStatus === 'Processing' || state.indexStatus === 'Ready');
                if (progress) {
                    progress.hidden = !showProgress;
                    progress.textContent = `${list.dataset.progressLabel}: ${state.indexCompletedChunks} / ${state.indexTotalChunks}`;
                }
                if (bar) {
                    bar.hidden = !showProgress;
                    bar.max = Math.max(1, state.indexTotalChunks);
                    bar.value = state.indexCompletedChunks;
                }
                const error = card.querySelector('[data-index-error]');
                if (error) error.textContent = state.errorText || '';
                const indexButton = card.parentElement.querySelector('[data-index-action]');
                if (indexButton) indexButton.disabled = active(state.indexStatus);
                const publishButton = card.parentElement.querySelector('[data-publish-action]');
                if (publishButton) publishButton.disabled = !state.canPublish;
            }
        } catch {
            // Keep the last known state visible; the next poll can recover.
        }
        if (cards.some(card => active(card.dataset.indexStatus))) setTimeout(refresh, 3000);
    };
    setTimeout(refresh, 1000);
})();
