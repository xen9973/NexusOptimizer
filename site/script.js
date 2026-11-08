const screen = document.querySelector('#demo-screen');
const demoWindow = document.querySelector('#demo-window');
const toast = document.querySelector('#toast');
const navItems = [...document.querySelectorAll('.demo-nav-item')];

const games = [
  { name: 'Hollow Knight', platform: 'Steam', letter: 'H', cover: 'cover-violet', genre: 'Приключение · Metroidvania' },
  { name: 'Cyberpunk 2077', platform: 'Steam', letter: 'C', cover: 'cover-orange', genre: 'RPG · Открытый мир' },
  { name: 'Hades', platform: 'Epic Games', letter: 'H', cover: 'cover-teal', genre: 'Экшен · Roguelike' },
  { name: 'Control', platform: 'Добавленная папка', letter: 'C', cover: 'cover-blue', genre: 'Экшен · Приключение' },
];

const cleanupItems = [
  ['user-temp', 'Временные файлы пользователя', 'Файлы старше суток в папке TEMP', '142 МБ'],
  ['windows-temp', 'Временные файлы Windows', 'Часть файлов может быть занята системой', '86 МБ'],
  ['shader-cache', 'Кэш шейдеров видеокарты', 'Кэш DirectX, NVIDIA и AMD', '318 МБ'],
  ['crash-dumps', 'Дампы после сбоев', 'Файлы отчётов о сбоях приложений', '24 МБ'],
  ['recycle-bin', 'Корзина', 'Удаление из корзины необратимо', '—'],
];

const tweakGroups = {
  'Производительность': [
    ['Отключить анимацию окон', 'Визуальная настройка интерфейса Windows'],
    ['Уменьшить задержку меню', 'Меню Windows будут открываться быстрее'],
    ['Отключить эффекты прозрачности', 'Меняется только оформление Windows'],
    ['Включить схему «Высокая производительность»', 'Может увеличить энергопотребление'],
  ],
  'Игры': [
    ['Включить игровой режим', 'Поведение зависит от Windows и игры'],
    ['Отключить фоновую запись игр', 'Фоновые клипы Xbox Game Bar не записываются'],
    ['Настроить профиль отдельной игры', 'Профиль действует только для выбранной игры'],
  ],
  'Приватность': [
    ['Отключить рекламный идентификатор', 'Для приложений текущего пользователя'],
    ['Скрыть предложения в меню «Пуск»', 'Меняется содержимое меню «Пуск»'],
  ],
  'Автозагрузка': [
    ['Изменить запуск выбранных служб', 'Некоторым службам может потребоваться перезагрузка'],
    ['Не перезапускать приложения после входа', 'Открытые приложения не будут восстановлены автоматически'],
  ],
  'Питание': [
    ['Включить схему «Высокая производительность»', 'Возможны дополнительный нагрев и расход батареи'],
    ['Отключить энергосбережение фоновых процессов', 'Может потребоваться перезагрузка'],
  ],
  'Проводник': [
    ['Показывать расширения файлов', 'Например, .txt и .exe'],
    ['Показывать скрытые файлы', 'Не удаляйте системные файлы без необходимости'],
  ],
};

let activePage = 'library';
let activeFilter = 'Все';
let favorites = new Set(['Hollow Knight']);
let activeTweakGroup = 'Производительность';
let toastTimer;

function showToast(message) {
  toast.textContent = message;
  toast.classList.add('is-visible');
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => toast.classList.remove('is-visible'), 2800);
}

function setPage(page) {
  activePage = page;
  navItems.forEach((item) => {
    const active = item.dataset.page === page;
    item.classList.toggle('is-active', active);
    item.setAttribute('aria-selected', String(active));
  });
  renderPage();
}

function renderLibrary() {
  screen.innerHTML = `
    <div class="screen-heading">
      <div><h3>Библиотека</h3><p class="screen-subtitle">Найденные игры в одном месте</p></div>
      <button class="screen-action" type="button" data-action="scan-games">Сканировать игры</button>
    </div>
    <input class="demo-search" type="search" placeholder="Поиск по названию" aria-label="Поиск по играм">
    <div class="demo-filters" aria-label="Фильтр библиотеки">
      ${['Все', 'Steam', 'Epic Games', 'Избранное'].map((filter) => `<button class="chip ${activeFilter === filter ? 'is-active' : ''}" type="button" data-filter="${filter}">${filter}</button>`).join('')}
    </div>
    <div class="game-grid" id="game-grid"></div>`;
  renderGames();
}

