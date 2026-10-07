import { getOwner, onCleanup } from 'solid-js';
import { parseRoute } from '../lib/route';

const activationListeners = new Set<() => void>();
let currentTitle = 'Universes';
let titleTimer = 0;

export function subscribeActivation(listener: () => void): void {
  activationListeners.add(listener);
  if (getOwner()) onCleanup(() => activationListeners.delete(listener));
}

export function notifyActivation(): void {
  activationListeners.forEach(listener => listener());
}

export function applyPageTitle(): void {
  if (parseRoute(location.hash)) document.title = currentTitle;
}

export function setPageTitle(title: string): void {
  currentTitle = title;
  applyPageTitle();
  window.clearTimeout(titleTimer);
  titleTimer = window.setTimeout(applyPageTitle, 250);
}
