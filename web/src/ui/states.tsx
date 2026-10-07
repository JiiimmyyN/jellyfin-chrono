import { For, Show, type JSX } from 'solid-js';

export function StateMessage(props: {
  title: string;
  text?: string;
  actionLabel?: string;
  onAction?: () => void;
  linkLabel?: string;
  linkHref?: string;
}): JSX.Element {
  return (
    <div class="chrono-state">
      <h2 class="chrono-state-title">{props.title}</h2>
      <Show when={props.text}>
        <p class="chrono-state-text">{props.text}</p>
      </Show>
      <div class="chrono-state-actions">
        <Show when={props.actionLabel && props.onAction}>
          <button type="button" class="chrono-btn chrono-btn-primary" onClick={() => props.onAction?.()}>
            {props.actionLabel}
          </button>
        </Show>
        <Show when={props.linkLabel && props.linkHref}>
          <a class="chrono-btn chrono-btn-secondary" href={props.linkHref}>{props.linkLabel}</a>
        </Show>
      </div>
    </div>
  );
}

const placeholderCards = Array.from({ length: 9 }, (_, index) => index);

export function RowSkeleton(): JSX.Element {
  return (
    <div class="chrono-row" aria-hidden="true">
      <div class="chrono-row-head">
        <div class="chrono-skel chrono-skel-title" />
      </div>
      <div class="chrono-rail chrono-rail-static">
        <For each={placeholderCards}>
          {() => (
            <div class="chrono-card">
              <div class="chrono-skel chrono-skel-poster" />
              <div class="chrono-skel chrono-skel-line" />
            </div>
          )}
        </For>
      </div>
    </div>
  );
}

export function HubSkeleton(props: { withHero: boolean }): JSX.Element {
  return (
    <div class="chrono-hub" aria-busy="true" aria-label="Loading">
      <Show when={props.withHero}>
        <div class="chrono-hero chrono-hero-skeleton">
          <div class="chrono-hero-body">
            <div class="chrono-skel chrono-skel-heading" />
            <div class="chrono-skel chrono-skel-text" />
            <div class="chrono-skel chrono-skel-text chrono-skel-short" />
            <div class="chrono-skel chrono-skel-button" />
          </div>
        </div>
      </Show>
      <RowSkeleton />
      <RowSkeleton />
    </div>
  );
}

export function GallerySkeleton(): JSX.Element {
  return (
    <div class="chrono-tiles" aria-busy="true" aria-label="Loading">
      <For each={[0, 1, 2]}>{() => <div class="chrono-tile chrono-skel" />}</For>
    </div>
  );
}
