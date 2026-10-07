import { chronoApi, currentClient, userKey } from '../lib/api';
import { routeHash } from '../lib/route';
import type { UniverseSummary } from '../types';

const entryClass = 'chrono-home-entry';

let cachedFor: string | undefined;
let cached: Promise<UniverseSummary[]> | undefined;

function universesForCurrentUser(): Promise<UniverseSummary[]> | undefined {
  const client = currentClient();
  if (!client) return undefined;
  const key = userKey(client);
  if (cachedFor !== key || !cached) {
    cachedFor = key;
    cached = chronoApi.universes().catch(() => {
      cached = undefined;
      return [];
    });
  }
  return cached;
}

function link(href: string, text: string, primary: boolean): HTMLAnchorElement {
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.className = primary ? 'chrono-home-pill chrono-home-pill-primary' : 'chrono-home-pill';
  anchor.textContent = text;
  return anchor;
}

function buildEntry(universes: UniverseSummary[]): HTMLElement {
  const container = document.createElement('nav');
  container.className = `${entryClass} padded-left padded-right`;
  container.setAttribute('aria-label', 'Universes');
  container.append(link(routeHash({ view: 'gallery' }), 'Universes', true));
  for (const universe of universes.slice(0, 8)) {
    container.append(link(routeHash({ view: 'hub', universeId: universe.id }), universe.name, false));
  }
  return container;
}

function findHomeTab(): HTMLElement | null {
  const page = document.querySelector<HTMLElement>('#indexPage:not(.hide)');
  return page?.querySelector<HTMLElement>('#homeTab') ?? null;
}

let pending = false;

function ensureEntry(): void {
  pending = false;
  const homeTab = findHomeTab();
  if (!homeTab || homeTab.querySelector(`.${entryClass}`)) return;
  const sections = homeTab.querySelector('.sections');
  if (!sections) return;
  const universes = universesForCurrentUser();
  if (!universes) {
    window.setTimeout(schedule, 1000);
    return;
  }
  void universes.then(list => {
    if (list.length === 0) return;
    const tab = findHomeTab();
    if (!tab || tab.querySelector(`.${entryClass}`)) return;
    const target = tab.querySelector('.sections');
    if (target) tab.insertBefore(buildEntry(list), target);
  });
}

function schedule(): void {
  if (pending) return;
  pending = true;
  requestAnimationFrame(ensureEntry);
}

export function installHomeEntry(): () => void {
  const observer = new MutationObserver(schedule);
  observer.observe(document.body, { childList: true, subtree: true });
  window.addEventListener('hashchange', schedule);
  schedule();
  return () => {
    observer.disconnect();
    window.removeEventListener('hashchange', schedule);
  };
}
