import { describe, expect, it } from 'vitest';
import {
  hasPlayableEntry,
  hasStarted,
  libraryCounts,
  loadHiddenFlags,
  nextEntry,
  saveHiddenFlags,
  toggleFlag,
  visibleEntryIds,
  visibleRows
} from '../src/lib/filters';
import type { HubEntry, HubRow, UniverseHub } from '../src/types';

function entry(id: string, extra: Partial<HubEntry> = {}): HubEntry {
  return {
    id,
    type: 'movie',
    title: id,
    flags: [],
    groups: [],
    tmdbId: 1,
    mediaType: 'movie',
    owned: true,
    played: false,
    ...extra
  };
}

const entries: Record<string, HubEntry> = {
  a: entry('a', { played: true }),
  b: entry('b', { flags: ['network-tv'] }),
  c: entry('c', { owned: false }),
  d: entry('d', { flags: ['one-shot', 'network-tv'], owned: false }),
  e: entry('e')
};

const timeline: HubRow = { id: 'timeline', title: 'Timeline', entryIds: ['a', 'b', 'c', 'd', 'e', 'missing'], playable: true };

function hub(extra: Partial<UniverseHub> = {}): UniverseHub {
  return {
    id: 'u',
    name: 'U',
    attribution: [],
    flagLabels: {},
    primaryRowId: 'timeline',
    entries,
    rows: [timeline],
    pages: [],
    requestsEnabled: false,
    ...extra
  };
}

describe('visibleEntryIds', () => {
  it('keeps everything when nothing is hidden', () => {
    expect(visibleEntryIds(timeline, entries, new Set())).toEqual(['a', 'b', 'c', 'd', 'e']);
  });

  it('drops entries with any hidden flag', () => {
    expect(visibleEntryIds(timeline, entries, new Set(['network-tv']))).toEqual(['a', 'c', 'e']);
    expect(visibleEntryIds(timeline, entries, new Set(['one-shot']))).toEqual(['a', 'b', 'c', 'e']);
  });
});

describe('visibleRows', () => {
  it('drops rows that become empty', () => {
    const tvOnly: HubRow = { id: 'tv', title: 'TV', entryIds: ['b', 'd'], playable: true };
    const rows = visibleRows([timeline, tvOnly], entries, new Set(['network-tv']));
    expect(rows.map(item => item.row.id)).toEqual(['timeline']);
  });
});

describe('libraryCounts', () => {
  it('counts owned entries among visible ones', () => {
    expect(libraryCounts(entries, new Set())).toEqual({ owned: 3, total: 5 });
    expect(libraryCounts(entries, new Set(['network-tv']))).toEqual({ owned: 2, total: 3 });
  });
});

describe('nextEntry', () => {
  it('uses the server suggestion when visible', () => {
    expect(nextEntry(hub({ nextEntryId: 'b' }), new Set())?.id).toBe('b');
  });

  it('falls back to the first owned unplayed visible entry', () => {
    expect(nextEntry(hub({ nextEntryId: 'b' }), new Set(['network-tv']))?.id).toBe('e');
    expect(nextEntry(hub(), new Set())?.id).toBe('b');
  });

  it('returns undefined when everything is played', () => {
    const played = Object.fromEntries(Object.entries(entries).map(([id, item]) => [id, { ...item, played: true }]));
    expect(nextEntry(hub({ entries: played }), new Set())).toBeUndefined();
  });
});

describe('row state', () => {
  it('detects playable rows', () => {
    expect(hasPlayableEntry(timeline, entries, new Set())).toBe(true);
    const unowned: HubRow = { id: 'x', title: 'X', entryIds: ['c', 'd'], playable: false };
    expect(hasPlayableEntry(unowned, entries, new Set())).toBe(false);
    const tv: HubRow = { id: 'tv', title: 'TV', entryIds: ['b'], playable: true };
    expect(hasPlayableEntry(tv, entries, new Set(['network-tv']))).toBe(false);
  });

  it('detects started rows', () => {
    expect(hasStarted(timeline, entries)).toBe(true);
    expect(hasStarted({ ...timeline, entryIds: ['b', 'e'] }, entries)).toBe(false);
    expect(hasStarted({ ...timeline, entryIds: ['b'] }, { b: entry('b', { progress: 0.2 }) })).toBe(true);
  });
});

describe('hidden flag persistence', () => {
  function memoryStorage() {
    const data = new Map<string, string>();
    return {
      getItem: (key: string) => data.get(key) ?? null,
      setItem: (key: string, value: string) => void data.set(key, value),
      removeItem: (key: string) => void data.delete(key),
      data
    };
  }

  it('round-trips per universe', () => {
    const storage = memoryStorage();
    saveHiddenFlags(storage, 'mcu', ['network-tv']);
    expect(loadHiddenFlags(storage, 'mcu')).toEqual(['network-tv']);
    expect(loadHiddenFlags(storage, 'star-wars')).toEqual([]);
    saveHiddenFlags(storage, 'mcu', []);
    expect(storage.data.size).toBe(0);
  });

  it('tolerates broken or unavailable storage', () => {
    const storage = memoryStorage();
    storage.setItem('chrono:hidden-flags:mcu', '{not json');
    expect(loadHiddenFlags(storage, 'mcu')).toEqual([]);
    storage.setItem('chrono:hidden-flags:mcu', '[1,"a"]');
    expect(loadHiddenFlags(storage, 'mcu')).toEqual(['a']);
    const throwing = {
      getItem: () => {
        throw new Error('denied');
      },
      setItem: () => {
        throw new Error('denied');
      },
      removeItem: () => {
        throw new Error('denied');
      }
    };
    expect(loadHiddenFlags(throwing, 'mcu')).toEqual([]);
    expect(() => saveHiddenFlags(throwing, 'mcu', ['a'])).not.toThrow();
    expect(loadHiddenFlags(undefined, 'mcu')).toEqual([]);
  });

  it('toggles flags', () => {
    expect(toggleFlag([], 'a')).toEqual(['a']);
    expect(toggleFlag(['a', 'b'], 'a')).toEqual(['b']);
  });
});
