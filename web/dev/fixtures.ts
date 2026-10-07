import type { HubEntry, HubRow, RequestStatus, UniverseHub, UniverseSummary } from '../src/types';
import { posterSvg, svgDataUri } from './svg';

type Seed = {
  id: string;
  title: string;
  subtitle?: string;
  type?: 'movie' | 'season' | 'series';
  released: string;
  owned?: boolean;
  played?: boolean;
  progress?: number;
  unplayed?: number;
  episodes?: number;
  flags?: string[];
  groups: string[];
  noPoster?: boolean;
  runtime?: number;
};

let hueSeed = 0;

function hueFor(id: string): number {
  let hash = hueSeed;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) % 360;
  return hash;
}

function serverImage(itemId: string, kind: 'Primary' | 'Backdrop', label: string, hue: number): string {
  return `Items/${itemId}/Images/${kind}?label=${encodeURIComponent(label)}&hue=${hue}`;
}

const overviewText =
  'A pivotal chapter in the saga. Heroes are tested, alliances shift, and the consequences ripple across everything that follows. Placeholder synopsis for development.';

function buildEntry(seed: Seed, mediaType: 'movie' | 'tv'): HubEntry {
  const type = seed.type ?? 'movie';
  const owned = seed.owned ?? true;
  const itemId = owned ? `item-${seed.id}` : undefined;
  const hue = hueFor(seed.id);
  const label = seed.subtitle ? `${seed.title} ${seed.subtitle.replace('Season ', 'S')}` : seed.title;
  const posterUrl = seed.noPoster
    ? undefined
    : owned
      ? serverImage(itemId!, 'Primary', label, hue)
      : svgDataUri(posterSvg(label, hue));
  const seasonNumber = seed.subtitle ? Number(/\d+/.exec(seed.subtitle)?.[0]) : undefined;
  return {
    id: seed.id,
    type,
    title: seed.title,
    subtitle: seed.subtitle,
    seasonNumber,
    released: seed.released,
    year: Number(seed.released.slice(0, 4)),
    overview: overviewText,
    flags: seed.flags ?? [],
    groups: seed.groups,
    tmdbId: 1000 + Math.abs(hue * 7),
    mediaType,
    owned,
    itemId,
    posterUrl,
    backdropUrl: owned ? serverImage(itemId!, 'Backdrop', seed.title, hue) : undefined,
    played: seed.played ?? false,
    progress: seed.progress,
    unplayedCount: type === 'movie' ? undefined : seed.unplayed ?? (seed.played ? 0 : seed.episodes ?? 8),
    runtimeMinutes: type === 'movie' ? seed.runtime ?? 128 : undefined,
    episodeCount: type === 'movie' ? undefined : seed.episodes ?? 8
  };
}

const p1 = ['phase-1', 'infinity-saga'];
const p2 = ['phase-2', 'infinity-saga'];
const p3 = ['phase-3', 'infinity-saga'];
const p4 = ['phase-4', 'multiverse-saga'];
const p5 = ['phase-5', 'multiverse-saga'];
const p6 = ['phase-6', 'multiverse-saga'];

