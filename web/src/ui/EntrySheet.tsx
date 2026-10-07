import { createSignal, For, onCleanup, onMount, Show, type JSX } from 'solid-js';
import { describeError, imageUrl } from '../lib/api';
import { comingLabel, displayYear } from '../lib/dates';
import { canRequest, formatRuntime, isInProgress, requestStatusLabel } from '../lib/labels';
import { detailsHash } from '../lib/route';
import type { HubEntry, RequestStatus } from '../types';
import { SvgIcon } from './icons';
import { Poster } from './Poster';

export function EntrySheet(props: {
  entry: HubEntry;
  status?: RequestStatus;
  flagLabels: Record<string, string>;
  requestsEnabled: boolean;
  playable: boolean;
  serverId?: string;
  now: Date;
  onClose(): void;
  onPlayFrom(entry: HubEntry): Promise<void>;
  onRequest(entry: HubEntry): Promise<RequestStatus>;
}): JSX.Element {
  const entry = () => props.entry;
  const titleId = `chrono-sheet-title-${entry().id}`;
  const [requesting, setRequesting] = createSignal(false);
  const [playing, setPlaying] = createSignal(false);
  const [error, setError] = createSignal<string>();
  let dialog: HTMLDivElement | undefined;
  const previousFocus = document.activeElement as HTMLElement | null;

  const coming = () => comingLabel(entry().released, entry().year, props.now);
  const year = () => displayYear(entry().released, entry().year);
  const meta = () =>
    [
      entry().subtitle,
      year(),
      formatRuntime(entry().runtimeMinutes),
      entry().episodeCount ? `${entry().episodeCount} episodes` : undefined
    ]
      .filter(Boolean)
      .join(' · ');
  const statusLabel = () => (entry().owned ? undefined : requestStatusLabel(props.status));
  const showRequest = () => !entry().owned && props.requestsEnabled && canRequest(props.status);
  const flagNames = () => entry().flags.map(flag => props.flagLabels[flag] ?? flag);
  const backdrop = () => imageUrl(entry().backdropUrl);

  const request = async () => {
    setError(undefined);
    setRequesting(true);
    try {
      await props.onRequest(entry());
    } catch (err) {
      setError(describeError(err));
    } finally {
      setRequesting(false);
    }
  };

  const playFrom = async () => {
    setError(undefined);
    setPlaying(true);
    try {
      await props.onPlayFrom(entry());
      props.onClose();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setPlaying(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      event.preventDefault();
      props.onClose();
      return;
    }
    if (event.key !== 'Tab' || !dialog) return;
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), [tabindex="0"]')
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  onMount(() => {
    dialog?.querySelector<HTMLElement>('.chrono-sheet-primary, .chrono-sheet-close')?.focus({ preventScroll: true });
    document.addEventListener('keydown', onKeyDown, true);
    onCleanup(() => {
      document.removeEventListener('keydown', onKeyDown, true);
      if (previousFocus && document.contains(previousFocus)) previousFocus.focus({ preventScroll: true });
    });
  });

  return (
    <div class="chrono-sheet-layer">
      <div class="chrono-sheet-backdrop" onClick={() => props.onClose()} />
      <div class="chrono-sheet" role="dialog" aria-modal="true" aria-labelledby={titleId} ref={dialog}>
        <Show when={backdrop()}>
          <div class="chrono-sheet-art" style={{ 'background-image': `url("${backdrop()}")` }} />
        </Show>
        <div class="chrono-sheet-grip" aria-hidden="true" />
        <button type="button" class="chrono-icon-btn chrono-sheet-close" aria-label="Close" onClick={() => props.onClose()}>
          <SvgIcon name="close" size={22} />
        </button>
        <div class="chrono-sheet-main">
          <div class="chrono-sheet-poster" classList={{ 'chrono-card-unowned': !entry().owned }}>
            <Poster url={entry().posterUrl} title={entry().title} subtitle={entry().subtitle} alt={`${entry().title} poster`} eager />
          </div>
          <div class="chrono-sheet-head">
            <h2 class="chrono-sheet-title" id={titleId}>{entry().title}</h2>
            <Show when={meta()}>
              <p class="chrono-sheet-meta">{meta()}</p>
            </Show>
            <div class="chrono-sheet-tags">
              <Show when={entry().played}>
                <span class="chrono-tag chrono-tag-good">
                  <SvgIcon name="check" size={14} /> Watched
                </span>
              </Show>
              <Show when={!entry().played && isInProgress(entry().progress)}>
                <span class="chrono-tag">{Math.round(entry().progress! * 100)}% watched</span>
              </Show>
              <Show when={entry().owned && !entry().played && (entry().unplayedCount ?? 0) > 0 && entry().type !== 'movie'}>
                <span class="chrono-tag">{entry().unplayedCount} unwatched</span>
              </Show>
              <Show when={coming()}>
                <span class="chrono-tag chrono-tag-accent">{coming()}</span>
              </Show>
              <Show when={!entry().owned && !coming()}>
                <span class="chrono-tag chrono-tag-muted">Not in library</span>
              </Show>
              <Show when={statusLabel()}>
                <span class={`chrono-tag chrono-status-${props.status}`}>{statusLabel()}</span>
              </Show>
            </div>
          </div>
        </div>
        <Show when={entry().overview}>
          <p class="chrono-sheet-overview">{entry().overview}</p>
        </Show>
        <Show when={flagNames().length > 0}>
          <ul class="chrono-sheet-flags" aria-label="Flags">
            <For each={flagNames()}>{name => <li class="chrono-chip chrono-chip-static">{name}</li>}</For>
          </ul>
        </Show>
        <Show when={error()}>
          <p class="chrono-sheet-error" role="alert">{error()}</p>
        </Show>
        <div class="chrono-sheet-actions">
          <Show when={entry().owned && entry().itemId}>
            <Show when={props.playable}>
              <button type="button" class="chrono-btn chrono-btn-primary chrono-sheet-primary" disabled={playing()} onClick={playFrom}>
                <SvgIcon name="play" size={20} />
                <span>{playing() ? 'Starting…' : 'Play from here'}</span>
              </button>
            </Show>
            <a
              class="chrono-btn chrono-btn-secondary"
              classList={{ 'chrono-sheet-primary': !props.playable }}
              href={detailsHash(entry().itemId!, props.serverId)}
              onClick={() => props.onClose()}
            >
              <SvgIcon name="details" size={20} />
              <span>Details</span>
            </a>
          </Show>
          <Show when={showRequest()}>
            <button type="button" class="chrono-btn chrono-btn-primary chrono-sheet-primary" disabled={requesting()} onClick={request}>
              <SvgIcon name="plus" size={20} />
              <span>{requesting() ? 'Requesting…' : 'Request'}</span>
            </button>
          </Show>
        </div>
      </div>
    </div>
  );
}
