export function localIsoDate(now: Date): string {
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function parseIsoDate(value: string): Date | undefined {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return undefined;
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
}

export function isUpcoming(released: string | undefined, year: number | undefined, now: Date): boolean {
  if (released && parseIsoDate(released)) return released > localIsoDate(now);
  return year !== undefined && year > now.getFullYear();
}

export function comingLabel(released: string | undefined, year: number | undefined, now: Date, locale?: string): string | undefined {
  if (!isUpcoming(released, year, now)) return undefined;
  const date = released ? parseIsoDate(released) : undefined;
  if (!date) return `Coming ${year}`;
  const options: Intl.DateTimeFormatOptions = date.getFullYear() === now.getFullYear()
    ? { month: 'short', day: 'numeric' }
    : { month: 'short', day: 'numeric', year: 'numeric' };
  return `Coming ${date.toLocaleDateString(locale, options)}`;
}

export function displayYear(released: string | undefined, year: number | undefined): number | undefined {
  if (year !== undefined) return year;
  const date = released ? parseIsoDate(released) : undefined;
  return date?.getFullYear();
}
