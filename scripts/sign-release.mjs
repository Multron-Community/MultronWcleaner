import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { GENERATED, MANIFEST_APP, publicKeyObject, readPublicKey } from './update-key.mjs';

const [dir, version] = process.argv.slice(2);
const secret = (process.env.UPDATE_SIGNING_KEY || '').trim();
if (!dir || !version) {
  console.error('Usage: UPDATE_SIGNING_KEY=... node scripts/sign-release.mjs <folder> <version>');
  process.exit(2);
}
if (!secret) throw new Error('UPDATE_SIGNING_KEY is not set in the release-signing environment');
const pinned = readPublicKey();
if (!pinned) throw new Error('MultronWinCleaner/Processes/UpdateKey.cs has no public key. Run node scripts/update-keys.mjs first.');

const privateKey = crypto.createPrivateKey({ key: Buffer.from(secret, 'base64'), format: 'der', type: 'pkcs8' });
const files = {};
for (const name of fs.readdirSync(dir).sort()) {
  const full = path.join(dir, name);
  if (GENERATED.includes(name) || !fs.statSync(full).isFile()) continue;
  files[name] = crypto.createHash('sha256').update(fs.readFileSync(full)).digest('hex');
}
const zips = Object.keys(files).filter((n) => n.toLowerCase().endsWith('.zip'));
if (zips.length !== 1) throw new Error(`expected exactly one .zip for the updater, found: ${zips.join(', ') || 'none'}`);

const manifest = Buffer.from(JSON.stringify({ name: MANIFEST_APP, version, files }, null, 2) + '\n');
const signature = crypto.sign('sha256', manifest, privateKey);
if (!crypto.verify('sha256', manifest, publicKeyObject(pinned), signature)) {
  throw new Error('UPDATE_SIGNING_KEY does not match the public key in MultronWinCleaner/Processes/UpdateKey.cs');
}
fs.writeFileSync(path.join(dir, 'SHA256SUMS.txt'), Object.entries(files).map(([n, h]) => `${h}  ${n}`).join('\n') + '\n');
fs.writeFileSync(path.join(dir, 'update.json'), manifest);
fs.writeFileSync(path.join(dir, 'update.json.sig'), signature.toString('base64') + '\n');
console.log(`signed update.json for ${version} with ${Object.keys(files).length} files`);
