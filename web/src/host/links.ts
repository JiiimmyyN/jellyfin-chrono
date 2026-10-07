import { chronoHashFromHref, isChronoHref } from '../lib/route';

export function closeHostMenus(): void {
  document
    .querySelectorAll<HTMLElement>('.MuiModal-root:not(.MuiModal-hidden) .MuiBackdrop-root, .tmla-mask.backdrop:not(.hide)')
    .forEach(backdrop => {
      if (!backdrop.closest('.chrono-root')) backdrop.click();
    });
}

export function navigateTo(hash: string): void {
  if (location.hash === hash) return;
  location.hash = hash;
}

function onDocumentClick(event: MouseEvent): void {
  if (event.defaultPrevented || event.button !== 0) return;
  if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  const target = event.target instanceof Element ? event.target : null;
  const anchor = target?.closest('a[href]');
  if (!anchor || anchor.closest('.chrono-root')) return;
  const href = anchor.getAttribute('href');
  if (!isChronoHref(href)) return;
  const hash = chronoHashFromHref(href!);
  event.preventDefault();
  event.stopPropagation();
  closeHostMenus();
  window.setTimeout(() => navigateTo(hash), 0);
}

export function installLinkInterceptor(): () => void {
  document.addEventListener('click', onDocumentClick, true);
  return () => document.removeEventListener('click', onDocumentClick, true);
}