function renderGames(query = '') {
  const grid = screen.querySelector('#game-grid');
  if (!grid) return;
  const normalized = query.trim().toLocaleLowerCase('ru');
  const visibleGames = games.filter((game) => {
    const matchesText = game.name.toLocaleLowerCase('ru').includes(normalized);
    const matchesFilter = activeFilter === 'Все'
      || (activeFilter === 'Избранное' ? favorites.has(game.name) : game.platform === activeFilter);
    return matchesText && matchesFilter;
  });

  grid.innerHTML = visibleGames.length ? visibleGames.map((game) => `
    <article class="game-card" tabindex="0" role="button" aria-label="Открыть игру ${game.name}" data-game="${game.name}">
      <span class="game-cover ${game.cover}">${game.letter}</span>
      <span><span class="game-name">${game.name}</span><span class="game-platform">${game.platform}</span></span>
      <button class="favorite-button ${favorites.has(game.name) ? 'is-favorite' : ''}" type="button" aria-label="${favorites.has(game.name) ? 'Убрать из избранного' : 'В избранное'}" data-favorite="${game.name}">★</button>
    </article>`).join('') : '<p class="demo-empty">Игры не найдены. Попробуйте изменить поиск или фильтр.</p>';
}

function renderGameDetails(name) {
  const game = games.find((item) => item.name === name);
  if (!game) return;
  screen.innerHTML = `
    <div class="screen-heading"><div><h3>Карточка игры</h3><p class="screen-subtitle">Информация и игровой профиль</p></div></div>
    <div class="detail-panel">
      <button class="back-button" type="button" data-action="back-library">← Назад в библиотеку</button>
      <h4>${game.name}</h4>
      <p>${game.genre}<br>Источник: ${game.platform}. Здесь можно открыть папку игры и настроить индивидуальный профиль.</p>
      <div class="demo-stats"><div class="demo-stat"><small>Профиль</small><b>Стандартный</b></div><div class="demo-stat"><small>Статус</small><b>Найдено в библиотеке</b></div></div>
      <div class="demo-controls">
        <button class="screen-action" type="button" data-action="launch-game">Запустить игру</button>
        <button class="screen-action secondary" type="button" data-action="game-settings">Настройки игры</button>
      </div>
    </div>`;
}

function renderCleanup() {
  screen.innerHTML = `
    <div class="screen-heading"><div><h3>Очистка ПК</h3><p class="screen-subtitle">Выберите категории для анализа</p></div><button class="screen-action" type="button" data-action="analyze">Проанализировать</button></div>
    <p class="demo-warning">В настоящем приложении перед удалением можно проверить результат. Свежие, занятые, скрытые и системные файлы защищены от очистки.</p>
    <div class="cleanup-list">${cleanupItems.map(([id, title, description, size]) => `
      <label class="cleanup-row"><input class="check-demo" type="checkbox" data-cleanup="${id}" ${id !== 'recycle-bin' ? 'checked' : ''}>
        <span class="row-copy"><b>${title}</b><small>${description}</small></span><span class="row-value">${size}</span>
      </label>`).join('')}</div>
    <div class="demo-controls"><button class="screen-action" type="button" data-action="clean">Очистить выбранное</button><button class="screen-action secondary" type="button" data-action="cleanup-log">Журнал</button></div>
    <p class="demo-status" id="screen-status"></p>`;
}

function renderTweaks() {
  const groupButtons = Object.keys(tweakGroups).map((group) => `<button class="chip ${activeTweakGroup === group ? 'is-active' : ''}" type="button" data-group="${group}">${group}</button>`).join('');
  const entries = tweakGroups[activeTweakGroup].map(([title, description], index) => `
    <label class="tweak-row"><input class="check-demo" type="checkbox" data-tweak="${index}">
      <span class="row-copy"><b>${title}</b><small>${description}</small></span><span class="row-value">Обратимо</span>
    </label>`).join('');
  screen.innerHTML = `
    <div class="screen-heading"><div><h3>Настройка компьютера</h3><p class="screen-subtitle">Общие параметры Windows по категориям</p></div><button class="screen-action" type="button" data-action="apply-tweaks">Применить</button></div>
    <p class="demo-warning">Пресеты и выбор настроек ничего не меняют до применения. Для системных изменений могут потребоваться права администратора.</p>
    <div class="category-line">${groupButtons}</div>
    <div class="preset-row"><span class="preset-label">Пресет:</span>${['Сбалансированный', 'Производительность', 'Приватность'].map((name) => `<button class="chip" type="button" data-preset="${name}">${name}</button>`).join('')}</div>
    <div class="tweak-list">${entries}</div><p class="demo-status" id="screen-status"></p>`;
}

function renderScan() {
  screen.innerHTML = `
    <div class="screen-heading"><div><h3>Умная настройка компьютера</h3><p class="screen-subtitle">Сканирование и подбор обратимых настроек</p></div><button class="screen-action" type="button" data-action="scan-pc">Сканировать компьютер</button></div>
    <p class="demo-warning">Результаты ниже — пример интерфейса, а не данные вашего компьютера. Сканирование в программе само по себе ничего не меняет.</p>
    <div class="scan-layout">
      <div class="spec-panel"><h4>Характеристики</h4>
        <div class="spec-item"><small>Windows</small><b>Определяется при сканировании</b></div>
        <div class="spec-item"><small>Процессор</small><b>Данные вашего ПК</b></div>
        <div class="spec-item"><small>Оперативная память</small><b>Данные вашего ПК</b></div>
        <div class="spec-item"><small>Графика</small><b>Данные вашего ПК</b></div>
      </div>
      <div class="spec-panel"><h4>Пример рекомендаций</h4>
        <div class="recommendation-list">
          <div class="recommendation-row"><span>⌁</span><div class="row-copy"><b>Проверить игровые настройки</b><small>Посмотрите параметры и выберите только подходящие.</small></div></div>
          <div class="recommendation-row"><span>✓</span><div class="row-copy"><b>Настроить Windows</b><small>Рекомендации не гарантируют прирост FPS.</small></div></div>
        </div>
        <div class="demo-controls"><button class="screen-action secondary" type="button" data-action="open-tweaks">Посмотреть настройки</button></div>
      </div>
    </div><p class="demo-status" id="screen-status"></p>`;
}

