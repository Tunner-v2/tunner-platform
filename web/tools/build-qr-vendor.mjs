import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
const root = new URL('../../', import.meta.url);
const require = createRequire(new URL('../package.json', import.meta.url));
const ts = require('typescript');
const register = JSON.parse(await readFile(new URL('docs/development/THIRD_PARTY_COMPONENTS.json', root), 'utf8'));
const entry = register.components.find(item => item.id === 'nayuki-qr-code-generator');
if (!entry) throw new Error('QR_VENDOR_REGISTER_MISSING');
const digest = value => createHash('sha256').update(value).digest('hex');
const source = await readFile(new URL(entry.source_path, root));
if (source.length !== entry.source_bytes || digest(source) !== entry.source_sha256 || ts.version !== '5.9.3') throw new Error('QR_VENDOR_SOURCE_MISMATCH');
const emitted = ts.transpileModule(source.toString('utf8') + '\n\nexport { qrcodegen };\n', {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, removeComments: false, newLine: ts.NewLineKind.LineFeed }
}).outputText.replace(/[ \t]+$/gm, '');
if (digest(emitted) !== entry.generated_sha256) throw new Error('QR_VENDOR_GENERATED_MISMATCH');
const notice = (source.toString('utf8').split('*/')[0] + '*/\n').replace(/[ \t]+$/gm, '');
if ((await readFile(new URL(entry.license_notice_path, root), 'utf8')).replaceAll('\r\n', '\n') !== notice) throw new Error('QR_VENDOR_LICENSE_MISMATCH');
if (process.argv.includes('--check')) {
  if ((await readFile(new URL(entry.generated_path, root), 'utf8')).replaceAll('\r\n', '\n') !== emitted) throw new Error('QR_VENDOR_OUTPUT_STALE');
} else await writeFile(new URL(entry.generated_path, root), emitted);
console.log('QR vendor integrity PASS');
