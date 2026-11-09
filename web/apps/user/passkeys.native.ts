/** Standards-only browser conversion. Tunner never implements a cryptographic verifier here. */
export function passkeysSupported(){return window.isSecureContext&&typeof PublicKeyCredential!=='undefined'&&typeof navigator.credentials?.create==='function'&&typeof navigator.credentials?.get==='function'&&typeof PublicKeyCredential.parseCreationOptionsFromJSON==='function'&&typeof PublicKeyCredential.parseRequestOptionsFromJSON==='function'&&typeof PublicKeyCredential.prototype.toJSON==='function';}
function credentialJson(value:Credential|null,maximum:number):unknown{
  if(!(value instanceof PublicKeyCredential))throw new Error('A public-key credential response is required.');
  const result:unknown=value.toJSON();
  if(!result||typeof result!=='object'||!('type' in result)||result.type!=='public-key'||new TextEncoder().encode(JSON.stringify(result)).byteLength>maximum)throw new Error('The credential response is invalid.');
  return result;
}
export async function createPasskey(publicKey:unknown,maximum:number,signal:AbortSignal){if(!passkeysSupported())throw new Error('Passkeys unavailable.');const parsed=PublicKeyCredential.parseCreationOptionsFromJSON(publicKey as PublicKeyCredentialCreationOptionsJSON);return credentialJson(await navigator.credentials.create({publicKey:parsed,signal}),maximum);}
export async function getPasskey(publicKey:unknown,maximum:number,signal:AbortSignal){if(!passkeysSupported())throw new Error('Passkeys unavailable.');const parsed=PublicKeyCredential.parseRequestOptionsFromJSON(publicKey as PublicKeyCredentialRequestOptionsJSON);return credentialJson(await navigator.credentials.get({publicKey:parsed,signal}),maximum);}
