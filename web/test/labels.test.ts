import { describe, expect, it } from 'vitest';
import { canRequest, entryLabel, formatRuntime, isInProgress, ownedCountLabel, requestStatusLabel } from '../src/lib/labels';

describe('labels', () => {
  it('maps request statuses to badges', () => {
    expect(requestStatusLabel('pending')).toBe('Requested');
    expect(requestStatusLabel('processing')).toBe('Downloading');
    expect(requestStatusLabel('available')).toBe('Available');
    expect(requestStatusLabel('none')).toBeUndefined();
    expect(requestStatusLabel('unknown')).toBeUndefined();
    expect(requestStatusLabel(undefined)).toBeUndefined();
  });

  it('only allows requests for unrequested entries', () => {
    expect(canRequest('none')).toBe(true);
    expect(canRequest('unknown')).toBe(true);
    expect(canRequest(undefined)).toBe(true);
    expect(canRequest('pending')).toBe(false);
    expect(canRequest('available')).toBe(false);
  });

  it('formats entry labels and counts', () => {
    expect(entryLabel({ title: 'Loki', subtitle: 'Season 2' })).toBe('Loki: Season 2');
    expect(entryLabel({ title: 'Thor' })).toBe('Thor');
    expect(ownedCountLabel(12, 40)).toBe('12 of 40 in library');
  });

  it('formats runtimes', () => {
    expect(formatRuntime(181)).toBe('3h 1m');
    expect(formatRuntime(120)).toBe('2h');
    expect(formatRuntime(4)).toBe('4m');
    expect(formatRuntime(undefined)).toBeUndefined();
  });

  it('detects partial progress', () => {
    expect(isInProgress(0.5)).toBe(true);
    expect(isInProgress(0)).toBe(false);
    expect(isInProgress(1)).toBe(false);
    expect(isInProgress(undefined)).toBe(false);
  });
});
