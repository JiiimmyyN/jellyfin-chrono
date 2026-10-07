const nativeCreateElement = document.createElement.bind(document);
document.createElement = ((tagName: string, options?: ElementCreationOptions) => {
  if (options && typeof options === 'object' && 'is' in options) {
    (options.is as unknown as string).toLowerCase();
  }
  return nativeCreateElement(tagName, options);
}) as typeof document.createElement;

function appBar(): HTMLElement {
  const bar = document.createElement('header');
  bar.className = 'MuiAppBar-root';
  bar.innerHTML = `
    <button type="button" id="fake-menu" aria-label="Menu">☰</button>
    <a href="#/home">Home</a>
    <a href="#/chrono" target="_blank" rel="noopener noreferrer">Universes</a>
    <span class="fake-spacer"></span>
    <button type="button" onclick="history.back()">Back</button>`;
  return bar;
}

function drawer(): HTMLElement {
  const modal = document.createElement('div');
  modal.className = 'MuiModal-root MuiModal-hidden';
  modal.innerHTML = `
    <div class="MuiBackdrop-root"></div>
    <nav class="fake-drawer">
      <a href="#/home">Home</a>
      <a href="#/chrono" target="_blank" rel="noopener noreferrer">Universes</a>
    </nav>`;
  modal.querySelector('.MuiBackdrop-root')!.addEventListener('click', () => modal.classList.add('MuiModal-hidden'));
  return modal;
}

function renderPage(root: HTMLElement): void {
  const hash = location.hash || '#/home';
  if (hash.startsWith('#/home')) {
    root.innerHTML = `
      <div id="indexPage" class="page homePage">
        <div class="tabContent pageTabContent" id="homeTab" data-index="0">
          <div class="sections">
            <div class="fake-section"><h2>Continue Watching</h2><div class="fake-section-row"><div></div><div></div><div></div></div></div>
            <div class="fake-section"><h2>Latest Movies</h2><div class="fake-section-row"><div></div><div></div><div></div><div></div></div></div>
          </div>
        </div>
      </div>`;
  } else if (hash.startsWith('#/details')) {
    const id = new URLSearchParams(hash.split('?')[1]).get('id');
    root.innerHTML = `<div id="itemDetailPage" class="page"><h1>Item details</h1><p>Jellyfin would show item <code>${id}</code> here.</p><a href="#/chrono/mcu" style="color:#00a4dc">Back to MCU</a></div>`;
  } else {
    root.innerHTML = `<div id="fallbackPage" class="page"><h1>Page not found</h1><p>This is what jellyfin-web renders under the overlay.</p></div>`;
  }
}

export function installFakeHost(): void {
  const root = document.getElementById('reactRoot')!;
  const modal = drawer();
  document.body.prepend(appBar());
  document.body.append(modal);
  document.getElementById('fake-menu')!.addEventListener('click', () => modal.classList.remove('MuiModal-hidden'));
  if (!location.hash) history.replaceState(null, '', '#/chrono');
  renderPage(root);
  window.addEventListener('hashchange', () => renderPage(root));
}
