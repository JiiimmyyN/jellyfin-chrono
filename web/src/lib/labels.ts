import type { HubEntry, RequestStatus } from '../types';

export function requestStatusLabel(status: RequestStatus | undefined): string | undefined {
  switch (status) {
    case 'pending':
      return 'Requested';
    case 'processing':
      return 'Downloading';
    case 'available':
      return 'Available';
    case 'partial':
      return 'Partially available';
    default:
      return undefined;
  }
}

export function canRequest(status: RequestStatus | undefined): boolean {
  return status === undefined || status === 'none' || status === 'unknown';
}

export function entryLabel(entry: Pick<HubEntry, 'title' | 'subtitle'>): string {
  return entry.subtitle ? `${entry.title}: ${entry.subtitle}` : entry.title;
}

export function ownedCountLabel(owned: number, total: number): string {
  return `${owned} of ${total} in library`;
}

export function formatRuntime(minutes: number | undefined): string | undefined {
  if (!minutes || minutes <= 0) return undefined;
  const hours = Math.floor(minutes / 60);
  const rest = Math.round(minutes % 60);
  if (hours === 0) return `${rest}m`;
  return rest === 0 ? `${hours}h` : `${hours}h ${rest}m`;
}

export function isInProgress(progress: number | undefined): progress is number {
  return progress !== undefined && progress > 0 && progress < 1;
}
