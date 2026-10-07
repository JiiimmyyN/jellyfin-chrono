import { Show, type JSX } from 'solid-js';
import { comingLabel, displayYear } from '../lib/dates';
import { entryLabel, isInProgress, requestStatusLabel } from '../lib/labels';
import { detailsHash } from '../lib/route';
import type { HubEntry, RequestStatus } from '../types';
import { SvgIcon } from './icons';
import { Poster } from './Poster';

export interface CardActions {
  open(entry: HubEntry): void;
}

function finePointer(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(hover: hover) and (pointer: fine)').matches;
}

export function entryMeta(entry: HubEntry): string {
  const year = displayYear(entry.released, entry.year);
  return [entry.subtitle, year].filter(Boolean).join(' · ');
}

export function Card(props: {
  entry: HubEntry;
  status?: RequestStatus;
  serverId?: string;
  now: Date;
  actions: CardActions;
}): JSX.Element {
  const entry = () => props.entry;
  const owned = () => entry().owned && !!entry().itemId;
  const coming = () => comingLabel(entry().released, entry().year, props.now);
  const statusLabel = () => (entry().owned ? undefined : requestStatusLabel(props.status));
  const footLabel = () => coming() ?? (entry().owned ? undefined : 'Not in library');
  const description = () => {
    const parts = [entryLabel(entry()), String(displayYear(entry().released, entry().year) ?? '')];
    if (entry().played) parts.push('watched');
    else if (isInProgress(entry().progress)) parts.push(`${Math.round(entry().progress! * 100)}% watched`);
    if (statusLabel()) parts.push(statusLabel()!);
    if (footLabel()) parts.push(footLabel()!);
    return parts.filter(Boolean).join(', ');
  };

  const onContextMenu = (event: MouseEvent) => {
    const pointerType = (event as PointerEvent).pointerType;
    if (pointerType === 'mouse' || (!pointerType && finePointer())) return;
    event.preventDefault();
    props.actions.open(entry());
  };

  const content = () => (
    <>
      <div class="chrono-poster">
        <Poster url={entry().posterUrl} title={entry().title} subtitle={entry().subtitle} />
        <Show when={entry().played}>
          <span class="chrono-badge-watched" title="Watched">
            <SvgIcon name="check" size={16} />
          </span>
        </Show>
        <Show when={!entry().played && entry().owned && entry().type !== 'movie' && (entry().unplayedCount ?? 0) > 0}>
          <span class="chrono-badge-count" title={`${entry().unplayedCount} unwatched episodes`}>
            {entry().unplayedCount}
          </span>
        </Show>
        <Show when={statusLabel()}>
          <span class={`chrono-badge-status chrono-status-${props.status}`}>{statusLabel()}</span>
        </Show>
        <Show when={footLabel()}>
          <span class="chrono-poster-label" classList={{ 'chrono-poster-label-coming': !!coming() }}>
            {footLabel()}
          </span>
        </Show>
        <Show when={!entry().played && isInProgress(entry().progress)}>
          <span class="chrono-progress">
            <span class="chrono-progress-fill" style={{ width: `${Math.round(entry().progress! * 100)}%` }} />
          </span>
        </Show>
      </div>
      <span class="chrono-card-text">
        <span class="chrono-card-title">{entry().title}</span>
        <span class="chrono-card-meta">{entryMeta(entry())}</span>
      </span>
    </>
  );

  return (
    <div
      class="chrono-card"
      classList={{ 'chrono-card-unowned': !entry().owned, 'chrono-card-upcoming': !!coming() }}
    >
      <Show
        when={owned()}
        fallback={
          <button
            type="button"
            class="chrono-card-hit"
            aria-label={description()}
            onClick={() => props.actions.open(entry())}
            onContextMenu={onContextMenu}
          >
            {content()}
          </button>
        }
      >
        <a
          class="chrono-card-hit"
          href={detailsHash(entry().itemId!, props.serverId)}
          aria-label={description()}
          onContextMenu={onContextMenu}
          draggable={false}
        >
          {content()}
        </a>
      </Show>
      <Show when={owned()}>
        <button
          type="button"
          class="chrono-card-more"
          aria-label={`More options for ${entryLabel(entry())}`}
          aria-haspopup="dialog"
          onClick={() => props.actions.open(entry())}
        >
          <SvgIcon name="more" size={18} />
        </button>
      </Show>
    </div>
  );
}
