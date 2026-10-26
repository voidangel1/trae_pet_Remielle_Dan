// 生成安装器用的预览卡与 ICO（从源 idle 首帧渲染）
const sharp = require('sharp');
const path = require('path');
const fs = require('fs');

const DIST = path.join(__dirname, 'dist');
const PREVIEW = path.join(DIST, 'installer-preview.png');
const ICO = path.join(DIST, 'remielle.ico');

const SIZE = 512;
const CHAR_HEIGHT = 388;
const BOTTOM_MARGIN = 18;

async function makePreview() {
  const metaPath = path.join(DIST, 'meta.json');
  if (!fs.existsSync(metaPath)) throw new Error('缺少 dist/meta.json，先运行 convert-qoder-pet.js');
  const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
  const idle = meta.sourceGifs.idle;
  const box = idle.box;
  const src = idle.file;
  const char = await sharp(src, { page: 0, pages: 1 })
    .ensureAlpha()
    .extract({ left: box.minX, top: box.minY, width: box.w, height: box.h })
    .resize({ height: CHAR_HEIGHT, fit: 'inside', kernel: 'lanczos3' })
    .png()
    .toBuffer();
  const charMeta = await sharp(char).metadata();
  const left = Math.round((SIZE - charMeta.width) / 2);
  const top = SIZE - BOTTOM_MARGIN - charMeta.height;

  const bg = `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0.9" y2="1">
      <stop offset="0" stop-color="#fff7fb"/>
      <stop offset="0.55" stop-color="#ffeef6"/>
      <stop offset="1" stop-color="#ffddea"/>
    </linearGradient>
    <radialGradient id="glow" cx="0.5" cy="0.42" r="0.52">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.95"/>
      <stop offset="0.7" stop-color="#ffffff" stop-opacity="0.35"/>
      <stop offset="1" stop-color="#ffffff" stop-opacity="0"/>
    </radialGradient>
  </defs>
  <rect width="${SIZE}" height="${SIZE}" rx="96" fill="url(#bg)"/>
  <circle cx="256" cy="238" r="188" fill="url(#glow)"/>
  <circle cx="74" cy="92" r="10" fill="#ffffff" opacity="0.75"/>
  <circle cx="438" cy="130" r="6" fill="#ffffff" opacity="0.8"/>
  <circle cx="428" cy="368" r="12" fill="#ffffff" opacity="0.55"/>
  <circle cx="88" cy="360" r="7" fill="#ffffff" opacity="0.65"/>
  <ellipse cx="256" cy="${top + charMeta.height - 8}" rx="142" ry="20" fill="#e7a9c8" opacity="0.18"/>
  <path d="M400 66 l7 19 19 7 -19 7 -7 19 -7 -19 -19 -7 19 -7z" fill="#ffffff" opacity="0.8"/>
  <path d="M110 206 l5 13 13 5 -13 5 -5 13 -5 -13 -13 -5 13 -5z" fill="#ffffff" opacity="0.7"/>
  <path d="M414 250 l4 10 10 4 -10 4 -4 10 -4 -10 -10 -4 10 -4z" fill="#f7b8d4" opacity="0.65"/>
  </svg>`;

  await sharp(Buffer.from(bg))
    .composite([{ input: char, left, top }])
    .png()
    .toFile(PREVIEW);
  console.log(`[OK] ${PREVIEW} (${SIZE}x${SIZE})`);
}

async function makeIco() {
  const png = fs.readFileSync(PREVIEW);
  const sizes = [256, 128, 64, 48, 32, 24, 16];
  const images = [];
  for (const s of sizes) {
    images.push({ size: s, data: await sharp(png).resize(s, s, { fit: 'cover' }).png().toBuffer() });
  }
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  const entries = Buffer.alloc(images.length * 16);
  let offset = 6 + entries.length;
  images.forEach((img, i) => {
    const p = i * 16;
    entries.writeUInt8(img.size >= 256 ? 0 : img.size, p);
    entries.writeUInt8(img.size >= 256 ? 0 : img.size, p + 1);
    entries.writeUInt8(0, p + 2);
    entries.writeUInt8(0, p + 3);
    entries.writeUInt16LE(1, p + 4);
    entries.writeUInt16LE(32, p + 6);
    entries.writeUInt32LE(img.data.length, p + 8);
    entries.writeUInt32LE(offset, p + 12);
    offset += img.data.length;
  });
  fs.writeFileSync(ICO, Buffer.concat([header, entries, ...images.map((i) => i.data)]));
  console.log(`[OK] ${ICO} (${sizes.join('/')})`);
}

(async () => {
  fs.mkdirSync(DIST, { recursive: true });
  await makePreview();
  await makeIco();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
