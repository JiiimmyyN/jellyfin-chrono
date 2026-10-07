import type { HubEntry, HubRow, UniverseHub } from '../types';

export function isHidden(entry: HubEntry, hiddenFlags: ReadonlySet<string>): boolean {
  if (hiddenFlags.size === 0) return false;
  return entry.flags.some(flag => hiddenFlags.has(flag));
}

export function visibleEntryIds(
  row: HubRow,
  entries: Record<string, HubEntry>,
  hiddenFlags: ReadonlySet<string>
): string[] {
  return row.entryIds.filter(id => {
    const entry = entries[id];
    return entry !== undefined && !isHidden(entry, hiddenFlags);
  });
}

export function visibleRows(
  rows: HubRow[],
  entries: Record<string, HubEntry>,
  hiddenFlags: ReadonlySet<string>
): { row: HubRow; entryIds: string[] }[] {
  return rows
    .map(row => ({ row, entryIds: visibleEntryIds(row, entries, hiddenFlags) }))
    .filter(item => item.entryIds.length > 0);
}

export function libraryCounts(
  entries: Record<string, HubEntry>,
  hiddenFlags: ReadonlySet<string>
): { owned: number; total: number } {
  let owned = 0;
  let total = 0;
  for (const entry of Object.values(entries)) {
    if (isHidden(entry, hiddenFlags)) continue;
    total++;
    if (entry.owned) owned++;
  }
  return { owned, total };
}

export function primaryRow(hub: UniverseHub): HubRow | undefined {
  return hub.rows.find(row => row.id === hub.primaryRowId)
    ?? hub.pages.flatMap(page => page.rows).find(row => row.id === hub.primaryRowId);
}

export function nextEntry(hub: UniverseHub, hiddenFlags: ReadonlySet<string>): HubEntry | undefined {
  const suggested = hub.nextEntryId ? hub.entries[hub.nextEntryId] : undefined;
  if (suggested && suggested.owned && !isHidden(suggested, hiddenFlags)) return suggested;
  const row = primaryRow(hub);
  if (!row) return undefined;
  return visibleEntryIds(row, hub.entries, hiddenFlags)
    .map(id => hub.entries[id])
    .find(entry => entry.owned && !entry.played);
}

export function hasPlayableEntry(row: HubRow, entries: Record<string, HubEntry>, hiddenFlags: ReadonlySet<string>): boolean {
  return row.playable && visibleEntryIds(row, entries, hiddenFlags).some(id => entries[id].owned);
}

export function hasStarted(row: HubRow, entries: Record<string, HubEntry>): boolean {
  return row.entryIds.some(id => {
    const entry = entries[id];
    return entry !== undefined && (entry.played || (entry.progress ?? 0) > 0);
  });
}

const storagePrefix = 'chrono:hidden-flags:';

export function loadHiddenFlags(storage: Pick<Storage, 'getItem'> | undefined, universeId: string): string[] {
  try {
    const raw = storage?.getItem(storagePrefix + universeId);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((flag): flag is string => typeof flag === 'string') : [];
  } catch {
    return [];
  }
}

export function saveHiddenFlags(storage: Pick<Storage, 'setItem' | 'removeItem'> | undefined, universeId: string, flags: string[]): void {
  try {
    if (flags.length === 0) storage?.removeItem(storagePrefix + universeId);
    else storage?.setItem(storagePrefix + universeId, JSON.stringify(flags));
  } catch {
    return;
  }
}

export function toggleFlag(flags: string[], flag: string): string[] {
  return flags.includes(flag) ? flags.filter(f => f !== flag) : [...flags, flag];
}
