import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import vm from 'node:vm';
import { encodeQrMatrix } from '../../apps/user/totp-qr.ts';

const root = new URL('../../../', import.meta.url);
const oraclePath = process.env.TUNNER_PUBLIC_QR_ORACLE;
const oracleBytes = await readFile(oraclePath || new URL('artifacts/p2-008-totp-20261110/qr-oracle-jsqr-1.4.0.js', root));
assert.equal(createHash('sha256').update(oracleBytes).digest('hex'), 'bc40c8a15196236b2314db0856f72ca0b49980cd5413b8c852a7349f5fee0859', 'Public-only independent decoder integrity');
const sandbox = vm.createContext({ module: { exports: {} }, exports: {} }, { codeGeneration: { strings: false, wasm: false } });
new vm.Script(oracleBytes.toString('utf8'), { filename: 'pinned-public-qr-oracle.js' }).runInContext(sandbox, { timeout: 1000 });

function raster(matrix, pixels = 220) {
  const image = new Uint8ClampedArray(pixels * pixels * 4).fill(255);
  const modules = new Set([...matrix.path.matchAll(/M(\d+),(\d+)h1v1h-1z/g)].map(match => match[1] + ',' + match[2]));
  for (let y = 0; y < pixels; y++) for (let x = 0; x < pixels; x++) {
    const mx = Math.floor((x + 0.5) * matrix.extent / pixels), my = Math.floor((y + 0.5) * matrix.extent / pixels);
    if (modules.has(mx + ',' + my)) { const offset = (y * pixels + x) * 4; image[offset] = image[offset + 1] = image[offset + 2] = 0; }
  }
  return image;
}

const fixtures = [
  ['numeric', '01234567890123456789'],
  ['alphanumeric', 'PUBLIC QR TEST 123'],
  ['unicode', 'Public Unicode test: Δοκιμή 测试'],
  ['public-provisioning', 'otpauth://totp/' + encodeURIComponent('Public test:test@example.invalid') + '?secret=PUBLIC-NONFUNCTIONAL-NOT-BASE32&issuer=' + encodeURIComponent('Public test') + '&algorithm=SHA1&digits=6&period=30']
];
for (const [id, value] of fixtures) test('independent public QR decoding at approved220px: ' + id, () => {
  const encoded = encodeQrMatrix(value); assert.equal(encoded.ok, true); if (!encoded.ok) return;
  assert.match(encoded.path, /^(?:M\d+,\d+h1v1h-1z)+$/);
  const points = [...encoded.path.matchAll(/M(\d+),(\d+)/g)];
  assert.ok(points.every(([, x, y]) => Number(x) >= 4 && Number(y) >= 4 && Number(x) < encoded.extent - 4 && Number(y) < encoded.extent - 4));
  sandbox.publicPixels = raster(encoded);
  const decoded = new vm.Script('module.exports(publicPixels, 220, 220, {inversionAttempts:"dontInvert"})').runInContext(sandbox, { timeout: 2000 });
  assert.ok(decoded, 'Public fixture should decode'); assert.equal(decoded.data, value);
});
test('overflow is a fixed safe result without input in error', () => {
  assert.deepEqual(encodeQrMatrix('Public oversized input '.repeat(1000)), { ok: false, code: 'QR_UNAVAILABLE' });
});
test('matrix output retains no input/title/ARIA/HTML and vendor has no runtime network capability', async () => {
  const encoded = encodeQrMatrix('Public unique transport marker');
  assert.equal(encoded.ok, true); assert.ok(!JSON.stringify(encoded).includes('Public unique transport marker'));
  const source = await readFile(new URL('web/vendor/nayuki/qrcodegen.mjs', root), 'utf8');
  assert.doesNotMatch(source, /\b(?:fetch|XMLHttpRequest|WebSocket|eval)\s*\(/);
  assert.doesNotMatch(source, /\b(?:document|window|globalThis)\./);
});
