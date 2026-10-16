import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const namePattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const assertNames = (group) => {
  if (!group || typeof group !== 'object' || Array.isArray(group) || !Object.keys(group).length) throw new Error('Empty token group');
  for (const [key, value] of Object.entries(group)) {
    if (!namePattern.test(key) || typeof value !== 'string' || !value.trim() || /[;<>\r\n]/.test(value)) throw new Error(`Invalid token ${key}`);
    if (/[{}]/.test(value) && !/^\{(primitive|semantic|component)\.[a-z0-9-]+\}$/.test(value)) throw new Error(`Invalid reference ${key}`);
  }
};
export function compileTheme(source) {
  if (source.schemaVersion !== 1) throw new Error('Unsupported theme schema');
  assertNames(source.primitives); assertNames(source.components);
  if (Object.keys(source.profiles ?? {}).sort().join() !== 'admin,user') throw new Error('Both approved surfaces are required');
  const surfaces = Object.keys(source.profiles).sort();
  const modes = Object.keys(source.profiles.user).sort();
  if (!modes.includes('light') || modes.some(mode => !namePattern.test(mode))) throw new Error('Invalid theme modes');
  const semanticKeys = Object.keys(source.profiles.user.light).sort();
  const blocks = [];
  for (const surface of surfaces) {
    if (Object.keys(source.profiles[surface]).sort().join() !== modes.join()) throw new Error(`Incomplete modes for ${surface}`);
    for (const mode of modes) {
      const profile = source.profiles[surface][mode]; assertNames(profile);
      if (Object.keys(profile).sort().join() !== semanticKeys.join()) throw new Error(`Incomplete semantic profile ${surface}/${mode}`);
      const graph = Object.fromEntries([
        ...Object.entries(source.primitives).map(([key, value]) => [`primitive.${key}`, value]),
        ...Object.entries(profile).map(([key, value]) => [`semantic.${key}`, value]),
        ...Object.entries(source.components).map(([key, value]) => [`component.${key}`, value])
      ]);
      const visit = (key, chain = []) => {
        if (!(key in graph)) throw new Error(`Unknown token ${key}`);
        if (chain.includes(key)) throw new Error(`Token cycle ${[...chain, key].join(' -> ')}`);
        const value = graph[key];
        return value.startsWith('{') ? visit(value.slice(1, -1), [...chain, key]) : value;
      };
      const declarations = Object.keys(graph).sort().map(key => `  --tn-${key.replace('.', '-')}: ${visit(key)};`);
      blocks.push(`[data-tunner-surface="${surface}"][data-tunner-theme="${mode}"] {\n${declarations.join('\n')}\n}`);
    }
  }
  const header = '/* Generated from theme.tokens.json; run pnpm theme:build. */\n';
  const css = header + blocks.join('\n\n') + '\n';
  const types = `// Generated from theme.tokens.json; do not edit.\nexport const themeSurfaces = ${JSON.stringify(surfaces)} as const;\nexport const themeModes = ${JSON.stringify(modes)} as const;\nexport type ThemeSurface = typeof themeSurfaces[number];\nexport type ThemeMode = typeof themeModes[number];\n`;
  const bridge = header + '@theme inline {\n  --color-tunner-surface: var(--tn-semantic-surface);\n  --color-tunner-page: var(--tn-semantic-page);\n  --color-tunner-text: var(--tn-semantic-text);\n  --color-tunner-muted: var(--tn-semantic-muted);\n  --color-tunner-action: var(--tn-semantic-action-background);\n  --color-tunner-on-action: var(--tn-semantic-action-text);\n  --font-tunner: var(--tn-semantic-font);\n  --radius-tunner-control: var(--tn-component-field-radius);\n}\n';
  return { css, types, bridge };
}
export function readThemeSource(text) {
  const parsed = JSON.parse(text);
  const lexical = text.match(/"(?:\\.|[^"\\])*"|[{}\[\]:,]|true|false|null|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g);
  const stack = [];
  for (let index = 0; index < lexical.length; index++) {
    const value = lexical[index];
    if (value === '{') stack.push(new Set());
    else if (value === '[') stack.push(null);
    else if (value === '}' || value === ']') stack.pop();
    else if (value.startsWith('"') && lexical[index + 1] === ':') {
      const keys = stack.at(-1); const key = JSON.parse(value);
      if (keys?.has(key)) throw new Error(`Duplicate token/source key ${key}`);
      keys?.add(key);
    }
  }
  return parsed;
}
export function verifyCurrentOutput(expected, actual, path) {
  if (actual.replaceAll('\r\n', '\n') !== expected) throw new Error(`Stale generated theme ${path}`);
}
const directory = new URL('../', import.meta.url);
export function build(check = false) {
  const source = readThemeSource(readFileSync(new URL('theme.tokens.json', directory), 'utf8'));
  const result = compileTheme(source);
  for (const [path, body] of [['styles/tokens.generated.css', result.css], ['src/theme.generated.ts', result.types], ['styles/tailwind.generated.css', result.bridge]]) {
    const url = new URL(path, directory);
    if (check) { verifyCurrentOutput(body, readFileSync(url, 'utf8'), path); }
    else writeFileSync(url, body);
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  build(process.argv.includes('--check')); console.log(process.argv.includes('--check') ? 'Theme outputs CURRENT' : 'Theme outputs GENERATED');
}
