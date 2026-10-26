// Remielle GIF → Qoder 原生桌宠宠物包
//
// Qoder 读取 %USERPROFILE%\.petdex\pets\<id>\pet.json + spritesheet.webp。
// 精灵图固定 8 列；v2 为 11 行（9 个动作 + 2 行视线帧），每格 192×208（12:13）。
// 每行帧数与播放节奏由 Qoder 内置定义，素材不足时重采样 / 重复源帧。
//
// 用法:
//   node qoder-pet/convert-qoder-pet.js              生成完整宠物包
//   node qoder-pet/convert-qoder-pet.js --test       生成兼容性测试卡
//   node qoder-pet/convert-qoder-pet.js --out <dir>  自定义输出目录
const sharp = require('sharp');
const path = require('path');
const fs = require('fs');
const os = require('os');

const ROOT = path.join(__dirname, '..');
const ASSETS = path.join(ROOT, 'Remielle_Dan-clawd-theme-v0.5.4', 'assets');
const DEFAULT_OUT = path.join(__dirname, 'dist', 'remielle');
const PREVIEW_PATH = path.join(__dirname, 'dist', 'preview.png');
const META_PATH = path.join(__dirname, 'dist', 'meta.json');

const CELL_W = 192;
const CELL_H = 208;
const COLS = 8;
const SPRITE_VERSION = 2;
const ROW_COUNT = 11;
const SHEET_W = CELL_W * COLS;
const SHEET_H = CELL_H * ROW_COUNT;
const CONTENT_W = 184;
const CONTENT_H = 192;
const BOTTOM_MARGIN = 8;

const STATES = {
  idle: { file: 'idle-loop.gif' },
  thinking: { file: 'thinking.gif' },
  editing: { file: 'editing.gif' },
  notification: { file: 'notification.gif' },
  attention: { file: 'attention.gif' },
  error: { file: 'error.gif' },
};

// 行帧数与 rowIndex 必须与 Qoder 内置定义一致。
// Qoder 语义：waving=启动问候，jumping=悬停，runningLeft/Right=拖拽方向，
// waiting/review/failed/running=Agent 阶段。running 按用户要求以 editing 为主。
const ROW_SPECS = [
  { row: 0, key: 'idle', source: 'idle', frames: 6 },
  { row: 1, key: 'runningRight', source: 'attention', frames: 8 },
  { row: 2, key: 'runningLeft', source: 'attention', frames: 8, mirror: true },
  { row: 3, key: 'waving', source: 'notification', frames: 4 },
  { row: 4, key: 'jumping', source: 'attention', frames: 5, dy: [0, -3, -6, -3, 0] },
  { row: 5, key: 'failed', source: 'error', frames: 8, hold: true, dx: [0, -3, 3, -3, 3, -2, 2, 0] },
  { row: 6, key: 'waiting', source: 'notification', frames: 6 },
  { row: 7, key: 'running', source: 'editing', frames: 6 },
  { row: 8, key: 'review', source: 'thinking', frames: 6 },
  { row: 9, key: 'lookLeft', source: 'idle', frames: 8, mirror: true },
  { row: 10, key: 'lookRight', source: 'idle', frames: 8 },
];

const TEST_ROWS = [
  { row: 0, key: 'idle', frames: 6, color: '#0ea5e9' },
  { row: 1, key: 'runningRight', frames: 8, color: '#f59e0b' },
  { row: 2, key: 'runningLeft', frames: 8, color: '#d97706' },
  { row: 3, key: 'waving', frames: 4, color: '#22c55e' },
  { row: 4, key: 'jumping', frames: 5, color: '#a855f7' },
  { row: 5, key: 'failed', frames: 8, color: '#ef4444' },
  { row: 6, key: 'waiting', frames: 6, color: '#06b6d4' },
  { row: 7, key: 'running', frames: 6, color: '#f97316' },
  { row: 8, key: 'review', frames: 6, color: '#84cc16' },
  { row: 9, key: 'lookLeft', frames: 8, color: '#64748b' },
  { row: 10, key: 'lookRight', frames: 8, color: '#475569' },
];

