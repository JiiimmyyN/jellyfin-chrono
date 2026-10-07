export type EntryType = 'movie' | 'series' | 'season';

export type RequestStatus = 'available' | 'partial' | 'processing' | 'pending' | 'none' | 'unknown';

export interface UniverseSummary {
  id: string;
  name: string;
  description?: string;
  entryCount: number;
  ownedCount: number;
  posterUrls: string[];
  backdropUrl?: string;
}

export interface HubEntry {
  id: string;
  type: EntryType;
  title: string;
  subtitle?: string;
  seasonNumber?: number;
  released?: string;
  year?: number;
  overview?: string;
  flags: string[];
  groups: string[];
  tmdbId: number;
  mediaType: 'movie' | 'tv';
  owned: boolean;
  itemId?: string;
  posterUrl?: string;
  backdropUrl?: string;
  played: boolean;
  progress?: number;
  unplayedCount?: number;
  runtimeMinutes?: number;
  episodeCount?: number;
}

export interface HubRow {
  id: string;
  title: string;
  description?: string;
  basis?: string;
  entryIds: string[];
  playable: boolean;
}

export interface HubPage {
  id: string;
  title: string;
  rows: HubRow[];
}

export interface UniverseHub {
  id: string;
  name: string;
  description?: string;
  revision?: string;
  attribution: { name: string; url?: string; license?: string }[];
  flagLabels: Record<string, string>;
  backdropUrl?: string;
  primaryRowId: string;
  nextEntryId?: string;
  entries: Record<string, HubEntry>;
  rows: HubRow[];
  pages: HubPage[];
  requestsEnabled: boolean;
}

export interface PlayRequest {
  rowId: string;
  fromEntryId?: string;
  excludeFlags?: string[];
}
