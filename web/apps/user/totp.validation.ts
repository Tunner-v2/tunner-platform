import {z} from '@tunner/ui';

/** Mirrors the supported native boundary: bounded string, ASCII spaces/hyphens, six ASCII digits. */
export function canonicalTotpCode(value:string):string|null {
  if(value.length>32)return null;
  const code=value.replace(/[ -]/g,'');
  return /^[0-9]{6}$/.test(code)?code:null;
}
export function totpCodeSchema(keys:{required:string;invalid:string}={required:'totp.error.required',invalid:'totp.error.code'}) {
  return z.object({code:z.string().min(1,keys.required).refine(value=>canonicalTotpCode(value)!==null,keys.invalid).transform(value=>canonicalTotpCode(value)!)});
}
