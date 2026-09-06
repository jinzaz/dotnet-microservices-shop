import type { InputHTMLAttributes, ReactNode } from 'react';

type FormFieldProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  error?: string;
  leading?: ReactNode;
  trailing?: ReactNode;
};

export function FormField({
  label,
  error,
  leading,
  trailing,
  id,
  className = '',
  ...inputProps
}: FormFieldProps) {
  const errorId = error && id ? `${id}-error` : undefined;

  return (
    <div className={`form-field ${className}`.trim()}>
      <label htmlFor={id}>{label}</label>
      <div className={`form-field__control ${error ? 'is-invalid' : ''}`}>
        {leading ? <span className="form-field__leading">{leading}</span> : null}
        <input
          id={id}
          aria-invalid={Boolean(error)}
          aria-describedby={errorId}
          {...inputProps}
        />
        {trailing ? <span className="form-field__trailing">{trailing}</span> : null}
      </div>
      {error ? <p className="form-field__error" id={errorId}>{error}</p> : null}
    </div>
  );
}
