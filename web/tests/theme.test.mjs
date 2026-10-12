import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { compileTheme, build, readThemeSource, verifyCurrentOutput } from '../packages/ui/tools/build-theme.mjs';
const source = JSON.parse(readFileSync(new URL('../packages/ui/theme.tokens.json', import.meta.url), 'utf8'));
const clone = () => structuredClone(source);
test('deterministic compiled profiles preserve exact Figma control roles', () => {
  const output = compileTheme(source); assert.deepEqual(output, compileTheme(clone()));
  assert.match(output.css, /--tn-component-button-background: #4f46e5;/);
  assert.match(output.css, /--tn-component-button-background: #09090a;/);
  assert.match(output.css, /--tn-component-field-height: 44px;/);
  assert.match(output.css, /--tn-component-field-height: 42px;/);
  assert.match(output.types, /themeModes = \["light"\]/);
  assert.match(output.bridge, /@theme inline/); assert.match(output.bridge, /var\(--tn-semantic-action-background\)/);
});
test('unknown alias fails with the responsible reference', () => { const s = clone(); s.components['button-color'] = '{primitive.unknown}'; assert.throws(() => compileTheme(s), /Unknown token primitive.unknown/); });
test('component alias cycle fails', () => { const s = clone(); s.components['button-color'] = '{component.button-color}'; assert.throws(() => compileTheme(s), /cycle/); });
test('missing approved surface and incomplete semantic profile fail', () => {
  const s = clone(); delete s.profiles.admin; assert.throws(() => compileTheme(s), /Both approved surfaces/);
  const t = clone(); delete t.profiles.admin.light.font; assert.throws(() => compileTheme(t), /Incomplete semantic profile/);
});
test('future mode requires a complete profile for both surfaces', () => {
  const s = clone(); s.profiles.user.example = structuredClone(s.profiles.user.light); assert.throws(() => compileTheme(s), /Incomplete modes/);
  s.profiles.admin.example = structuredClone(s.profiles.admin.light); assert.match(compileTheme(s).types, /"example","light"/);
});
test('noncanonical token names and CSS injection are rejected', () => {
  const s = clone(); s.primitives['white_color'] = '#fff'; assert.throws(() => compileTheme(s), /Invalid token/);
  const t = clone(); t.primitives.white = '#fff; color: red'; assert.throws(() => compileTheme(t), /Invalid token/);
});
test('unsupported schema fails', () => { const s = clone(); s.schemaVersion = 2; assert.throws(() => compileTheme(s), /schema/); });
test('checked-in generated views are current and root-independent', () => {
  build(true);
  execFileSync(process.execPath, [fileURLToPath(new URL('../packages/ui/tools/build-theme.mjs', import.meta.url)), '--check'], { cwd: fileURLToPath(new URL('../packages/ui/src', import.meta.url)) });
});
function luminance(hex) { return hex.match(/[a-f0-9]{2}/gi).map(v => parseInt(v,16)/255).map(v => v <= .04045 ? v/12.92 : ((v+.055)/1.055)**2.4).reduce((sum,v,i) => sum + v * [.2126,.7152,.0722][i], 0); }
function contrast(a,b) { const x=luminance(a),y=luminance(b); return (Math.max(x,y)+.05)/(Math.min(x,y)+.05); }
test('text, actions, errors and interactive boundaries meet governed contrast', () => {
  for (const [key, threshold] of [['slate-900',4.5],['slate-600',4.5],['admin-ink',4.5],['admin-muted',4.5],['error',4.5],['indigo-600',4.5],['indigo-500',3],['admin-action',4.5]]) assert.ok(contrast(source.primitives[key], '#ffffff') >= threshold, key);
  assert.ok(contrast(source.primitives['slate-200'], '#ffffff') < 3); assert.ok(contrast(source.primitives['admin-border'], '#ffffff') < 3);
});

test('duplicate source properties and stale generated output fail', () => {
  assert.throws(() => readThemeSource('{"primitives":{"white":"#fff","white":"#000"}}'), /Duplicate/);
  const compiled = compileTheme(source);
  assert.throws(() => verifyCurrentOutput(compiled.css, compiled.css.replace('#4f46e5','#000000'), 'tokens.generated.css'), /Stale generated/);
});