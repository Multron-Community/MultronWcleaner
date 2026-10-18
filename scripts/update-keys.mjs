import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { readPublicKey, root, writePublicKey } from './update-key.mjs';

const args = process.argv.slice(2);
const rotate = args.includes('--rotate');
const out = path.resolve(args.find((a) => !a.startsWith('--')) ?? path.join(os.homedir(), 'multron-wincleaner-signing-key.txt'));

if (readPublicKey() && !rotate) {
  console.error(`An update signing key is already set in MultronWinCleaner/Processes/UpdateKey.cs.
Installed copies only accept updates signed with that key. A new key means every
installed copy has to be updated by hand once. Run with --rotate only if the old
private key is lost or leaked.`);
  process.exit(1);
}
if (out.startsWith(root + path.sep)) {
  console.error('Save the private key outside the repository, so it can never be committed.');
  process.exit(1);
}
if (fs.existsSync(out)) {
  console.error(`${out} already exists; choose another path.`);
  process.exit(1);
}

const { publicKey, privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
fs.writeFileSync(out, privateKey.export({ format: 'der', type: 'pkcs8' }).toString('base64') + '\n', { mode: 0o600 });
writePublicKey(publicKey.export({ format: 'der', type: 'spki' }).toString('base64'));

console.log(`Public key written to MultronWinCleaner/Processes/UpdateKey.cs (commit this file).
Private key written to ${out}

Next steps:
  1. GitHub → Settings → Environments → New environment "release-signing".
  2. Add a required reviewer (you) and limit it to the "main" and "beta" branches and "v*" tags.
  3. Add an environment secret UPDATE_SIGNING_KEY with the content of the private key file.
  4. Keep an offline backup of the private key file, then delete it from this computer.

Never commit the private key or paste it anywhere else.`);
