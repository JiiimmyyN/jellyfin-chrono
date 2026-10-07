import { createEffect, createSignal, Match, on, onCleanup, onMount, Show, Switch, type JSX } from 'solid-js';
import { watchHeaderHeight } from '../host/header';
import { locationChangeEvent, watchHistory } from '../host/history';
import { parseRoute, routeKey, type Route } from '../lib/route';
import { Gallery } from './Gallery';
import { HubView } from './HubView';
import { applyPageTitle, notifyActivation } from './shell';
import { Toasts } from './toast';

const activeClass = 'chrono-active';

export function App(): JSX.Element {
  const initial = parseRoute(location.hash);
  const [route, setRoute] = createSignal<Route | null>(initial);
  const [shownRoute, setShownRoute] = createSignal<Route | null>(initial, {
    equals: (a, b) => (a && b ? routeKey(a) === routeKey(b) : a === b)
  });
  const [headerHeight, setHeaderHeight] = createSignal(56);
  const [scrolled, setScrolled] = createSignal(false);
  let scroller: HTMLDivElement | undefined;

  const syncRoute = () => {
    const next = parseRoute(location.hash);
    const wasActive = route() !== null;
    setRoute(next);
    if (!next) return;
    const previous = shownRoute();
    setShownRoute(next);
    if (previous && routeKey(previous) === routeKey(next)) {
      if (!wasActive) notifyActivation();
      window.setTimeout(applyPageTitle, 0);
    }
  };

  onMount(() => {
    watchHistory();
    window.addEventListener('hashchange', syncRoute);
    window.addEventListener('popstate', syncRoute);
    window.addEventListener(locationChangeEvent, syncRoute);
    onCleanup(() => {
      window.removeEventListener('hashchange', syncRoute);
      window.removeEventListener('popstate', syncRoute);
      window.removeEventListener(locationChangeEvent, syncRoute);
    });
  });

  createEffect(() => {
    const active = route() !== null;
    document.documentElement.classList.toggle(activeClass, active);
    if (!active) return;
    const stop = watchHeaderHeight(setHeaderHeight);
    onCleanup(stop);
  });

  createEffect(
    on(
      () => (shownRoute() ? routeKey(shownRoute()!) : ''),
      () => {
        if (scroller) {
          scroller.scrollTop = 0;
          scroller.focus({ preventScroll: true });
        }
        setScrolled(false);
      },
      { defer: true }
    )
  );

  const universeId = () => {
    const current = shownRoute();
    return current && current.view !== 'gallery' ? current.universeId : undefined;
  };
  const pageId = () => {
    const current = shownRoute();
    return current?.view === 'page' ? current.pageId : undefined;
  };

  return (
    <Show when={shownRoute()}>
      <div
        class="chrono-root"
        classList={{ 'chrono-inactive': route() === null }}
        style={{ '--chrono-header-h': `${headerHeight()}px` }}
        aria-hidden={route() === null ? 'true' : undefined}
      >
        <div class="chrono-topband" classList={{ 'chrono-topband-solid': scrolled() }} aria-hidden="true" />
        <div
          class="chrono-scroll"
          ref={scroller}
          tabIndex={-1}
          onScroll={event => setScrolled(event.currentTarget.scrollTop > 8)}
        >
          <main class="chrono-main">
            <Switch>
              <Match when={shownRoute()?.view === 'gallery'}>
                <Gallery />
              </Match>
              <Match when={universeId()}>
                {id => (
                  <Show when={id()} keyed>
                    {stableId => <HubView universeId={stableId} pageId={pageId()} />}
                  </Show>
                )}
              </Match>
            </Switch>
          </main>
        </div>
        <Toasts />
      </div>
    </Show>
  );
}
