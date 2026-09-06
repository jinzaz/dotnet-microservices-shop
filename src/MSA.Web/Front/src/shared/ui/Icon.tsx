import type { ReactNode, SVGProps } from 'react';

export type IconName =
  | 'search'
  | 'user'
  | 'bag'
  | 'menu'
  | 'close'
  | 'arrow-right'
  | 'sofa'
  | 'phone'
  | 'luggage'
  | 'cup'
  | 'shield'
  | 'truck'
  | 'refresh'
  | 'headset'
  | 'mail'
  | 'lock'
  | 'eye'
  | 'eye-off'
  | 'home'
  | 'map-pin'
  | 'star'
  | 'clipboard'
  | 'logout'
  | 'coins'
  | 'calendar'
  | 'gift'
  | 'receipt'
  | 'edit'
  | 'plus'
  | 'trash'
  | 'check'
  | 'chevron-right'
  | 'heart';

const paths: Record<IconName, ReactNode> = {
  search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4.5 20c.7-4.1 3.1-6 7.5-6s6.8 1.9 7.5 6" /></>,
  bag: <><path d="M5 8h14l-1 13H6L5 8Z" /><path d="M9 9V6a3 3 0 0 1 6 0v3" /></>,
  menu: <><path d="M4 7h16M4 12h16M4 17h16" /></>,
  close: <path d="m5 5 14 14M19 5 5 19" />,
  'arrow-right': <><path d="M5 12h14" /><path d="m14 7 5 5-5 5" /></>,
  sofa: <><path d="M5 11V8a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v3" /><path d="M4 11a2 2 0 0 0-2 2v5h20v-5a2 2 0 0 0-2-2" /><path d="M5 18v2m14-2v2" /></>,
  phone: <><rect x="7" y="2" width="10" height="20" rx="2" /><path d="M11 18h2" /></>,
  luggage: <><rect x="7" y="5" width="10" height="17" rx="2" /><path d="M10 5V2h4v3M4 9v9m16-9v9M10 19h4" /></>,
  cup: <><path d="M4 5h13v7a6 6 0 0 1-6 6H10a6 6 0 0 1-6-6V5Z" /><path d="M17 7h2a3 3 0 0 1 0 6h-2M3 21h16" /></>,
  shield: <path d="M12 2 4.5 5v6.2c0 4.8 2.9 8.8 7.5 10.8 4.6-2 7.5-6 7.5-10.8V5L12 2Z" />,
  truck: <><path d="M3 5h11v12H3zM14 9h4l3 4v4h-7" /><circle cx="7" cy="18" r="2" /><circle cx="18" cy="18" r="2" /></>,
  refresh: <><path d="M20 7V3l-2 2a9 9 0 1 0 2.2 9" /><path d="M20 3h-5" /></>,
  headset: <><path d="M4 13v-2a8 8 0 0 1 16 0v2" /><path d="M4 12H2v6h4v-6H4Zm16 0h2v6h-4v-6h2Z" /><path d="M18 18c0 2-1.5 3-4 3" /></>,
  mail: <><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m4 7 8 6 8-6" /></>,
  lock: <><rect x="5" y="10" width="14" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" /></>,
  eye: <><path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" /><circle cx="12" cy="12" r="2.5" /></>,
  'eye-off': <><path d="M3 3 21 21" /><path d="M10.6 6.1A11 11 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-2.1 2.8M6.1 6.1C3.4 7.8 2 12 2 12s3.5 6 10 6c1 0 1.9-.1 2.7-.4" /><path d="M9.8 9.8a3 3 0 0 0 4.4 4.4" /></>,
  home: <><path d="m3 11 9-8 9 8" /><path d="M5 10v11h14V10M9 21v-7h6v7" /></>,
  'map-pin': <><path d="M20 10c0 5-8 12-8 12S4 15 4 10a8 8 0 1 1 16 0Z" /><circle cx="12" cy="10" r="2.5" /></>,
  star: <path d="m12 2.5 3 6 6.5 1-4.7 4.6 1.1 6.4-5.9-3.1-5.9 3.1 1.1-6.4-4.7-4.6 6.5-1 3-6Z" />,
  clipboard: <><rect x="5" y="4" width="14" height="18" rx="2" /><path d="M9 4V2h6v2M9 9h6M9 13h6M9 17h4" /></>,
  logout: <><path d="M10 4H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5" /><path d="M14 8l4 4-4 4M8 12h10" /></>,
  coins: <><ellipse cx="9" cy="6" rx="6" ry="3" /><path d="M3 6v5c0 1.7 2.7 3 6 3 1.2 0 2.3-.2 3.2-.5M3 11v5c0 1.7 2.7 3 6 3 .8 0 1.6-.1 2.3-.3" /><circle cx="17" cy="16" r="4" /></>,
  calendar: <><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M7 2v6m10-6v6M3 10h18M8 14h.01M12 14h.01M16 14h.01" /></>,
  gift: <><rect x="3" y="9" width="18" height="12" rx="1" /><path d="M12 9v12M2 9h20V5H2v4ZM12 5c-1-3-5-3-5-1s3 1 5 1Zm0 0c1-3 5-3 5-1s-3 1-5 1Z" /></>,
  receipt: <><path d="M6 3h12v19l-3-2-3 2-3-2-3 2V3Z" /><path d="M9 8h6M9 12h6M9 16h4" /></>,
  edit: <><path d="M4 20h4L19 9l-4-4L4 16v4Z" /><path d="m13.5 6.5 4 4" /></>,
  plus: <path d="M12 5v14M5 12h14" />,
  trash: <><path d="M4 7h16M9 7V4h6v3M6 7l1 14h10l1-14M10 11v6m4-6v6" /></>,
  check: <path d="m5 12 4 4L19 6" />,
  'chevron-right': <path d="m9 5 7 7-7 7" />,
  heart: <path d="M20.8 5.8a5.5 5.5 0 0 0-7.8 0L12 6.9l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 22l8.8-8.4a5.5 5.5 0 0 0 0-7.8Z" />,
};

type IconProps = SVGProps<SVGSVGElement> & { name: IconName };

export function Icon({ name, ...props }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {paths[name]}
    </svg>
  );
}
