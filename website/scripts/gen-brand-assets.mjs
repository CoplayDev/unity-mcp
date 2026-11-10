// Rasterizes the brand SVG marks into the favicon/icon PNG set.
// Run from the website/ directory: `npm run gen:brand`
// Text-bearing assets (hero banner, social card) are produced separately via
// an HTML render so the Satoshi wordmark uses the real font.
import sharp from 'sharp';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url)); // website/scripts
const ROOT = path.resolve(HERE, '..', '..');
const IMG = path.join(ROOT, 'website', 'static', 'img');

const full = path.join(IMG, 'logo-mark.svg');

async function rasterize(src, size, out) {
  const svg = await readFile(src);
  await sharp(svg, { density: 512 })
    .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png()
    .toFile(out);
  console.log('  wrote', path.relative(ROOT, out), `(${size}px)`);
}

await mkdir(IMG, { recursive: true });
await rasterize(full, 48, path.join(IMG, 'favicon.png'));
await rasterize(full, 32, path.join(IMG, 'favicon-32.png'));
await rasterize(full, 180, path.join(IMG, 'apple-touch-icon.png'));
await rasterize(full, 192, path.join(IMG, 'android-chrome-192.png'));
await rasterize(full, 512, path.join(IMG, 'android-chrome-512.png'));
// 128px source for the UPM package icon — wiring it into MCPForUnity needs the
// Unity Editor to generate the .meta, so it lives here as a source asset.
await rasterize(full, 128, path.join(IMG, 'package-icon.png'));
console.log('Brand icon set generated.');
