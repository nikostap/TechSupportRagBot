(() => {
    const closeHelp = except => {
        for (const owner of document.querySelectorAll('[data-help].component-help-open')) {
            if (owner === except) continue;
            owner.classList.remove('component-help-open');
            owner.querySelector('.component-help-trigger')?.setAttribute('aria-expanded', 'false');
        }
    };

    for (const owner of document.querySelectorAll('.component-form label[data-help], .component-form fieldset[data-help], .component-template-form label[data-help], .component-item summary[data-help]')) {
        let target = owner;
        if (owner.tagName === 'LABEL' || owner.tagName === 'FIELDSET') {
            const wrapper = document.createElement('div');
            wrapper.className = 'component-help-field';
            wrapper.dataset.help = owner.dataset.help;
            owner.before(wrapper);
            wrapper.append(owner);
            owner.removeAttribute('data-help');
            target = wrapper;
        }
        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'component-help-trigger';
        trigger.textContent = '?';
        trigger.setAttribute('aria-label', target.dataset.help);
        trigger.setAttribute('aria-expanded', 'false');
        trigger.addEventListener('click', event => {
            event.preventDefault();
            event.stopPropagation();
            const open = !target.classList.contains('component-help-open');
            closeHelp(target);
            target.classList.toggle('component-help-open', open);
            trigger.setAttribute('aria-expanded', String(open));
        });
        target.append(trigger);
    }

    document.addEventListener('click', () => closeHelp());
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeHelp();
    });
})();
