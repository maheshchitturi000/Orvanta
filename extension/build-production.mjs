// Makes a production copy of the extension that talks to your deployed Orvanta instead of localhost.
// In production the web app and the API share one origin (the Gateway serves both).
//
//   node extension/build-production.mjs https://orvanta-gateway.<id>.<region>.azurecontainerapps.io
//
// Output: dist/extension - load it unpacked from chrome://extensions, or zip it for the Chrome Web Store.

import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const appUrl = (process.argv[2] ?? '').replace(/\/+$/, '');
if (!/^https:\/\/[^/]+$/.test(appUrl)) {
  console.error('Usage: node extension/build-production.mjs https://your-orvanta-host');
  process.exit(1);
}

const source = dirname(fileURLToPath(import.meta.url));
const target = join(source, '..', 'dist', 'extension');
const skip = new Set(['README.md', 'build-production.mjs']);

rmSync(target, { recursive: true, force: true });
mkdirSync(target, { recursive: true });

// Order matters: the API URL contains the Gateway origin.
const replacements = [
  ['http://localhost:5095/api', `${appUrl}/api`],
  ['http://localhost:5095/*', `${appUrl}/*`],
  ['http://localhost:4200', appUrl]
];

for (const name of readdirSync(source)) {
  if (skip.has(name)) continue;
  const from = join(source, name);
  const to = join(target, name);
  if (['.js', '.json', '.html'].includes(extname(name))) {
    let text = readFileSync(from, 'utf8');
    for (const [find, replace] of replacements) text = text.replaceAll(find, replace);
    writeFileSync(to, text);
  } else {
    cpSync(from, to, { recursive: true });
  }
}

// The manifest listed both dev origins; in production they are the same one.
const manifestPath = join(target, 'manifest.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
manifest.host_permissions = [...new Set(manifest.host_permissions)];
writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');

console.log(`Production extension for ${appUrl} written to ${target}`);
