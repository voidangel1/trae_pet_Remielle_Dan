// Remielle GIF → Trae 原生桌宠（TRAE Bao）动画 webp 转换
//
// 输出两组素材（均部署到官方 icons\traebao\ 目录，互不冲突）：
//
// 1) 官方四态位（主进程 _resolveIconPaths 会引用的固定文件名，Trae 3.4.7 起）：
//    idle-loop  → default.png   （静态 fallback 图；3D 渲染禁用后的待机位，
//                                 Hooks 轮询空闲态会改用下方 default.webp 动画）
//    thinking   → busy.webp     （有运行中会话，Hooks 桥不可用时的回落表现）
//    editing    → super-busy.webp（忙碌满 3 分钟，每 180s 播 10s）
//    attention  → hover-drag.webp（悬停/拖拽）
//    notification → voice.webp  （3.4.7 新增语音态）
//
// 2) 六态精细素材（deploy-step-main.ps1 注入的 Hooks 轮询脚本按状态名引用）：
//    idle(动画) / thinking / editing / notification / error 独立文件。
//    —— 使原生桌宠恢复 v1.x 独立版的完整六态状态机（PreToolUse→editing 等）。
//
// 用法: node convert-native-pet.js  （需已 npm install sharp）
const sharp = require('sharp');
const path = require('path');
const fs = require('fs');

const ASSETS = path.join(__dirname, '..', 'Remielle_Dan-clawd-theme-v0.5.4', 'assets');
const OUT = path.join(__dirname, 'assets');

// [源 GIF, 输出文件]，同一源可输出多个目标（全部输出动画 webp）
const MAPPING = [
  ['idle-loop.gif', 'default.webp'],
  ['thinking.gif', 'busy.webp'],
  ['editing.gif', 'super-busy.webp'],
  ['attention.gif', 'hover-drag.webp'],
  ['notification.gif', 'voice.webp'],
  ['thinking.gif', 'thinking.webp'],
  ['editing.gif', 'editing.webp'],
  ['notification.gif', 'notification.webp'],
  ['error.gif', 'error.webp'],
];

// 静态 PNG（官方 3.4.7 default.png 位：取 GIF 首帧，PNG 不支持动画）
const PNG_OUT = [['idle-loop.gif', 'default.png']];

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  for (const [src, dst] of MAPPING) {
    const from = path.join(ASSETS, src);
    const to = path.join(OUT, dst);
    const meta = await sharp(from, { animated: true }).metadata();
    await sharp(from, { animated: true })
      .webp({ quality: 90, effort: 4 })
      .toFile(to);
    const kb = (fs.statSync(to).size / 1024).toFixed(1);
    console.log(`${src} (${meta.pages}帧) -> ${dst} (${kb}KB)`);
  }
  for (const [src, dst] of PNG_OUT) {
    const from = path.join(ASSETS, src);
    const to = path.join(OUT, dst);
    await sharp(from).png().toFile(to); // 不带 animated：静态首帧
    const kb = (fs.statSync(to).size / 1024).toFixed(1);
    console.log(`${src} (首帧) -> ${dst} (${kb}KB)`);
  }
  console.log('转换完成，运行 deploy-native-pet.ps1 部署到 Trae。');
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
