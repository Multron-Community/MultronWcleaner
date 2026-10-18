import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const keyFile = path.join(root, 'MultronWinCleaner', 'Processes', 'UpdateKey.cs');
export const MANIFEST_APP = 'multron-win-cleaner';
export const GENERATED = ['SHA256SUMS.txt', 'update.json', 'update.json.sig'];

export function readPublicKey() {
  const match = fs.readFileSync(keyFile, 'utf8').match(/PublicKey\s*=\s*"([^"]*)"/);
  return match ? match[1].trim() : '';
}

export function writePublicKey(base64) {
  const text = fs.readFileSync(keyFile, 'utf8').replace(/(PublicKey\s*=\s*")[^"]*(")/, `$1${base64}$2`);
  fs.writeFileSync(keyFile, text);
}

export function publicKeyObject(base64) {
  return crypto.createPublicKey({ key: Buffer.from(base64, 'base64'), format: 'der', type: 'spki' });
}
