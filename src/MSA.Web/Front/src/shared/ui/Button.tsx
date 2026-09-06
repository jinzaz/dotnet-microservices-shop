import type { ButtonHTMLAttributes, ReactNode } from 'react';

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger';
  size?: 'regular' | 'small';
  loading?: boolean;
  children: ReactNode;
};

export function Button({
  variant = 'primary',
  size = 'regular',
  loading = false,
  className = '',
  disabled,
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      className={`button button--${variant} button--${size} ${className}`.trim()}
      disabled={disabled || loading}
      aria-busy={loading}
      {...props}
    >
      {loading ? <span className="button__spinner" aria-hidden="true" /> : null}
      <span>{loading ? '处理中…' : children}</span>
    </button>
  );
}
