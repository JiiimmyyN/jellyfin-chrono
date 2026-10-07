import { describe, expect, it } from 'vitest';
import { apiUrl, isAbsoluteUrl, resolveServerUrl, siblingAssetUrl } from '../src/lib/url';

describe('resolveServerUrl', () => {
  it('prefixes server-relative URLs', () => {
    expect(resolveServerUrl('Items/1/Images/Primary?tag=x', 'https://jf.example')).toBe(
      'https://jf.example/Items/1/Images/Primary?tag=x'
    );
    expect(resolveServerUrl('/Items/1', 'https://jf.example/jellyfin/')).toBe('https://jf.example/jellyfin/Items/1');
  });

  it('keeps absolute URLs', () => {
    expect(resolveServerUrl('https://image.tmdb.org/t/p/w342/a.jpg', 'https://jf')).toBe('https://image.tmdb.org/t/p/w342/a.jpg');
    expect(resolveServerUrl('data:image/svg+xml,abc', 'https://jf')).toBe('data:image/svg+xml,abc');
    expect(resolveServerUrl('//cdn.example/a.jpg', 'https://jf')).toBe('//cdn.example/a.jpg');
  });

  it('returns undefined for empty input', () => {
    expect(resolveServerUrl(undefined, 'https://jf')).toBeUndefined();
    expect(resolveServerUrl('', 'https://jf')).toBeUndefined();
  });

  it('detects absolute URLs', () => {
    expect(isAbsoluteUrl('http://a')).toBe(true);
    expect(isAbsoluteUrl('Items/1')).toBe(false);
  });
});

describe('apiUrl', () => {
  it('encodes path segments', () => {
    expect(apiUrl('https://jf.example/', 'Universes', 'star wars', 'Entries', 'a/b', 'Request')).toBe(
      'https://jf.example/Chrono/Universes/star%20wars/Entries/a%2Fb/Request'
    );
  });
});

describe('siblingAssetUrl', () => {
  it('points next to the script and keeps its query', () => {
    expect(siblingAssetUrl('https://jf.example/Chrono/Client/chrono.js?v=1.2', 'chrono.css')).toBe(
      'https://jf.example/Chrono/Client/chrono.css?v=1.2'
    );
    expect(siblingAssetUrl('https://jf.example/base/Chrono/Client/chrono.js', 'chrono.css')).toBe(
      'https://jf.example/base/Chrono/Client/chrono.css'
    );
  });
});
