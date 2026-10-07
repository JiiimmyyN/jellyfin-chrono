import { createSignal, Show, type JSX } from 'solid-js';
import { imageUrl } from '../lib/api';
import { entryLabel, ownedCountLabel } from '../lib/labels';
import type { HubEntry } from '../types';
import { SvgIcon } from './icons';

export function Hero(props: {
  name: string;
  description?: string;
  backdropUrl?: string;
  owned: number;
  total: number;
  next?: HubEntry;
  started: boolean;
  canPlay: boolean;
  busy: boolean;
  onContinue(entry: HubEntry): void;
  onPlayFromStart(): void;
}): JSX.Element {
  const [loadedUrl, setLoadedUrl] = createSignal<string>();
  const src = () => imageUrl(props.backdropUrl);
  const ratio = () => (props.total > 0 ? Math.round((props.owned / props.total) * 100) : 0);

  return (
    <section class="chrono-hero">
      <div class="chrono-hero-media" aria-hidden="true">
        <Show when={src()}>
          {url => (
            <img
              class="chrono-hero-img"
              classList={{ 'chrono-loaded': loadedUrl() === url() }}
              src={url()}
              alt=""
              decoding="async"
              onLoad={() => setLoadedUrl(url())}
            />
          )}
        </Show>
        <div class="chrono-hero-shade" />
      </div>
      <div class="chrono-hero-body">
        <h1 class="chrono-hero-title">{props.name}</h1>
        <div class="chrono-hero-meta">
          <span>{ownedCountLabel(props.owned, props.total)}</span>
          <span class="chrono-meter" role="presentation">
            <span class="chrono-meter-fill" style={{ width: `${ratio()}%` }} />
          </span>
        </div>
        <Show when={props.description}>
          <p class="chrono-hero-desc">{props.description}</p>
        </Show>
        <Show when={props.canPlay}>
          <div class="chrono-hero-actions">
            <Show when={props.next}>
              {next => (
                <button
                  type="button"
                  class="chrono-btn chrono-btn-primary chrono-btn-hero"
                  disabled={props.busy}
                  onClick={() => props.onContinue(next())}
                >
                  <SvgIcon name="play" size={24} />
                  <span class="chrono-btn-stack">
                    <span class="chrono-btn-kicker">{props.started ? 'Continue' : 'Start'}</span>
                    <span class="chrono-btn-label">{entryLabel(next())}</span>
                  </span>
                </button>
              )}
            </Show>
            <Show when={props.started || !props.next}>
              <button
                type="button"
                class="chrono-btn chrono-btn-hero"
                classList={{ 'chrono-btn-primary': !props.next, 'chrono-btn-secondary': !!props.next }}
                disabled={props.busy}
                onClick={() => props.onPlayFromStart()}
              >
                <SvgIcon name="restart" size={22} />
                <span>Play timeline from start</span>
              </button>
            </Show>
          </div>
        </Show>
      </div>
    </section>
  );
}
