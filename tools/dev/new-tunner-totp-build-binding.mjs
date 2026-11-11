import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {lstat, mkdir, readFile, readdir, realpath, writeFile} from 'node:fs/promises';
import {dirname, isAbsolute, relative, resolve, sep} from 'node:path';
import {fileURLToPath} from 'node:url';

// Run only after the complete reviewed source is committed and its build succeeds.
// This receipt records a local build; it does not invoke a build or claim acceptance.
const root = await realpath(fileURLToPath(new URL('../../', import.meta.url)));
const sourceRoots = ['src', 'tools/browser-testing', 'tests/Shared', 'tests/Tunner.Identity.Persistence.Tests', 'web', 'tools/dev/new-tunner-totp-build-binding.mjs', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'Tunner.Platform.slnx', '.editorconfig', '.gitattributes', 'NuGet.Config'];
const outputRoot = 'tools/browser-testing/bin/Debug/net10.0';
const receiptPath = 'artifacts/p2-008-totp-20261110/runtime-build-binding.json';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const git = args => execFileSync('git', args, {cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']});
const ordinal = (left, right) => left < right ? -1 : left > right ? 1 : 0;
async function checkedPath(path, directory = false) {
  if (isAbsolute(path) || path.includes('\\') || path.split('/').some(part => !part || part === '.' || part === '..')) throw new Error();
  const absolute = resolve(root, path), inside = relative(root, absolute);
  if (isAbsolute(inside) || inside === '..' || inside.startsWith('..' + sep)) throw new Error();
  const actual = await realpath(absolute);
  const same = process.platform === 'win32' ? actual.toLowerCase() === absolute.toLowerCase() : actual === absolute;
  const stat = await lstat(absolute);
  if (!same || stat.isSymbolicLink() || !(directory ? stat.isDirectory() : stat.isFile())) throw new Error();
  return absolute;
}
try {
  if (process.argv.length !== 2) throw new Error();
  const sourceCommit = git(['rev-parse', 'HEAD']).trim();
  if (!/^[a-f0-9]{40}$/.test(sourceCommit) || git(['status', '--porcelain', '--untracked-files=all', '--', ...sourceRoots]).trim()) throw new Error();
  const entries = git(['ls-tree', '-rz', '--full-tree', sourceCommit, '--', ...sourceRoots]).split('\0').filter(Boolean);
  const sourceInputs = [];
  for (const entry of entries) {
    const match = /^(100644|100755) blob ([a-f0-9]{40})\t(.+)$/.exec(entry);
    if (!match) throw new Error();
    const path = match[3];
    sourceInputs.push({path, sha256: digest(await readFile(await checkedPath(path))), git_blob: match[2]});
  }
  sourceInputs.sort((left, right) => ordinal(left.path, right.path));
  if (!sourceInputs.length || !sourceInputs.some(value => value.path === 'tools/dev/new-tunner-totp-build-binding.mjs')) throw new Error();
  const buildOutputs = [];
  async function visit(path) {
    for (const name of (await readdir(await checkedPath(path, true))).sort(ordinal)) {
      const child = path + '/' + name, stat = await lstat(resolve(root, child));
      if (stat.isSymbolicLink()) throw new Error();
      if (stat.isDirectory()) await visit(child);
      else buildOutputs.push({path: child, sha256: digest(await readFile(await checkedPath(child)))});
    }
  }
  await visit(outputRoot);
  buildOutputs.sort((left, right) => ordinal(left.path, right.path));
  if (!buildOutputs.some(value => value.path === outputRoot + '/Tunner.BrowserTesting.dll')) throw new Error();
  if (git(['rev-parse', 'HEAD']).trim() !== sourceCommit || git(['status', '--porcelain', '--untracked-files=all', '--', ...sourceRoots]).trim()) throw new Error();
  const bytes = JSON.stringify({schema_version: 1, source_commit_sha: sourceCommit, source_inputs: sourceInputs, build_outputs: buildOutputs}, null, 2) + '\n';
  const receipt = resolve(root, receiptPath);
  await mkdir(dirname(receipt), {recursive: true});
  await checkedPath(relative(root, dirname(receipt)).split(sep).join('/'), true);
  await writeFile(receipt, bytes, {encoding: 'utf8'});
  process.stdout.write(JSON.stringify({status: 'BUILD_BINDING_RECORDED', sourceCommit, sourceInputs: sourceInputs.length, buildOutputs: buildOutputs.length, receipt: receiptPath, sha256: digest(Buffer.from(bytes))}) + '\n');
} catch {
  process.stderr.write('BUILD_BINDING_FAILED: require clean committed source, current reviewed build outputs, and regular in-repository files.\n');
  process.exitCode = 1;
}