function renderPage() {
  if (activePage === 'library') renderLibrary();
  if (activePage === 'cleanup') renderCleanup();
  if (activePage === 'tweaks') renderTweaks();
  if (activePage === 'scan') renderScan();
}

navItems.forEach((item) => item.addEventListener('click', () => setPage(item.dataset.page)));

screen.addEventListener('input', (event) => {
  if (event.target.matches('.demo-search')) renderGames(event.target.value);
});

screen.addEventListener('change', (event) => {
  if (event.target.matches('[data-tweak]')) {
    const count = screen.querySelectorAll('[data-tweak]:checked').length;
    const status = screen.querySelector('#screen-status');
    if (status) status.textContent = count ? `Выбрано настроек: ${count}. Пока это только демонстрация.` : '';
  }
});

screen.addEventListener('click', (event) => {
  const target = event.target.closest('button, [data-game]');
  if (!target) return;

  if (target.dataset.filter) {
    activeFilter = target.dataset.filter;
    renderLibrary();
    return;
  }
  if (target.dataset.favorite) {
    const name = target.dataset.favorite;
    favorites.has(name) ? favorites.delete(name) : favorites.add(name);
    renderLibrary();
    return;
  }
  if (target.dataset.game) {
    renderGameDetails(target.dataset.game);
    return;
  }
  if (target.dataset.group) {
    activeTweakGroup = target.dataset.group;
    renderTweaks();
    return;
  }
  if (target.dataset.preset) {
    screen.querySelectorAll('[data-tweak]').forEach((input, index) => { input.checked = index === 0 || target.dataset.preset === 'Производительность'; });
    const status = screen.querySelector('#screen-status');
    if (status) status.textContent = `Выбран пресет «${target.dataset.preset}». Это демо — настройки Windows не изменяются.`;
    return;
  }

  const action = target.dataset.action;
  if (action === 'back-library') renderLibrary();
  if (action === 'scan-games') showToast('Демо: библиотека показывает примеры игр, сканирование ПК не запускается.');
  if (action === 'launch-game') showToast('Демо: запуск игры доступен только в приложении для Windows.');
  if (action === 'game-settings') {
    activePage = 'tweaks';
    activeTweakGroup = 'Игры';
    navItems.forEach((item) => item.classList.toggle('is-active', item.dataset.page === 'tweaks'));
    navItems.forEach((item) => item.setAttribute('aria-selected', String(item.dataset.page === 'tweaks')));
    renderTweaks();
    showToast('Откройте настройки профиля игры в приложении.');
  }
  if (action === 'analyze') {
    const count = screen.querySelectorAll('[data-cleanup]:checked').length;
    const status = screen.querySelector('#screen-status');
    status.textContent = count ? `Демо-анализ: выбрано категорий — ${count}. Размеры показаны для примера.` : 'Выберите хотя бы одну категорию для анализа.';
  }
  if (action === 'clean') {
    const count = screen.querySelectorAll('[data-cleanup]:checked').length;
    showToast(count ? 'Это демонстрация: файлы на компьютере не удалялись.' : 'Сначала выберите категории для очистки.');
  }
  if (action === 'cleanup-log') showToast('Журнал очистки доступен в приложении для Windows.');
  if (action === 'apply-tweaks') {
    const count = screen.querySelectorAll('[data-tweak]:checked').length;
    showToast(count ? 'Демо: настройки Windows не применялись.' : 'Выберите настройки перед применением.');
  }
  if (action === 'scan-pc') {
    const status = screen.querySelector('#screen-status');
    status.textContent = 'Пример: в приложении здесь отобразятся характеристики вашего компьютера и подходящие рекомендации.';
  }
  if (action === 'open-tweaks') setPage('tweaks');
});

document.querySelector('#theme-toggle').addEventListener('click', () => {
  demoWindow.classList.toggle('light-demo');
});

const mobileMenuButton = document.querySelector('.mobile-menu-button');
const siteNav = document.querySelector('#site-nav');
mobileMenuButton.addEventListener('click', () => {
  const isOpen = siteNav.classList.toggle('is-open');
  mobileMenuButton.setAttribute('aria-expanded', String(isOpen));
});
siteNav.addEventListener('click', (event) => {
  if (event.target.closest('a')) {
    siteNav.classList.remove('is-open');
    mobileMenuButton.setAttribute('aria-expanded', 'false');
  }
});

renderPage();
