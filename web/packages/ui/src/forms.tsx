import { useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import type { ZodType } from 'zod';
import { ContentText } from './content';
export { z } from 'zod';

export type FieldErrors<T> = Partial<Record<Extract<keyof T, string>, string>>;
export type FormValidation<T> = { valid: true; data: T; errors: FieldErrors<T> } | { valid: false; errors: FieldErrors<T>; formErrorKey?: string };

/** Mechanics only: the owning domain supplies a typed schema and published message keys. */
export function validateTunnerForm<T>(schema: ZodType<T>, values: T): FormValidation<T> {
  const result = schema.safeParse(values);
  if (result.success) return { valid: true, data: result.data, errors: {} };
  const errors: FieldErrors<T> = {};
  let formErrorKey: string | undefined;
  for (const issue of result.error.issues) {
    const field = issue.path[0];
    if (typeof field === 'string') {
      const key = field as Extract<keyof T, string>;
      errors[key] ??= issue.message;
    } else formErrorKey ??= issue.message;
  }
  return { valid: false, errors, ...(formErrorKey ? { formErrorKey } : {}) };
}

export function useTunnerForm<T extends Record<string, unknown>>(schema: ZodType<T>, values: T) {
  const [touched, setTouched] = useState<Partial<Record<keyof T, boolean>>>({});
  const [submitted, setSubmitted] = useState(false);
  const [submitPending, setSubmitPending] = useState(false);
  const inFlight = useRef(false);
  const result = validateTunnerForm(schema, values);
  const errors = Object.fromEntries(Object.entries(result.errors).filter(([name]) => submitted || touched[name])) as FieldErrors<T>;
  function onBlur(name: Extract<keyof T, string>) { setTouched(current => ({ ...current, [name]: true })); }
  function reset() { setTouched({}); setSubmitted(false); }
  async function handleSubmit(event: FormEvent<HTMLFormElement>, onValid: (data: T) => void | Promise<void>) {
    event.preventDefault();
    if (inFlight.current) return;
    setSubmitted(true);
    const checked = validateTunnerForm(schema, values);
    if (!checked.valid) {
      const form = event.currentTarget;
      requestAnimationFrame(() => {
        if (!form.isConnected) return;
        const first = Array.from(form.elements).find(element => element instanceof HTMLElement &&
          'name' in element && typeof element.name === 'string' && checked.errors[element.name as Extract<keyof T, string>]);
        if (first instanceof HTMLElement) first.focus();
      });
      return;
    }
    inFlight.current = true; setSubmitPending(true);
    try { await onValid(checked.data); }
    finally { inFlight.current = false; setSubmitPending(false); }
  }
  return { errors, onBlur, reset, handleSubmit, submitPending, hasErrors: submitted && !result.valid,
    formErrorKey: submitted && !result.valid ? result.formErrorKey : undefined };
}

export function FieldError({ id, contentKey, children }: { id: string; contentKey: string; children?: ReactNode }) {
  return <span className="tn-field-error" id={id}><ContentText contentKey={contentKey} />{children}</span>;
}
export function FormErrorSummary({ contentKey, children }: { contentKey: string; children?: ReactNode }) {
  return <p className="tn-form-error-summary" role="alert"><ContentText contentKey={contentKey} />{children}</p>;
}
