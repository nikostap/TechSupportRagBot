(() => {
    function createElement(tag, className, text) {
        const element = document.createElement(tag);
        element.className = className;
        if (text != null) element.textContent = text;
        return element;
    }

    function setupWorkspace(list, title) {
        const workspace = createElement('div', 'focus-list-workspace');
        const nav = createElement('nav', 'focus-list-nav');
        nav.setAttribute('aria-label', title);
        const heading = createElement('div', 'focus-list-nav-heading', title);
        nav.append(heading);
        list.before(workspace);
        workspace.append(nav, list);
        list.classList.add('focus-list-pane');
        list.setAttribute('tabindex', '0');
        return { workspace, nav, heading };
    }

    function addButton(nav, label, count, selected) {
        const button = createElement('button', 'focus-list-button', null);
        button.type = 'button';
        button.append(createElement('span', 'focus-list-button-label', label));
        if (count != null) button.append(createElement('span', 'focus-list-count', count));
        button.setAttribute('aria-current', selected ? 'true' : 'false');
        nav.append(button);
        return button;
    }

    function addPaneHeading(pane, overline, title, count) {
        const heading = createElement('div', 'focus-list-pane-heading');
        const main = createElement('div', 'focus-list-pane-heading-main');
        main.append(createElement('span', 'focus-list-overline', overline));
        main.append(createElement('h3', '', title));
        heading.append(main);
        if (count != null) heading.append(createElement('span', 'focus-list-pane-count', count));
        pane.prepend(heading);
        return heading;
    }

    function makeSelector(nav, pane, items) {
        let active = 0;
        const select = (index) => {
            active = index;
            items.forEach((item, itemIndex) => {
                item.button.setAttribute('aria-current', String(index === itemIndex));
                item.show(false);
            });
            items[index].show(true);
            pane.scrollTop = 0;
        };
        items.forEach((item, index) => item.button.addEventListener('click', () => select(index)));
        if (items.length) select(active);
    }

    function setupQA() {
        const list = document.getElementById('qa-entry-list');
        if (!list) return;
        const models = [...list.querySelectorAll(':scope > [data-model-group]')];
        if (!models.length) return;
        const { nav } = setupWorkspace(list, 'Модели и номера');
        list.classList.add('focus-qa-list');
        const items = [];
        models.forEach(model => {
            const modelName = model.dataset.model || 'Не указана';
            const serials = [...model.querySelectorAll(':scope > .qa-model-group-body > [data-serial-group]')];
            [model, ...serials].forEach(group => group.querySelector(':scope > summary')?.addEventListener('click', event => {
                if (!(event.target instanceof HTMLInputElement)) event.preventDefault();
            }));
            const label = createElement('div', 'focus-list-nav-group', modelName);
            nav.append(label);
            serials.forEach(serial => {
                const serialName = serial.dataset.serial || 'Не указан';
                const count = serial.querySelectorAll('.qa-select-entry').length;
                const button = addButton(nav, `№ ${serialName}`, String(count), false);
                button.querySelector('.focus-list-button-label').prepend(createElement('span', 'focus-list-mobile-model', `${modelName} · `));
                model.open = true;
                serial.open = true;
                const item = {
                    button,
                    show(selected) {
                        serial.hidden = !selected;
                        if (!selected) model.hidden = [...model.querySelectorAll('[data-serial-group]')].every(part => part.hidden);
                        if (selected) {
                            model.hidden = false;
                            list.querySelector('.focus-list-pane-heading')?.remove();
                            addPaneHeading(list, modelName, `№ ${serialName}`, `${count} QA`);
                        }
                    }
                };
                items.push(item);
            });
        });
        makeSelector(nav, list, items);
    }

    function setupClientTickets() {
        const list = document.querySelector('.ticket-group-list');
        if (!list || !list.querySelector('.client-ticket-machine-group')) return;
        const groups = [...list.querySelectorAll(':scope > .client-ticket-machine-group')];
        const { nav } = setupWorkspace(list, 'Оборудование');
        list.classList.add('focus-client-list');
        const items = groups.map(group => {
            const name = group.querySelector('.machine-summary h2')?.textContent.trim() || 'Оборудование';
            const count = group.querySelectorAll('.ticket-card').length;
            const button = addButton(nav, name, String(count), false);
            group.open = true;
            return {
                button,
                show(selected) {
                    group.hidden = !selected;
                    if (selected) {
                        list.querySelector('.focus-list-pane-heading')?.remove();
                        addPaneHeading(list, 'Оборудование', name, `${count} обращений`);
                    }
                }
            };
        });
        makeSelector(nav, list, items);
    }

    function setupDashboard() {
        document.querySelectorAll('.dashboard-machine-list').forEach(list => {
            const groups = [...list.querySelectorAll(':scope > .dashboard-machine-group')];
            if (!groups.length) return;
            const { nav } = setupWorkspace(list, 'Оборудование');
            list.classList.add('focus-dashboard-list');
            const items = groups.map(group => {
                const name = group.querySelector('.dashboard-machine-title strong')?.textContent.trim() || 'Оборудование';
                const count = group.querySelectorAll('.focus-dashboard-record').length;
                const button = addButton(nav, name, String(count), false);
                group.open = true;
                return {
                    button,
                    show(selected) {
                        group.hidden = !selected;
                        if (selected) {
                            list.querySelector('.focus-list-pane-heading')?.remove();
                            addPaneHeading(list, 'Обращения', name, `${count}`);
                        }
                    }
                };
            });
            makeSelector(nav, list, items);
        });
    }

    function setupIndexedChats() {
        const list = document.querySelector('.indexed-chat-list');
        if (!list) return;
        const cards = [...list.querySelectorAll(':scope > .indexed-chat-card')];
        if (!cards.length) return;
        const { nav } = setupWorkspace(list, 'Решения');
        list.classList.add('focus-indexed-list');
        const groups = [
            { label: 'Ожидают проверки', cards: cards.filter(card => card.querySelector('.status-pill.warning')) },
            { label: 'В индексе', cards: cards.filter(card => !card.querySelector('.status-pill.warning')) }
        ].filter(group => group.cards.length);
        const items = groups.map(group => {
            const button = addButton(nav, group.label, String(group.cards.length), false);
            return {
                button,
                show(selected) {
                    group.cards.forEach(card => card.hidden = !selected);
                    if (selected) {
                        list.querySelector('.focus-list-pane-heading')?.remove();
                        addPaneHeading(list, 'Решения', group.label, `${group.cards.length}`);
                    }
                }
            };
        });
        makeSelector(nav, list, items);
    }

    function setupBotTest() {
        const list = document.querySelector('.bot-test-chunk-list');
        if (!list) return;
        const chunks = [...list.querySelectorAll(':scope > .qa-compact-card')];
        if (!chunks.length) return;
        const { nav } = setupWorkspace(list, 'Найденные фрагменты');
        list.classList.add('focus-bot-list');
        const items = chunks.map((chunk, index) => {
            const name = chunk.querySelector('.qa-summary-main strong')?.textContent.trim() || `Фрагмент ${index + 1}`;
            const button = addButton(nav, name, null, false);
            return {
                button,
                show(selected) {
                    chunk.hidden = !selected;
                    if (selected) {
                        list.querySelector('.focus-list-pane-heading')?.remove();
                        addPaneHeading(list, 'Выбранный фрагмент', name, `${index + 1} / ${chunks.length}`);
                    }
                }
            };
        });
        makeSelector(nav, list, items);
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('details').forEach(detail => { detail.open = false; });
        setupQA();
        setupClientTickets();
        setupDashboard();
        setupIndexedChats();
        setupBotTest();
    });
})();