function esc(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function unionBox(a, b) {
  if (!a) return { ...b };
  if (!b) return { ...a };
  const minX = Math.min(a.minX, b.minX);
  const minY = Math.min(a.minY, b.minY);
  const maxX = Math.max(a.maxX, b.maxX);
  const maxY = Math.max(a.maxY, b.maxY);
  return { minX, minY, maxX, maxY, w: maxX - minX + 1, h: maxY - minY + 1 };
}

function alphaBox(data, width, height, threshold = 8) {
  let minX = width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  for (let y = 0; y < height; y++) {
    const row = y * width * 4;
    for (let x = 0; x < width; x++) {
      if (data[row + x * 4 + 3] > threshold) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  if (maxX < 0) return null;
  return { minX, minY, maxX, maxY, w: maxX - minX + 1, h: maxY - minY + 1 };
}

async function loadState(name) {
  const def = STATES[name];
  if (!def) throw new Error(`未知状态: ${name}`);
  const file = path.join(ASSETS, def.file);
  const meta = await sharp(file, { animated: true }).metadata();
  const pages = meta.pages || 1;
  const frameH = meta.pageHeight || meta.height;
  const frames = [];
  const boxes = [];
  let box = null;
  for (let i = 0; i < pages; i++) {
    const buf = pages === 1
      ? await sharp(file).ensureAlpha().raw().toBuffer()
      : await sharp(file, { page: i, pages: 1 }).ensureAlpha().raw().toBuffer();
    if (buf.length !== meta.width * frameH * 4) {
      throw new Error(`${def.file} 第 ${i} 帧尺寸异常`);
    }
    frames.push(buf);
    const b = alphaBox(buf, meta.width, frameH);
    if (!b) continue;
    boxes.push(b);
    box = unionBox(box, b);
  }
  if (!box) throw new Error(`${def.file} 没有可见像素`);
  // 中位帧尺寸用于定标，避免单帧飞出的纸片/大幅位移把整体缩得太小。
  const median = (arr) => arr.slice().sort((a, b) => a - b)[Math.floor(arr.length / 2)];
  const medianBox = { w: median(boxes.map((b) => b.w)), h: median(boxes.map((b) => b.h)) };
  console.log(
    `[载入] ${def.file}: ${pages} 帧, ${meta.width}x${frameH}, 内容框 ${box.w}x${box.h}@${box.minX},${box.minY}, 中位 ${medianBox.w}x${medianBox.h}`
  );
  return { name, file, width: meta.width, height: frameH, pages, frames, box, medianBox, delays: meta.delay };
}

function pickFrames(state, count, hold) {
  if (hold) return new Array(count).fill(0);
  const out = [];
  for (let i = 0; i < count; i++) {
    out.push(Math.min(state.pages - 1, Math.floor((i * state.pages) / count)));
  }
  return out;
}

async function renderFrame(state, index, width, height, mirror) {
  let img = sharp(state.frames[index], {
    raw: { width: state.width, height: state.height, channels: 4 },
  })
    .extract({ left: state.box.minX, top: state.box.minY, width: state.box.w, height: state.box.h })
    .resize(width, height, { fit: 'fill', kernel: 'lanczos3' });
  if (mirror) img = img.flop();
  return img.png().toBuffer();
}

async function makePetPackage(outDir) {
  fs.mkdirSync(outDir, { recursive: true });

  const states = {};
  for (const name of Object.keys(STATES)) {
    states[name] = await loadState(name);
  }

  const maxMedianW = Math.max(...Object.values(states).map((s) => s.medianBox.w));
  const maxMedianH = Math.max(...Object.values(states).map((s) => s.medianBox.h));
  const scale = Math.min(CONTENT_W / maxMedianW, CONTENT_H / maxMedianH);
  console.log(`[比例] 中位内容上限 ${maxMedianW}x${maxMedianH}, scale=${scale.toFixed(4)}`);

  const composites = [];
  const metaRows = [];
  for (const spec of ROW_SPECS) {
    const state = states[spec.source];
    const width = Math.max(1, Math.round(state.box.w * scale));
    const height = Math.max(1, Math.round(state.box.h * scale));
    const baseLeft = Math.round((CELL_W - width) / 2);
    const baseTop = Math.round(CELL_H - BOTTOM_MARGIN - height);
    const indices = pickFrames(state, spec.frames, spec.hold);
    const frames = [];
    for (let col = 0; col < spec.frames; col++) {
      const dx = spec.dx ? spec.dx[col] || 0 : 0;
      const dy = spec.dy ? spec.dy[col] || 0 : 0;
      const x = baseLeft + dx;
      const y = baseTop + dy;
      if (x < 0 || y < 0 || x + width > CELL_W || y + height > CELL_H) {
        throw new Error(`row ${spec.row} 第 ${col} 帧越界: ${x},${y} ${width}x${height}`);
      }
      const buf = await renderFrame(state, indices[col], width, height, spec.mirror);
      composites.push({ input: buf, left: col * CELL_W + x, top: spec.row * CELL_H + y });
      frames.push({ column: col, sourceFrame: indices[col], x, y, dx, dy, mirror: !!spec.mirror });
    }
    metaRows.push({
      row: spec.row,
      key: spec.key,
      source: STATES[spec.source].file,
      frames: spec.frames,
      sourceFrames: indices,
      cellBox: { x: baseLeft, y: baseTop, w: width, h: height },
      placements: frames,
      note: spec.hold ? 'single-frame source, held' : undefined,
    });
    console.log(`[行 ${String(spec.row).padStart(2)}] ${spec.key.padEnd(12)} ← ${STATES[spec.source].file} (${spec.frames}f)`);
  }

  const sheetPath = path.join(outDir, 'spritesheet.webp');
  await sharp({
    create: { width: SHEET_W, height: SHEET_H, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } },
  })
    .composite(composites)
    .webp({ lossless: true, effort: 6 })
    .toFile(sheetPath);

  const pet = {
    id: 'remielle',
    displayName: '蕾米埃尔',
    description: '蕾米埃尔 · Clawd 主题的 Qoder 原生桌宠（六态 + 悬停/拖拽/视线跟随）',
    spriteVersionNumber: SPRITE_VERSION,
    spritesheetPath: 'spritesheet.webp',
  };
  const petPath = path.join(outDir, 'pet.json');
  fs.writeFileSync(petPath, JSON.stringify(pet, null, 2) + '\n');

  const sheetMeta = await sharp(sheetPath).metadata();
  const bytes = fs.statSync(sheetPath).size;
  if (sheetMeta.width !== SHEET_W || sheetMeta.height !== SHEET_H) {
    throw new Error(`spritesheet 尺寸错误: ${sheetMeta.width}x${sheetMeta.height}`);
  }
  if (sheetMeta.pages !== undefined && sheetMeta.pages !== 1) {
    throw new Error(`spritesheet 必须是静态图，当前 ${sheetMeta.pages} 页`);
  }
  if (!sheetMeta.hasAlpha) throw new Error('spritesheet 丢失透明通道');
  if (bytes > 32 * 1024 * 1024) throw new Error(`spritesheet 超过 32MB: ${bytes}`);
  if (sheetMeta.width % COLS !== 0 || sheetMeta.height % ROW_COUNT !== 0) {
    throw new Error('spritesheet 不能按行列整除');
  }
  const cellW = sheetMeta.width / COLS;
  const cellH = sheetMeta.height / ROW_COUNT;
  if (cellW * 13 !== cellH * 12) throw new Error(`单元格比例不是 12:13: ${cellW}x${cellH}`);
  console.log(`[OK] ${sheetPath} (${sheetMeta.width}x${sheetMeta.height}, ${(bytes / 1024).toFixed(1)}KB, lossless, alpha=${sheetMeta.hasAlpha})`);
  console.log(`[OK] ${petPath}`);

  await makePreview(sheetPath, metaRows);
  const meta = {
    id: pet.id,
    displayName: pet.displayName,
    spriteVersionNumber: SPRITE_VERSION,
    generatedAt: new Date().toISOString(),
    cell: { width: CELL_W, height: CELL_H },
    sheet: { width: SHEET_W, height: SHEET_H, bytes, lossless: true },
    fit: { medianWidth: maxMedianW, medianHeight: maxMedianH, scale: Number(scale.toFixed(6)) },
    content: { width: CONTENT_W, height: CONTENT_H, bottomMargin: BOTTOM_MARGIN },
    rows: metaRows,
    sourceGifs: Object.fromEntries(
      Object.entries(states).map(([name, s]) => [name, {
        file: s.file, pages: s.pages, width: s.width, height: s.height, box: s.box, delays: s.delays,
      }])
    ),
  };
  fs.mkdirSync(path.dirname(META_PATH), { recursive: true });
  fs.writeFileSync(META_PATH, JSON.stringify(meta, null, 2) + '\n');
  console.log(`[OK] ${META_PATH}`);
  return { outDir, sheetPath, petPath, bytes };
}

function previewSvg(rows) {
  const margin = 220;
  const labels = [];
  for (const row of rows) {
    const y = row.row * CELL_H + CELL_H / 2;
    labels.push(
      `<text x="18" y="${y - 10}" font-family="Consolas, monospace" font-size="18" font-weight="700" fill="#e5e7eb">${row.row} · ${esc(row.key)}</text>`,
      `<text x="18" y="${y + 12}" font-family="Consolas, monospace" font-size="12" fill="#94a3b8">${esc(row.source)}</text>`,
      `<text x="18" y="${y + 30}" font-family="Consolas, monospace" font-size="12" fill="#64748b">${row.frames}f</text>`
    );
    labels.push(
      `<line x1="0" y1="${row.row * CELL_H}" x2="${margin + SHEET_W}" y2="${row.row * CELL_H}" stroke="#334155" stroke-width="1"/>`
    );
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${margin + SHEET_W}" height="${SHEET_H}" viewBox="0 0 ${margin + SHEET_W} ${SHEET_H}">${labels.join('')}</svg>`;
}

async function makePreview(sheetPath, rows) {
  const margin = 220;
  const sheetPng = await sharp(sheetPath).png().toBuffer();
  fs.mkdirSync(path.dirname(PREVIEW_PATH), { recursive: true });
  await sharp({
    create: {
      width: margin + SHEET_W,
      height: SHEET_H,
      channels: 4,
      background: { r: 20, g: 22, b: 28, alpha: 1 },
    },
  })
    .composite([
      { input: sheetPng, left: margin, top: 0 },
      { input: Buffer.from(previewSvg(rows)), left: 0, top: 0 },
    ])
    .png()
    .toFile(PREVIEW_PATH);
  console.log(`[OK] ${PREVIEW_PATH}`);
}

function buildTestSvg() {
  const cells = [];
  for (const def of TEST_ROWS) {
    for (let col = 0; col < COLS; col++) {
      const used = col < def.frames;
      const x = col * CELL_W + 4;
      const y = def.row * CELL_H + 4;
      const w = CELL_W - 8;
      const h = CELL_H - 8;
      const opacity = used ? 0.92 : 0.28;
      const label = used ? `${def.key.toUpperCase()} ${def.frames}f` : 'unused';
      cells.push(
        `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="14" fill="${def.color}" opacity="${opacity}" stroke="rgba(255,255,255,.45)" stroke-width="2"/>`,
        `<text x="${x + 14}" y="${y + 30}" font-family="Segoe UI, Arial, sans-serif" font-size="17" font-weight="700" fill="#ffffff">${esc(label)}</text>`,
        `<text x="${x + 14}" y="${y + 54}" font-family="Consolas, monospace" font-size="14" fill="rgba(255,255,255,.85)">row ${def.row} · frame ${col + 1}</text>`,
        `<text x="${x + w - 14}" y="${y + h - 16}" text-anchor="end" font-family="Consolas, monospace" font-size="12" fill="rgba(255,255,255,.6)">remielle-test</text>`
      );
    }
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${SHEET_W}" height="${SHEET_H}" viewBox="0 0 ${SHEET_W} ${SHEET_H}">${cells.join('')}</svg>`;
}

async function makeTestPackage(outDir) {
  fs.mkdirSync(outDir, { recursive: true });
  const svg = buildTestSvg();
  const sheetPath = path.join(outDir, 'spritesheet.webp');
  await sharp(Buffer.from(svg))
    .webp({ quality: 92, effort: 4 })
    .toFile(sheetPath);
  fs.writeFileSync(
    path.join(outDir, 'pet.json'),
    JSON.stringify({
      id: 'remielle-test',
      displayName: 'Remielle Test',
      description: 'Qoder pet package compatibility test',
      spriteVersionNumber: 2,
      spritesheetPath: 'spritesheet.webp',
    }, null, 2) + '\n'
  );
  const meta = await sharp(sheetPath).metadata();
  const size = fs.statSync(sheetPath).size;
  if (meta.width !== SHEET_W || meta.height !== SHEET_H) {
    throw new Error(`spritesheet 尺寸错误: ${meta.width}x${meta.height}`);
  }
  if (size > 32 * 1024 * 1024) {
    throw new Error(`spritesheet 超过 32MB: ${size}`);
  }
  console.log(`[OK] ${sheetPath} (${meta.width}x${meta.height}, ${(size / 1024).toFixed(1)}KB, alpha=${meta.hasAlpha})`);
  console.log(`[OK] ${path.join(outDir, 'pet.json')}`);
}

async function main() {
  const args = process.argv.slice(2);
  const test = args.includes('--test');
  const outIndex = args.indexOf('--out');
  const outDir = outIndex >= 0
    ? path.resolve(args[outIndex + 1])
    : (test ? path.join(os.tmpdir(), 'remielle-qoder-test') : DEFAULT_OUT);
  if (test) {
    await makeTestPackage(outDir);
  } else {
    await makePetPackage(outDir);
  }
  console.log(`输出目录: ${outDir}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
