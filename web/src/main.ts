import { render } from 'solid-js/web';
import { installHomeEntry } from './host/home';
import { installLinkInterceptor } from './host/links';
import { siblingAssetUrl } from './lib/url';
import { App } from './ui/App';
import './styles.css';

const bootFlag = '__jellyfinChronoLoaded';
const scriptUrl = (document.currentScript as HTMLScriptElement | null)?.src
  ?? document.querySelector<HTMLScriptElement>('script[src*="chrono.js"]')?.src;

function loadStylesheet(): void {
  if (import.meta.env.DEV || !scriptUrl) return;
  if (document.querySelector('link[data-chrono-css]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = siblingAssetUrl(scriptUrl, 'chrono.css');
  link.dataset.chronoCss = '';
  document.head.appendChild(link);
}

function start(): void {
  const host = document.createElement('div');
  host.id = 'chrono-host';
  document.body.appendChild(host);
  render(App, host);
  installLinkInterceptor();
  installHomeEntry();
}

const globalScope = window as unknown as Record<string, unknown>;
if (!globalScope[bootFlag]) {
  globalScope[bootFlag] = true;
  loadStylesheet();
  if (document.body) start();
  else document.addEventListener('DOMContentLoaded', start, { once: true });
}
