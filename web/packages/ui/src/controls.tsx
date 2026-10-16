import { useId } from 'react';
import type { ButtonHTMLAttributes, InputHTMLAttributes } from 'react';
import { ContentText, contentProvenance, usePublishedContent } from './content';
import { useThemeSurface } from './theme';

type NativeButton = Pick<ButtonHTMLAttributes<HTMLButtonElement>, 'id' | 'type' | 'disabled' | 'onClick' | 'aria-busy' | 'aria-controls' | 'aria-expanded'>;
export interface PrimaryButtonProps extends NativeButton { labelKey: string }
export function PrimaryButton({ labelKey, type = 'button', ...native }: PrimaryButtonProps) {
  useThemeSurface();
  return <button {...native} type={type} className="tn-primary-button"><ContentText contentKey={labelKey} /></button>;
}
type NativeInput = Pick<InputHTMLAttributes<HTMLInputElement>, 'id' | 'name' | 'type' | 'value' | 'onChange' | 'onBlur' | 'onFocus' | 'disabled' | 'readOnly' | 'required' | 'autoComplete' | 'inputMode' | 'maxLength' | 'minLength'>;
export interface TextFieldProps extends NativeInput { labelKey: string; helpKey?: string; errorKey?: string }
export function TextField({ labelKey, helpKey, errorKey, id, type = 'text', ...native }: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const label = usePublishedContent(labelKey);
  const descriptions = [helpKey ? `${inputId}-help` : '', errorKey ? `${inputId}-error` : ''].filter(Boolean).join(' ') || undefined;
  return <div className="tn-text-field">
    <label htmlFor={inputId} {...contentProvenance(label)}>{label.text}</label>
    <input {...native} id={inputId} type={type} className="tn-input" aria-invalid={errorKey ? true : undefined} aria-describedby={descriptions} />
    {helpKey && <span className="tn-field-help" id={`${inputId}-help`}><ContentText contentKey={helpKey} /></span>}
    {errorKey && <span className="tn-field-error" id={`${inputId}-error`}><ContentText contentKey={errorKey} /></span>}
  </div>;
}
export function TunnerBrand({ contentKey }: { contentKey: string }) {
  useThemeSurface();
  return <span className="tn-brand"><ContentText contentKey={contentKey} /></span>;
}
