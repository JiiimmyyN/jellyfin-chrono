import { createEffect, createMemo, createSignal, For, onMount, Show, type JSX } from 'solid-js';
import { ApiError, chronoApi, currentClient, describeError, NotSignedInError, userKey } from '../lib/api';
import {
  hasPlayableEntry,
  hasStarted,
  libraryCounts,
  loadHiddenFlags,
  nextEntry,
  primaryRow,
  saveHiddenFlags,
  toggleFlag,
  visibleRows
} from '../lib/filters';
import { entryLabel } from '../lib/labels';
import { routeHash } from '../lib/route';
import type { HubEntry, PlayRequest, RequestStatus, UniverseHub } from '../types';
import { EntrySheet } from './EntrySheet';
import { Hero } from './Hero';
import { SvgIcon } from './icons';
import { Row } from './Row';
import { setPageTitle, subscribeActivation } from './shell';
import { HubSkeleton, StateMessage } from './states';
import { showToast } from './toast';

const hubCache = new Map<string, UniverseHub>();

function cacheKey(universeId: string): string {
  const client = currentClient();
  return `${client ? userKey(client) : ''}|${universeId}`;
}

function storage(): Storage | undefined {
  try {
    return window.localStorage;
  } catch {
    return undefined;
  }
}

function playErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'No playback session was found for this device. Reload the page and try again.';
  }
  return `Could not start playback: ${describeError(error)}`;
}

