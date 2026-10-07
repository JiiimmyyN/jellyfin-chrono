import { createSignal, For, type JSX } from 'solid-js';

interface Toast {
  id: number;
  text: string;
  tone: 'info' | 'error';
}

const [toasts, setToasts] = createSignal<Toast[]>([]);
let nextId = 1;

export function showToast(text: string, tone: Toast['tone'] = 'info'): void {
  const id = nextId++;
  setToasts(list => [...list.slice(-2), { id, text, tone }]);
  window.setTimeout(() => setToasts(list => list.filter(toast => toast.id !== id)), tone === 'error' ? 6000 : 3500);
}

export function Toasts(): JSX.Element {
  return (
    <div class="chrono-toasts" role="status" aria-live="polite">
      <For each={toasts()}>
        {toast => <div class="chrono-toast" classList={{ 'chrono-toast-error': toast.tone === 'error' }}>{toast.text}</div>}
      </For>
    </div>
  );
}
