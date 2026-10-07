import { createSignal, Show, type JSX } from 'solid-js';
import { imageUrl } from '../lib/api';

export function Poster(props: { url?: string; title: string; subtitle?: string; alt?: string; eager?: boolean }): JSX.Element {
  const [failedUrl, setFailedUrl] = createSignal<string>();
  const [loadedUrl, setLoadedUrl] = createSignal<string>();
  const src = () => imageUrl(props.url);
  const usable = () => {
    const url = src();
    return url && url !== failedUrl() ? url : undefined;
  };
  return (
    <Show
      when={usable()}
      fallback={
        <div class="chrono-poster-fallback">
          <span class="chrono-poster-fallback-title">{props.title}</span>
          <Show when={props.subtitle}>
            <span class="chrono-poster-fallback-sub">{props.subtitle}</span>
          </Show>
        </div>
      }
    >
      {url => (
        <img
          class="chrono-poster-img"
          classList={{ 'chrono-loaded': loadedUrl() === url() }}
          src={url()}
          alt={props.alt ?? ''}
          loading={props.eager ? 'eager' : 'lazy'}
          decoding="async"
          draggable={false}
          onLoad={() => setLoadedUrl(url())}
          onError={() => setFailedUrl(url())}
        />
      )}
    </Show>
  );
}
