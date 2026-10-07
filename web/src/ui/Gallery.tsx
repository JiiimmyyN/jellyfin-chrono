import { createSignal, For, Show, onMount, type JSX } from 'solid-js';
import { chronoApi, currentClient, describeError, imageUrl, NotSignedInError, userKey } from '../lib/api';
import { ownedCountLabel } from '../lib/labels';
import { routeHash } from '../lib/route';
import type { UniverseSummary } from '../types';
import { GallerySkeleton, StateMessage } from './states';
import { setPageTitle, subscribeActivation } from './shell';

const universeCache = new Map<string, UniverseSummary[]>();

function cacheKey(): string {
  const client = currentClient();
  return client ? userKey(client) : '';
}

function Tile(props: { universe: UniverseSummary }): JSX.Element {
  const backdrop = () => imageUrl(props.universe.backdropUrl);
  const posters = () => props.universe.posterUrls.slice(0, 6).map(url => imageUrl(url)!).filter(Boolean);
  const ratio = () =>
    props.universe.entryCount > 0 ? Math.round((props.universe.ownedCount / props.universe.entryCount) * 100) : 0;

  return (
    <a class="chrono-tile" href={routeHash({ view: 'hub', universeId: props.universe.id })}>
      <div class="chrono-tile-media" aria-hidden="true">
        <Show
          when={backdrop()}
          fallback={
            <div class="chrono-tile-collage">
              <For each={posters()}>{url => <img src={url} alt="" loading="lazy" decoding="async" />}</For>
            </div>
          }
        >
          <img class="chrono-tile-backdrop" src={backdrop()} alt="" loading="lazy" decoding="async" />
        </Show>
      </div>
      <div class="chrono-tile-shade" aria-hidden="true" />
      <Show when={backdrop() && posters().length > 0}>
        <div class="chrono-tile-fan" aria-hidden="true">
          <For each={posters().slice(0, 4)}>
            {(url, index) => <img src={url} alt="" loading="lazy" decoding="async" style={{ '--chrono-i': index() }} />}
          </For>
        </div>
      </Show>
      <div class="chrono-tile-body">
        <h2 class="chrono-tile-name">{props.universe.name}</h2>
        <p class="chrono-tile-meta">{ownedCountLabel(props.universe.ownedCount, props.universe.entryCount)}</p>
        <span class="chrono-meter" role="presentation">
          <span class="chrono-meter-fill" style={{ width: `${ratio()}%` }} />
        </span>
      </div>
    </a>
  );
}

export function Gallery(): JSX.Element {
  const [universes, setUniverses] = createSignal<UniverseSummary[] | undefined>(universeCache.get(cacheKey()));
  const [error, setError] = createSignal<unknown>();
  const [loading, setLoading] = createSignal(false);

  const load = async () => {
    setLoading(true);
    setError(undefined);
    try {
      const list = await chronoApi.universes();
      universeCache.set(cacheKey(), list);
      setUniverses(list);
    } catch (err) {
      setError(err);
    } finally {
      setLoading(false);
    }
  };

  onMount(() => {
    setPageTitle('Universes');
    void load();
  });
  subscribeActivation(() => {
    setPageTitle('Universes');
    void load();
  });

  return (
    <div class="chrono-gallery chrono-fade-in">
      <header class="chrono-page-head">
        <h1 class="chrono-page-title">Universes</h1>
        <p class="chrono-page-sub">Movies and series, in the order the story happens.</p>
      </header>
      <Show
        when={universes()}
        fallback={
          <Show when={error()} fallback={<GallerySkeleton />}>
            <StateMessage
              title={error() instanceof NotSignedInError ? 'Not signed in' : 'Could not load universes'}
              text={describeError(error())}
              actionLabel={loading() ? undefined : 'Retry'}
              onAction={load}
            />
          </Show>
        }
      >
        {list => (
          <Show
            when={list().length > 0}
            fallback={
              <StateMessage
                title="No universes enabled"
                text="An administrator can enable them in Dashboard → Plugins → Chrono."
              />
            }
          >
            <div class="chrono-tiles">
              <For each={list()}>{universe => <Tile universe={universe} />}</For>
            </div>
          </Show>
        )}
      </Show>
    </div>
  );
}