const mcuSeeds: Seed[] = [
  { id: 'captain-america-tfa', title: 'Captain America: The First Avenger', released: '2011-07-22', played: true, groups: p1 },
  { id: 'agent-carter-s1', title: 'Agent Carter', subtitle: 'Season 1', type: 'season', released: '2015-01-06', played: true, episodes: 8, flags: ['network-tv'], groups: [] },
  { id: 'agent-carter-s2', title: 'Agent Carter', subtitle: 'Season 2', type: 'season', released: '2016-01-19', owned: false, episodes: 10, flags: ['network-tv'], groups: [] },
  { id: 'the-consultant', title: 'The Consultant', released: '2011-09-13', owned: false, noPoster: true, runtime: 4, flags: ['one-shot'], groups: p1 },
  { id: 'captain-marvel', title: 'Captain Marvel', released: '2019-03-08', played: true, groups: p3 },
  { id: 'iron-man', title: 'Iron Man', released: '2008-05-02', played: true, groups: p1 },
  { id: 'iron-man-2', title: 'Iron Man 2', released: '2010-05-07', played: true, groups: p1 },
  { id: 'the-incredible-hulk', title: 'The Incredible Hulk', released: '2008-06-13', owned: false, groups: p1 },
  { id: 'thor', title: 'Thor', released: '2011-05-06', played: true, groups: p1 },
  { id: 'the-avengers', title: 'The Avengers', released: '2012-05-04', played: true, groups: p1 },
  { id: 'agents-of-shield-s1', title: "Marvel's Agents of S.H.I.E.L.D.", subtitle: 'Season 1', type: 'season', released: '2013-09-24', progress: 0.64, unplayed: 8, episodes: 22, flags: ['network-tv'], groups: [] },
  { id: 'iron-man-3', title: 'Iron Man 3', released: '2013-05-03', played: true, groups: p2 },
  { id: 'thor-the-dark-world', title: 'Thor: The Dark World', released: '2013-11-08', progress: 0.38, groups: p2 },
  { id: 'agents-of-shield-s2', title: "Marvel's Agents of S.H.I.E.L.D.", subtitle: 'Season 2', type: 'season', released: '2014-09-23', unplayed: 22, episodes: 22, flags: ['network-tv'], groups: [] },
  { id: 'captain-america-tws', title: 'Captain America: The Winter Soldier', released: '2014-04-04', groups: p2 },
  { id: 'guardians-of-the-galaxy', title: 'Guardians of the Galaxy', released: '2014-08-01', groups: p2 },
  { id: 'guardians-vol-2', title: 'Guardians of the Galaxy Vol. 2', released: '2017-05-05', groups: p3 },
  { id: 'daredevil-s1', title: "Marvel's Daredevil", subtitle: 'Season 1', type: 'season', released: '2015-04-10', owned: false, episodes: 13, flags: ['netflix'], groups: [] },
  { id: 'jessica-jones-s1', title: "Marvel's Jessica Jones", subtitle: 'Season 1', type: 'season', released: '2015-11-20', owned: false, episodes: 13, flags: ['netflix'], groups: [] },
  { id: 'avengers-age-of-ultron', title: 'Avengers: Age of Ultron', released: '2015-05-01', groups: p2 },
  { id: 'ant-man', title: 'Ant-Man', released: '2015-07-17', groups: p2 },
  { id: 'captain-america-civil-war', title: 'Captain America: Civil War', released: '2016-05-06', groups: p3 },
  { id: 'black-widow', title: 'Black Widow', released: '2021-07-09', groups: p4 },
  { id: 'black-panther', title: 'Black Panther', released: '2018-02-16', groups: p3 },
  { id: 'spider-man-homecoming', title: 'Spider-Man: Homecoming', released: '2017-07-07', groups: p3 },
  { id: 'doctor-strange', title: 'Doctor Strange', released: '2016-11-04', noPoster: true, groups: p3 },
  { id: 'thor-ragnarok', title: 'Thor: Ragnarok', released: '2017-11-03', groups: p3 },
  { id: 'ant-man-and-the-wasp', title: 'Ant-Man and the Wasp', released: '2018-07-06', owned: false, groups: p3 },
  { id: 'avengers-infinity-war', title: 'Avengers: Infinity War', released: '2018-04-27', groups: p3 },
  { id: 'avengers-endgame', title: 'Avengers: Endgame', released: '2019-04-26', runtime: 181, groups: p3 },
  { id: 'loki-s1', title: 'Loki', subtitle: 'Season 1', type: 'season', released: '2021-06-09', episodes: 6, groups: p4 },
  { id: 'what-if-s1', title: 'What If…?', subtitle: 'Season 1', type: 'season', released: '2021-08-11', owned: false, episodes: 9, flags: ['animated', 'alternate-universe'], groups: p4 },
  { id: 'wandavision', title: 'WandaVision', subtitle: 'Season 1', type: 'season', released: '2021-01-15', episodes: 9, unplayed: 3, progress: 0.66, groups: p4 },
  { id: 'falcon-winter-soldier', title: 'The Falcon and the Winter Soldier', subtitle: 'Season 1', type: 'season', released: '2021-03-19', episodes: 6, groups: p4 },
  { id: 'shang-chi', title: 'Shang-Chi and the Legend of the Ten Rings', released: '2021-09-03', groups: p4 },
  { id: 'eternals', title: 'Eternals', released: '2021-11-05', owned: false, groups: p4 },
  { id: 'spider-man-nwh', title: 'Spider-Man: No Way Home', released: '2021-12-17', groups: p4 },
  { id: 'hawkeye-s1', title: 'Hawkeye', subtitle: 'Season 1', type: 'season', released: '2021-11-24', episodes: 6, groups: p4 },
  { id: 'doctor-strange-motm', title: 'Doctor Strange in the Multiverse of Madness', released: '2022-05-06', groups: p4 },
  { id: 'thunderbolts', title: 'Thunderbolts*', released: '2025-05-02', groups: p5 },
  { id: 'fantastic-four', title: 'The Fantastic Four: First Steps', released: '2025-07-25', owned: false, flags: ['alternate-universe'], groups: p6 },
  { id: 'spider-man-bnd', title: 'Spider-Man: Brand New Day', released: '2026-07-31', owned: false, groups: p6 },
  { id: 'avengers-doomsday', title: 'Avengers: Doomsday', released: '2026-12-18', owned: false, groups: p6 },
  { id: 'avengers-secret-wars', title: 'Avengers: Secret Wars', released: '2027-12-17', owned: false, groups: p6 }
];

