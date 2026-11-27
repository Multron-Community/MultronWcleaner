const fs = require('fs');
const path = require('path');

const LANG_DIR = path.join(__dirname, '..', '..', 'MultronWinCleaner', 'Lang');
const REFERENCE = 'de.json';
const BATCH = 40;

const KEEP = [
  'Multron Malware Scan Cloud', 'Multron Windows Cleaner', 'Multron Win Cleaner', 'Multron Cloud',
  'viruskov.com', 'OPEN-EDR', 'VirusTotal', 'Microsoft Defender', 'AppInit_DLLs', 'Winlogon', 'Userinit',
  'database.txt', 'Settings.txt',
];

const TARGET = { 'pt-br': 'PT-BR', 'zh-cn': 'ZH-HANS', 'zh-tw': 'ZH-HANT', en: 'EN-US' };

const PLACEHOLDER = /\{\d+(?:[:,][^}]*)?\}/g;
const CONTEXT_KEY = /^([a-z]+)\|(.+)$/s;

function readJson(file) {
  let text = fs.readFileSync(file, 'utf8');
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1);
  return JSON.parse(text);
}

function writeJson(file, obj) {
  fs.writeFileSync(file, JSON.stringify(obj, null, 2).replace(/\n/g, '\r\n'), 'utf8');
}

const escapeXml = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const unescapeXml = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');

function protect(text) {
  const saved = [];
  const keep = token => `\u0000${saved.push(token) - 1}\u0000`;
  let s = text.replace(PLACEHOLDER, keep);
  for (const name of KEEP) s = s.split(name).join(keep(name));
  s = escapeXml(s)
    .replace(/\r?\n/g, '<br/>')
    .replace(/\t/g, '<tab/>')
    .replace(/\u0000(\d+)\u0000/g, (_, i) => `<x i="${i}"/>`);
  return { xml: s, saved };
}

function restore(xml, saved) {
  let s = xml
    .replace(/<x i="(\d+)"\s*\/>/g, (_, i) => `\u0000${i}\u0000`)
    .replace(/<br\s*\/>/g, '\n')
    .replace(/<tab\s*\/>/g, '\t');
  s = unescapeXml(s);
  return s.replace(/\u0000(\d+)\u0000/g, (_, i) => saved[Number(i)]);
}

const sig = s => [(s.match(PLACEHOLDER) || []).sort().join('|'), s.split('\n').length].join('#');

async function deepl(texts, target, key) {
  const host = key.endsWith(':fx') ? 'https://api-free.deepl.com' : 'https://api.deepl.com';
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(`${host}/v2/translate`, {
      method: 'POST',
      headers: { Authorization: `DeepL-Auth-Key ${key}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({
        text: texts, source_lang: 'EN', target_lang: target,
        tag_handling: 'xml', ignore_tags: ['x'], formality: 'prefer_more', preserve_formatting: true,
      }),
    });
    if (res.ok) return (await res.json()).translations.map(t => t.text);
    const body = await res.text();
    if ((res.status === 429 || res.status >= 500) && attempt < 6) {
      const wait = attempt * 5;
      console.log(`  DeepL ${res.status}, retrying in ${wait} s...`);
      await new Promise(r => setTimeout(r, wait * 1000));
      continue;
    }
    if (res.status === 456) throw new Error('DeepL monthly character quota is used up (456).');
    throw new Error(`DeepL error ${res.status}: ${body.slice(0, 300)}`);
  }
}

async function translateLanguage(code, ref, opts) {
  const file = path.join(LANG_DIR, `${code}.json`);
  const current = fs.existsSync(file) ? readJson(file) : {};
  const target = TARGET[code] || code.toUpperCase();

  const todo = Object.keys(ref).filter(k => {
    const v = current[k];
    if (!v) return true;
    if (opts.redoEnglish && (v === k || v === (CONTEXT_KEY.exec(k)?.[2] ?? k)) && /[a-z]{3}/i.test(v)) return true;
    return false;
  });
  const chars = todo.reduce((n, k) => n + k.length, 0);
  console.log(`${code}: ${todo.length} entries to translate (~${chars.toLocaleString()} characters) -> DeepL ${target}`);
  if (opts.dryRun || todo.length === 0) return;

  const failed = [];
  for (let i = 0; i < todo.length; i += BATCH) {
    const keys = todo.slice(i, i + BATCH);
    const parts = keys.map(k => protect(CONTEXT_KEY.exec(k)?.[2] ?? k));
    const out = await deepl(parts.map(p => p.xml), target, opts.key);
    keys.forEach((k, j) => {
      const source = CONTEXT_KEY.exec(k)?.[2] ?? k;
      const value = restore(out[j], parts[j].saved);
      if (!value || sig(value) !== sig(source)) failed.push(k);
      else current[k] = value;
    });

    writeJson(file, current);
    console.log(`  ${Math.min(i + BATCH, todo.length)}/${todo.length}`);
  }

  if (failed.length) {
    console.log(`  ${failed.length} entries skipped because placeholders or line breaks changed (they stay English):`);
    failed.slice(0, 20).forEach(k => console.log('   - ' + JSON.stringify(k).slice(0, 120)));
  }
  const final = readJson(file);
  console.log(`  done: ${Object.keys(final).length}/${Object.keys(ref).length} entries in ${code}.json`);
}

async function main() {
  const args = process.argv.slice(2);
  const opts = {
    dryRun: args.includes('--dry-run'),
    redoEnglish: args.includes('--redo-english'),
    key: process.env.DEEPL_API_KEY || '',
  };
  if (!opts.key && !opts.dryRun) {
    console.error('Set the DEEPL_API_KEY environment variable first.');
    process.exit(1);
  }
  let codes = args.filter(a => !a.startsWith('--'));
  if (args.includes('--all'))
    codes = fs.readdirSync(LANG_DIR).filter(f => f.endsWith('.json') && f !== REFERENCE).map(f => f.slice(0, -5));
  if (codes.length === 0) {
    console.error('Give a language code (for example: el) or --all.');
    process.exit(1);
  }
  const ref = readJson(path.join(LANG_DIR, REFERENCE));
  for (const code of codes) await translateLanguage(code, ref, opts);
}

if (require.main === module) main().catch(e => { console.error(e.message); process.exit(1); });

module.exports = { protect, restore };
