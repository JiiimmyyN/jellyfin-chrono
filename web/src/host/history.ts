export const locationChangeEvent = 'chrono:locationchange';

let installed = false;

export function watchHistory(): void {
  if (installed) return;
  installed = true;
  for (const method of ['pushState', 'replaceState'] as const) {
    const original = history[method];
    history[method] = function (this: History, ...args: Parameters<History['pushState']>) {
      const result = original.apply(this, args);
      window.dispatchEvent(new Event(locationChangeEvent));
      return result;
    };
  }
}