export function HubView(props: { universeId: string; pageId?: string }): JSX.Element {
  const [hub, setHub] = createSignal<UniverseHub | undefined>(hubCache.get(cacheKey(props.universeId)));
  const [error, setError] = createSignal<unknown>();
  const [statuses, setStatuses] = createSignal<Record<string, RequestStatus>>({});
  const [hiddenFlags, setHiddenFlags] = createSignal<string[]>(loadHiddenFlags(storage(), props.universeId));
  const [selected, setSelected] = createSignal<HubEntry>();
  const [playBusy, setPlayBusy] = createSignal(false);
  const now = new Date();

  const hiddenSet = createMemo(() => new Set(hiddenFlags()));
  const serverId = () => currentClient()?.serverId();

  const loadStatuses = async (universeId: string) => {
    try {
      setStatuses(await chronoApi.requestStatus(universeId));
    } catch {
      return;
    }
  };

  const load = async () => {
    setError(undefined);
    try {
      const data = await chronoApi.hub(props.universeId);
      hubCache.set(cacheKey(props.universeId), data);
      setHub(data);
      void loadStatuses(props.universeId);
    } catch (err) {
      setError(err);
    }
  };

  onMount(() => void load());
  subscribeActivation(() => {
    setSelected(undefined);
    void load();
  });

  const page = createMemo(() => {
    const data = hub();
    if (!data || !props.pageId) return undefined;
    return data.pages.find(candidate => candidate.id === props.pageId);
  });

  createEffect(() => {
    const data = hub();
    const name = data?.name ?? 'Universes';
    setPageTitle(page() ? `${page()!.title} · ${name}` : name);
  });

  const rows = createMemo(() => {
    const data = hub();
    if (!data) return [];
    const source = props.pageId ? page()?.rows ?? [] : data.rows;
    return visibleRows(source, data.entries, hiddenSet());
  });

  const rowsById = createMemo(() => new Map(rows().map(item => [item.row.id, item])));
  const rowIds = createMemo(() => rows().map(item => item.row.id), undefined, {
    equals: (a, b) => a.length === b.length && a.every((id, index) => id === b[index])
  });

  const counts = createMemo(() => {
    const data = hub();
    return data ? libraryCounts(data.entries, hiddenSet()) : { owned: 0, total: 0 };
  });

  const timeline = createMemo(() => {
    const data = hub();
    return data ? primaryRow(data) : undefined;
  });

  const toggle = (flag: string) => {
    const next = toggleFlag(hiddenFlags(), flag);
    setHiddenFlags(next);
    saveHiddenFlags(storage(), props.universeId, next);
  };

  const play = async (body: PlayRequest) => {
    const flags = hiddenFlags();
    const request: PlayRequest = flags.length > 0 ? { ...body, excludeFlags: flags } : body;
    setPlayBusy(true);
    try {
      await chronoApi.play(props.universeId, request);
      showToast('Starting playback…');
    } finally {
      setPlayBusy(false);
    }
  };

  const playWithToast = (body: PlayRequest) => {
    play(body).catch(err => showToast(playErrorMessage(err), 'error'));
  };

  let selectedRowId: string | undefined;

  const playFrom = async (entry: HubEntry) => {
    const rowId = selectedRowId ?? hub()?.primaryRowId;
    if (!rowId) return;
    try {
      await play({ rowId, fromEntryId: entry.id });
    } catch (err) {
      throw new Error(playErrorMessage(err));
    }
  };

  const requestEntry = async (entry: HubEntry): Promise<RequestStatus> => {
    const result = await chronoApi.request(props.universeId, entry.id);
    setStatuses(current => ({ ...current, [entry.id]: result.status }));
    showToast(`Requested ${entryLabel(entry)}`);
    return result.status;
  };

  const actionsFor = (rowId: string) => ({
    open(entry: HubEntry) {
      selectedRowId = rowId;
      setSelected(entry);
    }
  });

  const filterChips = (data: UniverseHub) => Object.entries(data.flagLabels);

  return (
    <Show
      when={hub()}
      fallback={
        <Show when={error()} fallback={<HubSkeleton withHero={!props.pageId} />}>
          <div class="chrono-page-pad">
            <StateMessage
              title={
                error() instanceof NotSignedInError
                  ? 'Not signed in'
                  : error() instanceof ApiError && (error() as ApiError).status === 404
                    ? 'Universe not found'
                    : 'Could not load this universe'
              }
              text={describeError(error())}
              actionLabel="Retry"
              onAction={load}
              linkLabel="All universes"
              linkHref={routeHash({ view: 'gallery' })}
            />
          </div>
        </Show>
      }
    >
      {data => (
        <div class="chrono-hub chrono-fade-in">
          <Show
            when={props.pageId}
            fallback={
              <Hero
                name={data().name}
                description={data().description}
                backdropUrl={data().backdropUrl ?? timeline()?.entryIds.map(id => data().entries[id]?.backdropUrl).find(Boolean)}
                owned={counts().owned}
                total={counts().total}
                next={nextEntry(data(), hiddenSet())}
                started={!!timeline() && hasStarted(timeline()!, data().entries)}
                canPlay={!!timeline() && hasPlayableEntry(timeline()!, data().entries, hiddenSet())}
                busy={playBusy()}
                onContinue={entry => playWithToast({ rowId: data().primaryRowId, fromEntryId: entry.id })}
                onPlayFromStart={() => playWithToast({ rowId: data().primaryRowId })}
              />
            }
          >
            <header class="chrono-page-head chrono-subpage-head">
              <a class="chrono-back" href={routeHash({ view: 'hub', universeId: data().id })}>
                <SvgIcon name="back" size={18} />
                <span>{data().name}</span>
              </a>
              <h1 class="chrono-page-title">{page()?.title ?? 'Page not found'}</h1>
            </header>
          </Show>

          <Show when={data().pages.length > 0 || filterChips(data()).length > 0}>
            <div class="chrono-toolbar">
              <Show when={data().pages.length > 0}>
                <nav class="chrono-tabs" aria-label={`${data().name} pages`}>
                  <a
                    class="chrono-tab"
                    classList={{ 'chrono-tab-active': !props.pageId }}
                    aria-current={!props.pageId ? 'page' : undefined}
                    href={routeHash({ view: 'hub', universeId: data().id })}
                  >
                    Overview
                  </a>
                  <For each={data().pages}>
                    {item => (
                      <a
                        class="chrono-tab"
                        classList={{ 'chrono-tab-active': props.pageId === item.id }}
                        aria-current={props.pageId === item.id ? 'page' : undefined}
                        href={routeHash({ view: 'page', universeId: data().id, pageId: item.id })}
                      >
                        {item.title}
                      </a>
                    )}
                  </For>
                </nav>
              </Show>
              <Show when={filterChips(data()).length > 0}>
                <div class="chrono-chips" role="group" aria-label="Include">
                  <span class="chrono-chips-label">
                    <SvgIcon name="filter" size={14} />
                    Include
                  </span>
                  <For each={filterChips(data())}>
                    {([flag, label]) => (
                      <button
                        type="button"
                        class="chrono-chip"
                        classList={{ 'chrono-chip-off': hiddenSet().has(flag) }}
                        aria-pressed={!hiddenSet().has(flag)}
                        onClick={() => toggle(flag)}
                      >
                        <Show when={!hiddenSet().has(flag)}>
                          <SvgIcon name="check" size={14} />
                        </Show>
                        {label}
                      </button>
                    )}
                  </For>
                </div>
              </Show>
            </div>
          </Show>

          <Show
            when={!props.pageId || page()}
            fallback={
              <StateMessage
                title="Page not found"
                text="This page does not exist in this universe."
                linkLabel={`Back to ${data().name}`}
                linkHref={routeHash({ view: 'hub', universeId: data().id })}
              />
            }
          >
            <Show
              when={rows().length > 0}
              fallback={<StateMessage title="Nothing to show" text="All entries are hidden by the filters above." />}
            >
              <div class="chrono-rows">
                <For each={rowIds()}>
                  {id => (
                    <Show when={rowsById().get(id)}>
                      {item => (
                        <Row
                          row={item().row}
                          entryIds={item().entryIds}
                          entries={data().entries}
                          statuses={statuses()}
                          serverId={serverId()}
                          now={now}
                          playable={hasPlayableEntry(item().row, data().entries, hiddenSet())}
                          actions={actionsFor(id)}
                          onPlayRow={row => playWithToast({ rowId: row.id })}
                        />
                      )}
                    </Show>
                  )}
                </For>
              </div>
            </Show>
          </Show>

          <Show when={data().attribution.length > 0}>
            <footer class="chrono-attribution">
              <span>Data: </span>
              {data()
                .attribution.map(item => (item.license ? `${item.name} (${item.license})` : item.name))
                .join(' · ')}
            </footer>
          </Show>

          <Show when={selected()} keyed>
            {entry => (
              <EntrySheet
                entry={data().entries[entry.id] ?? entry}
                status={statuses()[entry.id]}
                flagLabels={data().flagLabels}
                requestsEnabled={data().requestsEnabled}
                playable={entry.owned}
                serverId={serverId()}
                now={now}
                onClose={() => setSelected(undefined)}
                onPlayFrom={playFrom}
                onRequest={requestEntry}
              />
            )}
          </Show>
        </div>
      )}
    </Show>
  );
}
