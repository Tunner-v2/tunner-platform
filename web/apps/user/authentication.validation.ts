import { z } from '@tunner/ui';
import { plausibleEmail } from './validation';
/** Existing credentials are not subject to new-password registration rules. Server policy remains authoritative. */
export function passwordSignInSchema(maximum: number) {
  return z.object({ email:z.string().refine(plausibleEmail,'authentication.error.email'),
    password:z.string().refine(value=>Array.from(value).length>0&&Array.from(value).length<=maximum,'authentication.error.password'),remember:z.boolean() });
}
