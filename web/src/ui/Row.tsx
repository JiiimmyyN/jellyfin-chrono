import { createEffect, createSignal, For, on, onCleanup, onMount, Show, type JSX } from 'solid-js';
import type { HubEntry, HubRow, RequestStatus } from '../types';
import { Card, type CardActions } from './Card';
import { SvgIcon } from './icons';

function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

let rowSequence = 0;

export function Row(props: {
  row: HubRow;
  entryIds: string[];
  entries: Record<string, HubEntry>;
  statuses: Record<string, RequestStatus>;
  serverId?: string;
  now: Date;
  playable: boolean;
  actions: CardActions;
  onPlayRow(row: HubRow): void;
}): JSX.Element {
  const infoId = `chrono-row-info-${++rowSequence}`;
  const [expanded, setExpanded] = createSignal(false);
  const [canPrev, setCanPrev] = createSignal(false);
  const [canNext, setCanNext] = createSignal(false);
  let rail: HTMLDivElement | undefined;
  let frame = 0;

  const updateEdges = () => {
    frame = 0;
    if (!rail) return;
    const max = rail.scrollWidth - rail.clientWidth;
    const position = Math.abs(rail.scrollLeft);
    setCanPrev(position > 4);
    setCanNext(position < max - 4);
  };
  const scheduleEdges = () => {
    if (!frame) frame = requestAnimationFrame(updateEdges);
  };

  const scrollByPage = (direction: 1 | -1) => {
    if (!rail) return;
    rail.scrollBy({ left: direction * rail.clientWidth * 0.85, behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
  };

  onMount(() => {
    updateEdges();
    const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(scheduleEdges) : undefined;
    if (rail) observer?.observe(rail);
    onCleanup(() => {
      observer?.disconnect();
      if (frame) cancelAnimationFrame(frame);
    });
  });

  createEffect(on(() => props.entryIds.length, scheduleEdges, { defer: true }));

  const hasInfo = () => !!(props.row.description || props.row.basis);

  return (
    <section class="chrono-row" aria-label={props.row.title}>
      <div class="chrono-row-head">
        <h2 class="chrono-row-title">{props.row.title}</h2>
        <span class="chrono-row-count">{props.entryIds.length}</span>
        <Show when={hasInfo()}>
          <button
            type="button"
            class="chrono-icon-btn chrono-row-info-btn"
            aria-expanded={expanded()}
            aria-controls={infoId}
            aria-label={`About ${props.row.title}`}
            onClick={() => setExpanded(value => !value)}
          >
            <SvgIcon name="info" size={18} />
          </button>
        </Show>
        <Show when={props.playable}>
          <button type="button" class="chrono-row-play" onClick={() => props.onPlayRow(props.row)}>
            <SvgIcon name="play" size={16} />
            <span>Play</span>
          </button>
        </Show>
      </div>
      <Show when={expanded()}>
        <div class="chrono-row-info" id={infoId}>
          <Show when={props.row.description}>
            <p>{props.row.description}</p>
          </Show>
          <Show when={props.row.basis}>
            <p class="chrono-row-basis">
              <span>Source:</span> {props.row.basis}
            </p>
          </Show>
        </div>
      </Show>
      <div class="chrono-rail-wrap">
        <div class="chrono-rail" ref={rail} onScroll={scheduleEdges}>
          <For each={props.entryIds}>
            {id => (
              <Card
                entry={props.entries[id]}
                status={props.statuses[id]}
                serverId={props.serverId}
                now={props.now}
                actions={props.actions}
              />
            )}
          </For>
        </div>
        <button
          type="button"
          class="chrono-rail-arrow chrono-rail-prev"
          classList={{ 'chrono-rail-arrow-hidden': !canPrev() }}
          tabIndex={-1}
          aria-label="Scroll left"
          onClick={() => scrollByPage(-1)}
        >
          <SvgIcon name="chevronLeft" size={32} />
        </button>
        <button
          type="button"
          class="chrono-rail-arrow chrono-rail-next"
          classList={{ 'chrono-rail-arrow-hidden': !canNext() }}
          tabIndex={-1}
          aria-label="Scroll right"
          onClick={() => scrollByPage(1)}
        >
          <SvgIcon name="chevronRight" size={32} />
        </button>
      </div>
    </section>
  );
}
