export type Route =
  | { view: 'gallery' }
  | { view: 'hub'; universeId: string }
  | { view: 'page'; universeId: string; pageId: string };

const routePattern = /^#\/chrono(?:\/([^/?#]+))?(?:\/([^/?#]+))?\/?(?:\?.*)?$/;

function decode(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}

export function parseRoute(hash: string): Route | null {
  const match = routePattern.exec(hash);
  if (!match) return null;
  const [, universeId, pageId] = match;
  if (!universeId) return { view: 'gallery' };
  if (!pageId) return { view: 'hub', universeId: decode(universeId) };
  return { view: 'page', universeId: decode(universeId), pageId: decode(pageId) };
}

export function routeHash(route: Route): string {
  switch (route.view) {
    case 'gallery':
      return '#/chrono';
    case 'hub':
      return `#/chrono/${encodeURIComponent(route.universeId)}`;
    case 'page':
      return `#/chrono/${encodeURIComponent(route.universeId)}/${encodeURIComponent(route.pageId)}`;
  }
}

export function routeKey(route: Route): string {
  return routeHash(route);
}

export function isChronoHref(href: string | null | undefined): boolean {
  if (!href) return false;
  const hashIndex = href.indexOf('#');
  if (hashIndex < 0) return false;
  return parseRoute(href.slice(hashIndex)) !== null;
}

export function chronoHashFromHref(href: string): string {
  return href.slice(href.indexOf('#'));
}

export function detailsHash(itemId: string, serverId: string | undefined): string {
  const server = serverId ? `&serverId=${encodeURIComponent(serverId)}` : '';
  return `#/details?id=${encodeURIComponent(itemId)}${server}`;
}
