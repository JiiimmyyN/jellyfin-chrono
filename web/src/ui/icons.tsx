import type { JSX } from 'solid-js';

function Icon(props: { d: string; size?: number; class?: string }): JSX.Element {
  return (
    <svg
      class={props.class ?? 'chrono-icon'}
      width={props.size ?? 20}
      height={props.size ?? 20}
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <path d={props.d} fill="currentColor" />
    </svg>
  );
}

const paths = {
  play: 'M8 5.14v13.72a1 1 0 0 0 1.52.85l11.03-6.86a1 1 0 0 0 0-1.7L9.52 4.29A1 1 0 0 0 8 5.14Z',
  restart: 'M12 5V2L7 6l5 4V7a6 6 0 1 1-6 6H4a8 8 0 1 0 8-8Z',
  check: 'M9.55 17.6 4.4 12.45l1.4-1.4 3.75 3.75 8.65-8.65 1.4 1.4Z',
  info: 'M11 17h2v-6h-2v6Zm1-8a1.25 1.25 0 1 0 0-2.5A1.25 1.25 0 0 0 12 9Zm0 13a10 10 0 1 1 0-20 10 10 0 0 1 0 20Z',
  chevronLeft: 'M15.4 7.4 14 6l-6 6 6 6 1.4-1.4-4.6-4.6Z',
  chevronRight: 'M8.6 16.6 10 18l6-6-6-6-1.4 1.4 4.6 4.6Z',
  more: 'M12 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm0 6a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm0 6a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z',
  close: 'M18.3 5.7 16.9 4.3 12 9.2 7.1 4.3 5.7 5.7l4.9 4.9-4.9 4.9 1.4 1.4 4.9-4.9 4.9 4.9 1.4-1.4-4.9-4.9Z',
  plus: 'M11 19v-6H5v-2h6V5h2v6h6v2h-6v6Z',
  details: 'M4 6h16v2H4V6Zm0 5h16v2H4v-2Zm0 5h10v2H4v-2Z',
  filter: 'M3 5h18l-7 8.5V20l-4-2v-4.5Z',
  back: 'M20 11H7.8l5.6-5.6L12 4l-8 8 8 8 1.4-1.4L7.8 13H20Z',
  universe: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm6.9 6h-2.9a15.7 15.7 0 0 0-1.4-3.6A8 8 0 0 1 18.9 8ZM12 4c.8 1.2 1.5 2.5 1.9 4h-3.8c.4-1.5 1.1-2.8 1.9-4ZM4.3 14a8.2 8.2 0 0 1 0-4h3.4a16.5 16.5 0 0 0 0 4H4.3Zm.8 2h3a15.7 15.7 0 0 0 1.3 3.6A8 8 0 0 1 5.1 16Zm3-8h-3a8 8 0 0 1 4.3-3.6C8.9 5.5 8.4 6.7 8.1 8ZM12 20c-.8-1.2-1.5-2.5-1.9-4h3.8c-.4 1.5-1.1 2.8-1.9 4Zm2.3-6H9.7a14.7 14.7 0 0 1 0-4h4.6a14.7 14.7 0 0 1 0 4Zm.3 5.6c.6-1.1 1-2.3 1.4-3.6h2.9a8 8 0 0 1-4.3 3.6Zm1.7-5.6a16.5 16.5 0 0 0 0-4h3.4a8.2 8.2 0 0 1 0 4h-3.4Z'
} as const;

export type IconName = keyof typeof paths;

export function SvgIcon(props: { name: IconName; size?: number; class?: string }): JSX.Element {
  return <Icon d={paths[props.name]} size={props.size} class={props.class} />;
}
