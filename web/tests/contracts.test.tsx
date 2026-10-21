import { authenticationRoutes } from '../apps/user/authentication.routes';
import { registrationSchema, verificationSchema, personalInformationSchema, validBirthDate } from '../apps/user/validation';
import { registrationRoutes } from '../apps/user/routes';
import inventory from '../../docs/contracts/ui-inventory.json' with { type: 'json' };
import onboarding from '../../docs/contracts/registration-onboarding.openapi.json' with { type: 'json' };
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { renderToStaticMarkup } from 'react-dom/server';
import type { ReactNode } from 'react';
import { ThemeProvider, ContentProvider, PrimaryButton, TextField, TunnerBrand, z, validateTunnerForm, FormErrorSummary } from '../packages/ui/src/index';
import type { PublishedContentResolver, ThemeMode, ThemeSurface } from '../packages/ui/src/index';

const published: PublishedContentResolver = (key, context) => ({ key, text: key === 'brand' ? 'Tunner' : `<${key}>`, locale: context.locale, state: 'PUBLISHED', variantRef: 'fixture', versionRef: 'v1', publicationRef: 'fixture-only' });
function wrap(children: ReactNode, resolve = published, surface: ThemeSurface = 'user') { return <ThemeProvider surface={surface}><ContentProvider locale="en" audience="verification" resolve={resolve}>{children}</ContentProvider></ThemeProvider>; }
const render = (children: ReactNode, resolver?: PublishedContentResolver) => renderToStaticMarkup(wrap(children, resolver));
test('SSR is browser-independent with plain brand and no icon', () => { const html = render(<TunnerBrand contentKey="brand"/>); assert.match(html, />Tunner</); assert.doesNotMatch(html, /svg|img|window|storage/i); assert.match(html, /data-tunner-theme="light"/); });
test('native button defaults to button, with explicit owner submit supported', () => { assert.match(render(<PrimaryButton labelKey="action"/>), /type="button"/); assert.match(render(<PrimaryButton labelKey="action" type="submit"/>), /type="submit"/); });
test('owner disabled and busy state preserved', () => { const html = render(<PrimaryButton labelKey="action" disabled aria-busy/>); assert.match(html, /disabled=""/); assert.match(html, /aria-busy="true"/); });
test('content text is escaped and provenance is preserved', () => { const html = render(<PrimaryButton labelKey="script"/>); assert.match(html, /&lt;script&gt;/); assert.doesNotMatch(html, /<script>/); assert.match(html, /data-content-version="v1"/); assert.match(html, /data-content-publication="fixture-only"/); });
test('missing required key fails without fallback', () => { assert.throws(() => render(<PrimaryButton labelKey="missing"/>, () => undefined), /Invalid published content/); });
test('mismatched key or locale and unpublished snapshots fail', () => {
  for (const override of [{key:'other'},{locale:'fr'},{state:'DRAFT'}]) assert.throws(() => render(<PrimaryButton labelKey="action"/>, (key, context) => ({...published(key, context)!, ...override}) as never), /Invalid published content/);
});
test('missing provenance and blank required copy fail', () => { for (const property of ['variantRef','versionRef','publicationRef','text']) assert.throws(() => render(<PrimaryButton labelKey="action"/>, (key, context) => ({...published(key, context)!, [property]: ''})), /Invalid published content/); });
test('resolver receives bounded surface/locale/audience context', () => { renderToStaticMarkup(wrap(<PrimaryButton labelKey="action"/>, (key, context) => { assert.deepEqual(context, {surface:'admin',locale:'en',audience:'verification'}); assert.ok(Object.isFrozen(context)); return published(key, context); }, 'admin')); });
test('required theme/content boundaries fail explicitly', () => { assert.throws(() => renderToStaticMarkup(<PrimaryButton labelKey="action"/>), /ThemeProvider/); assert.throws(() => renderToStaticMarkup(<ThemeProvider surface="user"><PrimaryButton labelKey="action"/></ThemeProvider>), /provider missing/); });
test('invalid modes/surfaces fail rather than silently applying defaults', () => { assert.throws(() => renderToStaticMarkup(<ThemeProvider surface={'other' as ThemeSurface}>x</ThemeProvider>), /Unsupported/); assert.throws(() => renderToStaticMarkup(<ThemeProvider surface="user" mode={'dark' as ThemeMode}>x</ThemeProvider>), /Unsupported/); });
test('empty locale/audience is rejected', () => { assert.throws(() => renderToStaticMarkup(<ContentProvider locale="" audience="verification" resolve={published}>x</ContentProvider>), /context required/); assert.throws(() => renderToStaticMarkup(<ContentProvider locale="en" audience="" resolve={published}>x</ContentProvider>), /context required/); });
test('field label/help/error IDs and noncolor text preserve associations', () => { const html = render(<TextField id="sample" labelKey="label" helpKey="help" errorKey="error" value="owner data" readOnly/>); assert.match(html, /for="sample"/); assert.match(html, /aria-describedby="sample-help sample-error"/); assert.match(html, /aria-invalid="true"/); assert.match(html, /id="sample-error"/); assert.match(html, /&lt;error&gt;/); assert.match(html, /value="owner data"/); });
test('absent optional slots stay absent', () => { const html = render(<TextField labelKey="label" value="" readOnly/>); assert.doesNotMatch(html, /aria-invalid|aria-describedby|field-error|field-help/); });
test('generated label IDs are unique', () => { const html = render(<><TextField labelKey="a" value="" readOnly/><TextField labelKey="b" value="" readOnly/></>); const ids = [...html.matchAll(/<input[^>]*id="([^"]+)"/g)].map(m => m[1]); assert.equal(ids.length,2); assert.notEqual(ids[0],ids[1]); });
test('nested surface providers preserve independent DOM boundaries', () => { const html = renderToStaticMarkup(wrap(<><PrimaryButton labelKey="outer"/>{wrap(<PrimaryButton labelKey="inner"/>, published, 'admin')}<PrimaryButton labelKey="outer-again"/></>)); assert.match(html, /data-tunner-surface="user"/); assert.match(html, /data-tunner-surface="admin"/); });

test('placeholder requires its own published copy and provenance while retaining the visible label', () => {
  const html=render(<TextField id="published-placeholder" labelKey="label" placeholderKey="placeholder" value="" readOnly/>);
  assert.match(html,/for="published-placeholder"/); assert.match(html,/placeholder="&lt;placeholder&gt;"/);
  assert.match(html,/<input[^>]*data-content-key="placeholder"/);
  assert.throws(()=>render(<TextField labelKey="label" placeholderKey="placeholder" value="" readOnly/>,(key,context)=>key==='placeholder'?undefined:published(key,context)),/Invalid published content/);
});

// Shared mechanics carry only the owning schema's message keys and cross-field policy.

test('typed schemas enforce required, format, range and cross-field rules using domain keys', () => {
 const schema = z.object({ email:z.string().email('example.email'), amount:z.number().min(1,'example.range'), confirmation:z.string() }).refine(value=>value.confirmation==='confirmed',{path:['confirmation'],message:'example.confirmation'});
 const result = validateTunnerForm(schema,{email:'bad',amount:0,confirmation:''});
 assert.equal(result.valid,false);assert.deepEqual(result.errors,{email:'example.email',amount:'example.range',confirmation:'example.confirmation'});
 const corrected=validateTunnerForm(schema,{email:'example@example.test',amount:1,confirmation:'confirmed'});assert.equal(corrected.valid,true);assert.deepEqual(corrected.errors,{});
});
test('schema form-level errors and published summary remain separate from field rules',()=>{
 const schema=z.object({value:z.string()}).refine(()=>false,{message:'example.form'});const result=validateTunnerForm(schema,{value:'safe'});assert.equal(result.valid,false);if(!result.valid)assert.equal(result.formErrorKey,'example.form');
 const html=render(<FormErrorSummary contentKey="example.form"/>);assert.match(html,/role="alert"/);assert.match(html,/data-content-key="example.form"/);assert.doesNotMatch(html,/<example.form>/);
});

test('domain schemas use current policy and do not inherit historical Figma password or age restrictions',()=>{
 const policy={passwordMinimumLength:15,passwordMaximumLength:128,proofDigits:6};const password='a'.repeat(15);const schema=registrationSchema(policy);
 assert.equal(validateTunnerForm(schema,{email:'person@example.test',password,password_confirmation:password,notices:true}).valid,true);
 const invalid=validateTunnerForm(schema,{email:'bad',password:'short',password_confirmation:'other',notices:false});assert.equal(invalid.valid,false);assert.deepEqual(Object.keys(invalid.errors).sort(),['email','notices','password','password_confirmation']);
 const unicode='😀'.repeat(15);assert.equal(validateTunnerForm(schema,{email:'person@localhost',password:unicode,password_confirmation:unicode,notices:true}).valid,true);
 assert.equal(validateTunnerForm(verificationSchema(6),{verification_code:'12345'}).valid,false);assert.equal(validateTunnerForm(verificationSchema(6),{verification_code:'１２３４５６'}).valid,false);assert.equal(validateTunnerForm(verificationSchema(6),{verification_code:'123456'}).valid,true);
 const profile=personalInformationSchema(4,4,'2026-10-17');assert.equal(validateTunnerForm(profile,{full_name:'😀😀😀😀',display_name:'',date_of_birth:'2015-01-01'}).valid,true);assert.equal(validateTunnerForm(profile,{full_name:'😀😀😀😀😀',display_name:'',date_of_birth:'2015-01-01'}).valid,false);
});
test('DOB schema is calendar-safe and rejects future dates without client age eligibility policy',()=>{
 for(const date of ['2025-02-29','2024-02-30','0000-01-01','2026-10-18','not-a-date'])assert.equal(validBirthDate(date,'2026-10-17'),false,date);
 for(const date of ['2024-02-29','0099-01-01','2026-10-17','2015-01-01'])assert.equal(validBirthDate(date,'2026-10-17'),true,date);
});
test('every implemented screen route agrees with the central inventory and known BFF fallback contract',()=>{
 const rows=inventory.items.filter(row=>row.kind==='SCREEN'&&row.status==='IMPLEMENTED'&&row.app==='user');const routes=[...Object.values(registrationRoutes),...Object.values(authenticationRoutes)];assert.equal(rows.length,routes.length);
 for(const route of routes){const row=rows.find(r=>r.id===route.id);assert.ok(row);assert.equal(row.pathname,route.pathname);assert.equal(row.runtime_pathname,route.pathname);assert.ok(row.guard);assert.ok(row.figma_refs.length);}
 assert.deepEqual(Object.values(registrationRoutes).map(r=>r.pathname),onboarding['x-tunner-screen-paths']);
 const forms=inventory.items.filter(row=>row.kind==='FORM'&&row.status==='IMPLEMENTED');const formScreens=new Set(routes.map(route=>route.pathname));assert.ok(forms.every(form=>form.pathname!==null&&formScreens.has(form.pathname as typeof routes[number]['pathname'])));assert.ok(forms.some(form=>form.id==='FORM-LOGIN'));assert.equal(new Set(forms.map(form=>form.id)).size,forms.length);
});
