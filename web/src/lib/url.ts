const absolutePattern = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

export function isAbsoluteUrl(url: string): boolean {
  return absolutePattern.test(url);
}

export function resolveServerUrl(url: string | undefined | null, serverAddress: string): string | undefined {
  if (!url) return undefined;
  if (isAbsoluteUrl(url)) return url;
  return `${serverAddress.replace(/\/+$/, '')}/${url.replace(/^\/+/, '')}`;
}

export function apiUrl(serverAddress: string, ...segments: string[]): string {
  const path = segments.map(segment => encodeURIComponent(segment)).join('/');
  return `${serverAddress.replace(/\/+$/, '')}/Chrono/${path}`;
}

export function siblingAssetUrl(scriptUrl: string, fileName: string): string {
  const url = new URL(fileName, scriptUrl);
  url.search = new URL(scriptUrl).search;
  return url.toString();
}
