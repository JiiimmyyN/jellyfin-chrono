const headerSelector = '.MuiAppBar-root, .skinHeader';
const fallbackHeight = 56;

function isVisibleTopBar(element: Element): boolean {
  if (element.closest('.chrono-root')) return false;
  const style = getComputedStyle(element);
  if (style.display === 'none' || style.visibility === 'hidden') return false;
  if (style.position !== 'fixed' && style.position !== 'sticky') return false;
  const rect = element.getBoundingClientRect();
  return rect.height > 0 && rect.top <= 1 && rect.bottom > 0;
}

export function findHeader(): HTMLElement | undefined {
  const candidates = Array.from(document.querySelectorAll<HTMLElement>(headerSelector)).filter(isVisibleTopBar);
  candidates.sort((a, b) => b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom);
  return candidates[0];
}

export function measureHeaderHeight(): number {
  const header = findHeader();
  if (!header) return fallbackHeight;
  return Math.ceil(header.getBoundingClientRect().bottom);
}

export function watchHeaderHeight(onChange: (height: number) => void): () => void {
  let observed: HTMLElement | undefined;
  let last = -1;
  const resizeObserver = typeof ResizeObserver === 'function' ? new ResizeObserver(() => update()) : undefined;

  function update() {
    const header = findHeader();
    if (header !== observed) {
      if (observed) resizeObserver?.unobserve(observed);
      observed = header;
      if (header) resizeObserver?.observe(header);
    }
    const height = header ? Math.ceil(header.getBoundingClientRect().bottom) : fallbackHeight;
    if (height !== last) {
      last = height;
      onChange(height);
    }
  }

  update();
  const timers = [100, 400, 1200].map(delay => window.setTimeout(update, delay));
  const interval = window.setInterval(update, 2000);
  window.addEventListener('resize', update);
  return () => {
    timers.forEach(timer => window.clearTimeout(timer));
    window.clearInterval(interval);
    window.removeEventListener('resize', update);
    resizeObserver?.disconnect();
  };
}