function byRelease(entries: HubEntry[]): string[] {
  return [...entries].sort((a, b) => (a.released ?? '').localeCompare(b.released ?? '')).map(entry => entry.id);
}

function row(id: string, title: string, entryIds: string[], entries: Record<string, HubEntry>, extra: Partial<HubRow> = {}): HubRow {
  return { id, title, entryIds, playable: entryIds.some(entryId => entries[entryId]?.owned), ...extra };
}

function buildMcu(): UniverseHub {
  const list = mcuSeeds.map(seed => buildEntry(seed, seed.type && seed.type !== 'movie' ? 'tv' : 'movie'));
  const entries = Object.fromEntries(list.map(entry => [entry.id, entry]));
  const timeline = list.map(entry => entry.id);
  const inGroup = (group: string) => byRelease(list.filter(entry => entry.groups.includes(group)));
  const phaseNames = ['One', 'Two', 'Three', 'Four', 'Five', 'Six'];
  const phaseRows = phaseNames.map((name, index) =>
    row(`phase-${index + 1}`, `Phase ${name}`, inGroup(`phase-${index + 1}`), entries, {
      description: `Every Phase ${name} release, in release order.`,
      basis: 'Wikidata (CC0)'
    })
  );
  return {
    id: 'mcu',
    name: 'Marvel Cinematic Universe',
    description:
      'Every film and series from the Marvel Cinematic Universe, from the first Avenger to the Multiverse Saga, placed in the order the story unfolds.',
    revision: '2026.10.1',
    attribution: [
      { name: 'marvel.com Complete MCU Timeline (2026-06-02)' },
      { name: 'MCU Fandom', license: 'CC BY-SA' },
      { name: 'Wikidata', license: 'CC0' }
    ],
    flagLabels: {
      'network-tv': 'Network TV',
      netflix: 'Netflix series',
      'one-shot': 'One-Shots',
      animated: 'Animation',
      'alternate-universe': 'Alternate universes'
    },
    backdropUrl: serverImage('mcu-backdrop', 'Backdrop', 'Marvel Cinematic Universe', 225),
    primaryRowId: 'timeline',
    nextEntryId: 'agents-of-shield-s1',
    entries,
    rows: [
      row('timeline', 'Complete Timeline', timeline, entries, {
        description: 'Films, series and One-Shots in in-universe chronological order, season by season.',
        basis: 'Marvel.com Complete MCU Timeline (2026) + network shows placed via MCU Fandom'
      }),
      row('movie-timeline', 'Movie Timeline', timeline.filter(id => entries[id].type === 'movie'), entries, {
        description: 'Only the films, in chronological order.'
      }),
      row('infinity-saga', 'The Infinity Saga', inGroup('infinity-saga'), entries, { basis: 'Wikidata (CC0)' }),
      row('multiverse-saga', 'The Multiverse Saga', inGroup('multiverse-saga'), entries, { basis: 'Wikidata (CC0)' }),
      row('defenders', 'The Defenders Saga', timeline.filter(id => entries[id].flags.includes('netflix')), entries),
      row('release', 'Release Order', byRelease(list), entries, { description: 'Everything in the order it was released.' })
    ],
    pages: [
      { id: 'phases', title: 'Phases', rows: phaseRows },
      {
        id: 'sagas',
        title: 'Sagas',
        rows: [
          row('infinity-saga', 'The Infinity Saga', inGroup('infinity-saga'), entries),
          row('multiverse-saga', 'The Multiverse Saga', inGroup('multiverse-saga'), entries)
        ]
      }
    ],
    requestsEnabled: true
  };
}

