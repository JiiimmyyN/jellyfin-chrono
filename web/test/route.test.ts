import { describe, expect, it } from 'vitest';
import { chronoHashFromHref, detailsHash, isChronoHref, parseRoute, routeHash } from '../src/lib/route';

describe('parseRoute', () => {
  it('parses the gallery route', () => {
    expect(parseRoute('#/chrono')).toEqual({ view: 'gallery' });
    expect(parseRoute('#/chrono/')).toEqual({ view: 'gallery' });
  });

  it('parses hub and page routes', () => {
    expect(parseRoute('#/chrono/mcu')).toEqual({ view: 'hub', universeId: 'mcu' });
    expect(parseRoute('#/chrono/mcu/phases')).toEqual({ view: 'page', universeId: 'mcu', pageId: 'phases' });
    expect(parseRoute('#/chrono/star%20wars/')).toEqual({ view: 'hub', universeId: 'star wars' });
  });

  it('ignores query strings', () => {
    expect(parseRoute('#/chrono/mcu?x=1')).toEqual({ view: 'hub', universeId: 'mcu' });
  });

  it('rejects other routes', () => {
    expect(parseRoute('')).toBeNull();
    expect(parseRoute('#/home')).toBeNull();
    expect(parseRoute('#/chronology')).toBeNull();
    expect(parseRoute('#/details?id=1')).toBeNull();
    expect(parseRoute('#/chrono/a/b/c')).toBeNull();
  });

  it('survives malformed escapes', () => {
    expect(parseRoute('#/chrono/%E0%A4%A')).toEqual({ view: 'hub', universeId: '%E0%A4%A' });
  });
});

describe('routeHash', () => {
  it('round-trips through parseRoute', () => {
    const routes = [
      { view: 'gallery' as const },
      { view: 'hub' as const, universeId: 'star wars' },
      { view: 'page' as const, universeId: 'mcu', pageId: 'phase/1' }
    ];
    for (const route of routes) expect(parseRoute(routeHash(route))).toEqual(route);
  });
});

describe('chrono links', () => {
  it('recognises menu link hrefs', () => {
    expect(isChronoHref('#/chrono')).toBe(true);
    expect(isChronoHref('https://jf.example/web/#/chrono')).toBe(true);
    expect(isChronoHref('/web/index.html#/chrono/mcu')).toBe(true);
    expect(isChronoHref('#/home')).toBe(false);
    expect(isChronoHref('https://example.com/chrono')).toBe(false);
    expect(isChronoHref(null)).toBe(false);
  });

  it('extracts the hash part', () => {
    expect(chronoHashFromHref('https://jf.example/web/#/chrono/mcu')).toBe('#/chrono/mcu');
  });

  it('builds item details hashes', () => {
    expect(detailsHash('abc', 'srv')).toBe('#/details?id=abc&serverId=srv');
    expect(detailsHash('abc', undefined)).toBe('#/details?id=abc');
  });
});
