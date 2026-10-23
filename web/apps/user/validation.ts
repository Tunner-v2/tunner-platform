import { z } from '@tunner/ui';
export interface RegistrationValidationPolicy { passwordMinimumLength: number; passwordMaximumLength: number; proofDigits: number }
const points = (value: string) => Array.from(value).length;
// Deliberately advisory; the independent .NET MailAddress and current policy validation remain authoritative.
export function plausibleEmail(value: string) { return value.trim().length <= 256 && /^(?:"(?:[^"\\\r\n]|\\[^\r\n])*"|[^\s@<>(),;:"\\]+)@[^\s@<>(),;:"\\]+$/.test(value.trim()); }
export function registrationSchema(policy: RegistrationValidationPolicy) {
  return z.object({
    email:z.string().refine(plausibleEmail,'registration.error.email'),
    password:z.string().refine(value=>points(value)>=policy.passwordMinimumLength&&points(value)<=policy.passwordMaximumLength,'registration.error.password'),
    password_confirmation:z.string(), notices:z.boolean().refine(value=>value,'registration.error.notices')
  }).refine(value=>value.password_confirmation.length>0&&value.password===value.password_confirmation,{path:['password_confirmation'],message:'registration.error.confirmation'});
}
export function verificationSchema(digits: number) { return z.object({verification_code:z.string().refine(value=>value.length===digits&&/^[0-9]+$/.test(value),'verification.error.code')}); }
export function validBirthDate(value: string, serverToday: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.slice(0,4)==='0000' || value>serverToday) return false;
  const [year,month,day]=value.split('-').map(Number); if(year===undefined||month===undefined||day===undefined)return false;
  const date=new Date(0);date.setUTCFullYear(year,month-1,day);
  return date.getUTCFullYear()===year&&date.getUTCMonth()===month-1&&date.getUTCDate()===day;
}
export function personalInformationSchema(fullNameLimit: number, displayNameLimit: number, serverToday: string) {
  return z.object({full_name:z.string().refine(value=>points(value)<=fullNameLimit,'personal.error.name'),display_name:z.string().refine(value=>points(value)<=displayNameLimit,'personal.error.name'),date_of_birth:z.string().refine(value=>validBirthDate(value,serverToday),'personal.error.birth')});
}