const starWarsSeeds: Seed[] = [
  { id: 'phantom-menace', title: 'Star Wars: Episode I – The Phantom Menace', released: '1999-05-19', played: true, groups: [] },
  { id: 'attack-of-the-clones', title: 'Star Wars: Episode II – Attack of the Clones', released: '2002-05-16', played: true, groups: [] },
  { id: 'clone-wars-film', title: 'Star Wars: The Clone Wars', released: '2008-08-15', owned: false, flags: ['animated'], groups: [] },
  { id: 'clone-wars-s1', title: 'Star Wars: The Clone Wars', subtitle: 'Season 1', type: 'season', released: '2008-10-03', episodes: 22, flags: ['animated'], groups: [] },
  { id: 'revenge-of-the-sith', title: 'Star Wars: Episode III – Revenge of the Sith', released: '2005-05-19', groups: [] },
  { id: 'bad-batch-s1', title: 'Star Wars: The Bad Batch', subtitle: 'Season 1', type: 'season', released: '2021-05-04', owned: false, flags: ['animated'], groups: [] },
  { id: 'solo', title: 'Solo: A Star Wars Story', released: '2018-05-25', groups: [] },
  { id: 'andor-s1', title: 'Andor', subtitle: 'Season 1', type: 'season', released: '2022-09-21', episodes: 12, groups: [] },
  { id: 'rogue-one', title: 'Rogue One: A Star Wars Story', released: '2016-12-16', groups: [] },
  { id: 'a-new-hope', title: 'Star Wars: Episode IV – A New Hope', released: '1977-05-25', groups: [] },
  { id: 'empire-strikes-back', title: 'The Empire Strikes Back', released: '1980-05-21', groups: [] },
  { id: 'mandalorian-s1', title: 'The Mandalorian', subtitle: 'Season 1', type: 'season', released: '2019-11-12', owned: false, groups: [] }
];

function buildStarWars(): UniverseHub {
  hueSeed = 97;
  const list = starWarsSeeds.map(seed => buildEntry(seed, seed.type && seed.type !== 'movie' ? 'tv' : 'movie'));
  hueSeed = 0;
  const entries = Object.fromEntries(list.map(entry => [entry.id, entry]));
  const timeline = list.map(entry => entry.id);
  return {
    id: 'star-wars',
    name: 'Star Wars',
    description: 'A long time ago in a galaxy far, far away — the Skywalker Saga, the anthology films and the series, in timeline order.',
    attribution: [{ name: 'StarWars.com viewing guide (2026-09-03)' }],
    flagLabels: { animated: 'Animation' },
    primaryRowId: 'timeline',
    nextEntryId: 'clone-wars-s1',
    entries,
    rows: [
      row('timeline', 'Timeline Order', timeline, entries),
      row('release', 'Release Order', byRelease(list), entries)
    ],
    pages: [],
    requestsEnabled: false
  };
}

const johnWickSeeds: Seed[] = [
  { id: 'john-wick', title: 'John Wick', released: '2014-10-24', played: true, groups: [] },
  { id: 'john-wick-2', title: 'John Wick: Chapter 2', released: '2017-02-10', played: true, groups: [] },
  { id: 'john-wick-3', title: 'John Wick: Chapter 3 – Parabellum', released: '2019-05-17', played: true, groups: [] },
  { id: 'john-wick-4', title: 'John Wick: Chapter 4', released: '2023-03-24', played: true, groups: [] }
];

function buildJohnWick(): UniverseHub {
  hueSeed = 200;
  const list = johnWickSeeds.map(seed => buildEntry(seed, 'movie'));
  hueSeed = 0;
  const entries = Object.fromEntries(list.map(entry => [entry.id, entry]));
  return {
    id: 'john-wick',
    name: 'John Wick Collection',
    attribution: [],
    flagLabels: {},
    primaryRowId: 'release',
    entries,
    rows: [row('release', 'Release Order', list.map(entry => entry.id), entries)],
    pages: [],
    requestsEnabled: false
  };
}

export const hubs: Record<string, UniverseHub> = Object.fromEntries(
  [buildMcu(), buildStarWars(), buildJohnWick()].map(hub => [hub.id, hub])
);

export function summaries(): UniverseSummary[] {
  return Object.values(hubs).map(hub => {
    const entries = Object.values(hub.entries);
    const posters = [...entries.filter(entry => entry.owned), ...entries.filter(entry => !entry.owned)]
      .map(entry => entry.posterUrl)
      .filter((url): url is string => !!url)
      .slice(0, 6);
    return {
      id: hub.id,
      name: hub.name,
      description: hub.description,
      entryCount: entries.length,
      ownedCount: entries.filter(entry => entry.owned).length,
      posterUrls: posters,
      backdropUrl: hub.backdropUrl
    };
  });
}

export const requestStatuses: Record<string, Record<string, RequestStatus>> = {
  mcu: {
    'the-incredible-hulk': 'pending',
    'jessica-jones-s1': 'processing',
    'ant-man-and-the-wasp': 'available',
    eternals: 'none',
    'agent-carter-s2': 'none',
    'daredevil-s1': 'none',
    'what-if-s1': 'none',
    'fantastic-four': 'none',
    'spider-man-bnd': 'unknown',
    'avengers-doomsday': 'none',
    'avengers-secret-wars': 'none',
    'the-consultant': 'none'
  }
};
