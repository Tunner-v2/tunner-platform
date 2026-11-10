import type {FocusEvent} from 'react';
import {TextField} from '@tunner/ui';

/** One code-field presentation/entry boundary for enrollment, primary MFA and operation proof. */
export function TotpCodeField({id,code,disabled=false,errorKey,onChange,onBlur,proof=false}:{id:string;code:string;disabled?:boolean;errorKey?:string|undefined;onChange:(value:string)=>void;onBlur?:(event:FocusEvent<HTMLInputElement>)=>void;proof?:boolean}){
  return <TextField id={id} name="code" labelKey={proof?'stepUp.totp.code.label':'totp.code.label'} placeholderKey={proof?'stepUp.totp.code.placeholder':'totp.code.placeholder'}
    type="text" inputMode="numeric" autoComplete="one-time-code" required maxLength={32} value={code} disabled={disabled} errorKey={errorKey}
    onChange={event=>onChange(event.target.value)} {...(onBlur?{onBlur}:{})}/>;
}
