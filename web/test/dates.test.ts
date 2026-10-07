import { describe, expect, it } from 'vitest';
import { comingLabel, displayYear, isUpcoming, localIsoDate } from '../src/lib/dates';

const now = new Date(2026, 9, 7, 15, 30);

describe('isUpcoming', () => {
  it('compares release dates with the local date', () => {
    expect(isUpcoming('2026-10-08', undefined, now)).toBe(true);
    expect(isUpcoming('2026-10-07', undefined, now)).toBe(false);
    expect(isUpcoming('2026-07-31', undefined, now)).toBe(false);
  });

  it('falls back to the year', () => {
    expect(isUpcoming(undefined, 2027, now)).toBe(true);
    expect(isUpcoming(undefined, 2026, now)).toBe(false);
    expect(isUpcoming(undefined, undefined, now)).toBe(false);
    expect(isUpcoming('garbage', 2028, now)).toBe(true);
  });
});

describe('comingLabel', () => {
  it('omits the year for releases this year', () => {
    expect(comingLabel('2026-12-18', undefined, now, 'en-US')).toBe('Coming Dec 18');
  });

  it('includes the year for later years', () => {
    expect(comingLabel('2027-12-17', undefined, now, 'en-US')).toBe('Coming Dec 17, 2027');
  });

  it('uses the year when the date is unknown', () => {
    expect(comingLabel(undefined, 2028, now)).toBe('Coming 2028');
  });

  it('returns undefined for released titles', () => {
    expect(comingLabel('2008-05-02', 2008, now)).toBeUndefined();
  });
});

describe('helpers', () => {
  it('formats the local date', () => {
    expect(localIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('derives the display year', () => {
    expect(displayYear('2019-04-26', undefined)).toBe(2019);
    expect(displayYear(undefined, 2008)).toBe(2008);
    expect(displayYear(undefined, undefined)).toBeUndefined();
  });
});
