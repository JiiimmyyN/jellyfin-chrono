import type { PlayRequest, RequestStatus, UniverseHub, UniverseSummary } from '../types';
import { apiUrl, resolveServerUrl } from './url';

export interface JellyfinApiClient {
  serverAddress(): string;
  accessToken(): string | null;
  getCurrentUserId(): string | null;
  serverId(): string;
  getUrl?(path: string): string;
  setRequestHeaders?(headers: Record<string, string>): void;
}

declare global {
  interface Window {
    ApiClient?: JellyfinApiClient;
  }
}

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

export class NotSignedInError extends Error {
  constructor() {
    super('Sign in to Jellyfin to browse universes.');
  }
}

function readyClient(): JellyfinApiClient | undefined {
  const client = window.ApiClient;
  try {
    if (client && client.serverAddress() && client.accessToken()) return client;
  } catch {
    return undefined;
  }
  return undefined;
}

export function currentClient(): JellyfinApiClient | undefined {
  return readyClient();
}

export function waitForClient(timeoutMs = 15000, intervalMs = 200): Promise<JellyfinApiClient> {
  const ready = readyClient();
  if (ready) return Promise.resolve(ready);
  return new Promise((resolve, reject) => {
    const started = Date.now();
    const timer = window.setInterval(() => {
      const client = readyClient();
      if (client) {
        window.clearInterval(timer);
        resolve(client);
      } else if (Date.now() - started > timeoutMs) {
        window.clearInterval(timer);
        reject(new NotSignedInError());
      }
    }, intervalMs);
  });
}

export function authHeaders(client: JellyfinApiClient): Record<string, string> {
  const headers: Record<string, string> = {};
  try {
    client.setRequestHeaders?.(headers);
  } catch {
    delete headers.Authorization;
  }
  if (!headers.Authorization) {
    headers.Authorization = `MediaBrowser Token="${client.accessToken() ?? ''}"`;
  }
  return headers;
}

export function userKey(client: JellyfinApiClient): string {
  return `${client.serverId()}|${client.getCurrentUserId() ?? ''}`;
}

export function imageUrl(url: string | undefined): string | undefined {
  const client = window.ApiClient;
  if (!url) return undefined;
  try {
    return resolveServerUrl(url, client?.serverAddress() ?? '');
  } catch {
    return url;
  }
}

async function errorMessage(response: Response): Promise<string> {
  try {
    const text = await response.text();
    if (!text) return response.statusText || `HTTP ${response.status}`;
    try {
      const body = JSON.parse(text) as { title?: string; message?: string; detail?: string };
      return body.detail ?? body.message ?? body.title ?? text;
    } catch {
      return text.length > 200 ? `HTTP ${response.status}` : text;
    }
  } catch {
    return `HTTP ${response.status}`;
  }
}

async function request<T>(method: 'GET' | 'POST', segments: string[], body?: unknown): Promise<T> {
  const client = await waitForClient();
  const headers = authHeaders(client);
  headers.Accept = 'application/json';
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(apiUrl(client.serverAddress(), ...segments), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'same-origin'
  });
  if (!response.ok) throw new ApiError(response.status, await errorMessage(response));
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const chronoApi = {
  universes: () => request<UniverseSummary[]>('GET', ['Universes']),
  hub: (universeId: string) => request<UniverseHub>('GET', ['Universes', universeId]),
  requestStatus: (universeId: string) =>
    request<Record<string, RequestStatus>>('GET', ['Universes', universeId, 'RequestStatus']),
  request: (universeId: string, entryId: string) =>
    request<{ status: RequestStatus }>('POST', ['Universes', universeId, 'Entries', entryId, 'Request']),
  play: (universeId: string, body: PlayRequest) => request<void>('POST', ['Universes', universeId, 'Play'], body)
};

export function describeError(error: unknown): string {
  if (error instanceof NotSignedInError) return error.message;
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Your session has expired. Sign in again.';
    if (error.status === 403) return 'You do not have access to this.';
    if (error.status === 404) return 'Not found.';
    return error.message || `Server error (${error.status}).`;
  }
  if (error instanceof TypeError) return 'Could not reach the server.';
  return error instanceof Error ? error.message : 'Something went wrong.';
}
