function escapeXml(text: string): string {
  return text.replace(/[<>&"']/g, char => `&#${char.charCodeAt(0)};`);
}

function wrap(text: string, maxChars: number): string[] {
  const lines: string[] = [];
  let current = '';
  for (const word of text.split(/\s+/)) {
    if (current && (current + ' ' + word).length > maxChars) {
      lines.push(current);
      current = word;
    } else {
      current = current ? `${current} ${word}` : word;
    }
  }
  if (current) lines.push(current);
  return lines.slice(0, 5);
}

export function posterSvg(label: string, hue: number, wide = false): string {
  const width = wide ? 1280 : 400;
  const height = wide ? 720 : 600;
  const lines = wide ? [] : wrap(label.toUpperCase(), 12);
  const fontSize = wide ? 64 : 40;
  const startY = height * (wide ? 0.5 : 0.62) - ((lines.length - 1) * fontSize * 1.1) / 2;
  const text = lines
    .map(
      (line, index) =>
        `<text x="${wide ? 96 : width / 2}" y="${startY + index * fontSize * 1.1}" text-anchor="${wide ? 'start' : 'middle'}" font-family="Arial, sans-serif" font-weight="800" font-size="${fontSize}" fill="rgba(255,255,255,0.92)">${escapeXml(line)}</text>`
    )
    .join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue},70%,46%)"/><stop offset="1" stop-color="hsl(${(hue + 40) % 360},60%,16%)"/></linearGradient><radialGradient id="r" cx="0.75" cy="0.2" r="0.7"><stop offset="0" stop-color="rgba(255,255,255,0.35)"/><stop offset="1" stop-color="rgba(255,255,255,0)"/></radialGradient></defs><rect width="${width}" height="${height}" fill="url(#g)"/><rect width="${width}" height="${height}" fill="url(#r)"/><circle cx="${width * 0.8}" cy="${height * 0.28}" r="${height * 0.22}" fill="none" stroke="rgba(255,255,255,0.18)" stroke-width="${wide ? 6 : 4}"/>${text}</svg>`;
}

export function svgDataUri(svg: string): string {
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}
