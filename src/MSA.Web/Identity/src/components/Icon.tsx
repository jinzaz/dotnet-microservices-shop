import type { ReactNode, SVGProps } from 'react';

export type IconName =
  | 'arrow-right'
  | 'check'
  | 'eye'
  | 'eye-off'
  | 'headset'
  | 'lock'
  | 'mail'
  | 'phone'
  | 'refresh'
  | 'shield'
  | 'truck'
  | 'user';

const paths: Record<IconName, ReactNode> = {
  'arrow-right': <><path d="M5 12h14" /><path d="m14 7 5 5-5 5" /></>,
  check: <path d="m5 12 4 4L19 6" />,
  eye: <><path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" /><circle cx="12" cy="12" r="2.5" /></>,
  'eye-off': <><path d="M3 3 21 21" /><path d="M10.6 6.1A11 11 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-2.1 2.8M6.1 6.1C3.4 7.8 2 12 2 12s3.5 6 10 6c1 0 1.9-.1 2.7-.4" /><path d="M9.8 9.8a3 3 0 0 0 4.4 4.4" /></>,
  headset: <><path d="M4 13v-2a8 8 0 0 1 16 0v2" /><path d="M4 12H2v6h4v-6H4Zm16 0h2v6h-4v-6h2Z" /><path d="M18 18c0 2-1.5 3-4 3" /></>,
  lock: <><rect x="5" y="10" width="14" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" /></>,
  mail: <><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m4 7 8 6 8-6" /></>,
  phone: <path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 2 .7 2.9a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.2-1.2a2 2 0 0 1 2.1-.5c1 .4 1.9.6 2.9.7a2 2 0 0 1 1.7 2Z" />,
  refresh: <><path d="M20 7V3l-2 2a9 9 0 1 0 2.2 9" /><path d="M20 3h-5" /></>,
  shield: <path d="M12 2 4.5 5v6.2c0 4.8 2.9 8.8 7.5 10.8 4.6-2 7.5-6 7.5-10.8V5L12 2Z" />,
  truck: <><path d="M3 5h11v12H3zM14 9h4l3 4v4h-7" /><circle cx="7" cy="18" r="2" /><circle cx="18" cy="18" r="2" /></>,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4.5 20c.7-4.1 3.1-6 7.5-6s6.8 1.9 7.5 6" /></>,
};

export function Icon({ name, ...props }: SVGProps<SVGSVGElement> & { name: IconName }) {
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
