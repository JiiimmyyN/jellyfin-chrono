import { hubs, requestStatuses, summaries } from './fixtures';

const mockBase = `${location.origin}/mock`;
const params = new URLSearchParams(location.search);

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

async function handle(method: string, path: string, body: string | undefined): Promise<Response> {
  const latency = Number(params.get('latency') ?? 450);
  await delay(latency);
  if (params.get('fail') === 'load' && method === 'GET') return json({ title: 'Internal Server Error' }, 500);

  if (method === 'GET' && path === 'Chrono/Universes') {
    return json(params.get('empty') === '1' ? [] : summaries());
  }

  let match = /^Chrono\/Universes\/([^/]+)$/.exec(path);
  if (method === 'GET' && match) {
    const hub = hubs[decodeURIComponent(match[1])];
    return hub ? json(hub) : json({ title: 'Not Found' }, 404);
  }

  match = /^Chrono\/Universes\/([^/]+)\/RequestStatus$/.exec(path);
  if (method === 'GET' && match) {
    await delay(400);
    return json(requestStatuses[match[1]] ?? {});
  }

  match = /^Chrono\/Universes\/([^/]+)\/Entries\/([^/]+)\/Request$/.exec(path);
  if (method === 'POST' && match) {
    await delay(500);
    if (match[2] === 'the-consultant') return json({ title: 'Seerr returned 500: upstream timeout' }, 502);
    const statuses = (requestStatuses[match[1]] ??= {});
    statuses[match[2]] = 'pending';
    return json({ status: 'pending' });
  }

  match = /^Chrono\/Universes\/([^/]+)\/Play$/.exec(path);
  if (method === 'POST' && match) {
    console.info('[mock] Play', match[1], body);
    if (params.get('fail') === 'play') return json({ title: 'No session' }, 404);
    return new Response(null, { status: 204 });
  }

  return json({ title: 'Not Found' }, 404);
}

export function installMockApi(): void {
  const realFetch = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    if (!url.startsWith(mockBase + '/')) return realFetch(input, init);
    const headers = new Headers(init?.headers);
    if (!headers.get('Authorization')?.includes('Token="dev-token"')) return json({ title: 'Unauthorized' }, 401);
    const path = url.slice(mockBase.length + 1).split('?')[0];
    return handle(init?.method ?? 'GET', path, init?.body as string | undefined);
  };

  const userId = 'dev-user';
  const client = {
    _deviceId: 'dev-device',
    serverAddress: () => mockBase,
    accessToken: () => 'dev-token',
    getCurrentUserId: () => userId,
    serverId: () => 'dev-server',
    getUrl: (path: string) => `${mockBase}/${path.replace(/^\//, '')}`,
    setRequestHeaders(headers: Record<string, string>) {
      headers.Authorization = `MediaBrowser Client="Jellyfin Web", Device="Dev", DeviceId="${this._deviceId}", Version="12.2.0", Token="dev-token"`;
    }
  };
  setTimeout(() => {
    window.ApiClient = client;
  }, 300);
}
