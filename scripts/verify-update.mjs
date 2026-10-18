import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { publicKeyObject, readPublicKey } from './update-key.mjs';

const [manifestFile, signatureFile, ...files] = process.argv.slice(2);
if (!manifestFile || !signatureFile) {
  console.error('Usage: node scripts/verify-update.mjs update.json update.json.sig [downloaded files...]');
  process.exit(2);
}
const pinned = readPublicKey();
if (!pinned) {
  console.error('MultronWinCleaner/Processes/UpdateKey.cs has no public key.');
  process.exit(1);
}
const manifest = fs.readFileSync(manifestFile);
const signature = Buffer.from(fs.readFileSync(signatureFile, 'utf8').trim(), 'base64');
if (!crypto.verify('sha256', manifest, publicKeyObject(pinned), signature)) {
  console.error('INVALID: update.json was not signed with the Multron Win Cleaner key.');
  process.exit(1);
}
const data = JSON.parse(manifest);
console.log(`Signature OK: ${data.name} ${data.version}, ${Object.keys(data.files).length} files`);
let failed = false;
for (const file of files) {
  const name = path.basename(file);
  const expected = data.files[name];
  const actual = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const state = !expected ? 'NOT LISTED' : expected === actual ? 'OK' : 'MISMATCH';
  if (state !== 'OK') failed = true;
  console.log(`${state.padEnd(11)} ${name}`);
}
process.exit(failed ? 1 : 0);
