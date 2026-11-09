import { useId } from 'react';
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react';
import { ContentText, contentProvenance, usePublishedContent } from './content';
import { useThemeSurface } from './theme';
import { FieldError } from './forms';

type NativeButton = Pick<ButtonHTMLAttributes<HTMLButtonElement>, 'id' | 'type' | 'disabled' | 'onClick' | 'aria-busy' | 'aria-controls' | 'aria-expanded'>;
export interface PrimaryButtonProps extends NativeButton { labelKey: string; presentation?: 'standard' | 'spacious'; appearance?: 'filled' | 'outline' }
export function PrimaryButton({ labelKey, presentation = 'standard', appearance = 'filled', type = 'button', ...native }: PrimaryButtonProps) {
  useThemeSurface();
  return <button {...native} type={type} className="tn-primary-button" data-presentation={presentation} data-appearance={appearance}><ContentText contentKey={labelKey} /></button>;
}
type NativeInput = Pick<InputHTMLAttributes<HTMLInputElement>, 'id' | 'name' | 'type' | 'value' | 'onChange' | 'onBlur' | 'onFocus' | 'disabled' | 'readOnly' | 'required' | 'autoComplete' | 'inputMode' | 'maxLength' | 'minLength'>;
export interface TextFieldProps extends NativeInput { labelKey: string; helpKey?: string | undefined; errorKey?: string | undefined; errorDetails?: ReactNode; placeholderKey?: string; presentation?: 'standard' | 'spacious' | 'verification' }
export function TextField({ labelKey, helpKey, errorKey, errorDetails, placeholderKey, presentation = 'standard', id, type = 'text', ...native }: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const label = usePublishedContent(labelKey);
  const placeholder = usePublishedContent(placeholderKey ?? labelKey);
  const descriptions = [helpKey ? `${inputId}-help` : '', errorKey ? `${inputId}-error` : ''].filter(Boolean).join(' ') || undefined;
  return <div className="tn-text-field" data-presentation={presentation}>
    <label htmlFor={inputId} {...contentProvenance(label)}>{label.text}</label>
    <input {...native} {...(placeholderKey ? contentProvenance(placeholder) : {})} placeholder={placeholderKey ? placeholder.text : undefined} id={inputId} type={type} className="tn-input" aria-invalid={errorKey ? true : undefined} aria-describedby={descriptions} />
    {helpKey && <span className="tn-field-help" id={`${inputId}-help`}><ContentText contentKey={helpKey} /></span>}
    {errorKey && <FieldError id={`${inputId}-error`} contentKey={errorKey}>{errorDetails}</FieldError>}
  </div>;
}
export function TunnerBrand({ contentKey }: { contentKey: string }) {
  useThemeSurface();
  return <span className="tn-brand"><ContentText contentKey={contentKey} /></span>;
}
