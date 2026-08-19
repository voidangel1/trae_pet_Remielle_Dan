# ClawdPet — 蕾米埃尔 · Clawd 桌面宠物（Trae IDE）

本项目基于 `https://github.com/6666ccc/codex_pet_Remielle_Dan` 进行二次开发。
现在版本号为 1.0。

一个运行在 Windows 桌面上的 Electron 桌宠，通过本地 HTTP 桥（端口 27315）接收 Trae IDE 扩展与 Hooks 推送的事件，实时反映 Agent 的工作状态（空闲 / 注意 / 思考 / 编辑 / 通知 / 错误）。

## 功能

- 六种状态动画（360×360 GIF）：idle / attention / thinking / editing / notification / error
- Agent 工作时持续显示 thinking / editing，完成后自动回到 idle
- 拖拽移动（右键菜单「可移动」解锁）、单击、双击（摇晃 + error 表情）等交互
- 单实例运行，多个 Trae 项目共享同一只宠物
- F12 打开 DevTools 调试

## 安装

1. 解压发布包（或直接克隆本仓库）
2. 右键 `install.ps1` → 「使用 PowerShell 运行」
3. 重启 Trae IDE

脚本会自动：
- 安装桥接扩展到 Trae（CN / 国际版自适应）
- 用国内镜像安装 Electron 运行时
- 配置 Trae 设置（自动启动 + 项目路径）
- 注册全局 Hooks（UserPromptSubmit / PreToolUse / PostToolUse / Notification / Stop）

## 开发

```bash
npm install
npm start      # 运行宠物
npm run build  # 打包便携版 exe
.\release.ps1  # 生成可分发的 zip 发布包
```

## 目录结构

```
main.js      Electron 主进程（窗口、拖拽、本地桥接服务器）
pet.js       渲染进程（状态机、交互）
preload.js   上下文桥接
index.html   页面入口
style.css    样式
hook-handler.js   Trae Hooks 处理器
trae-pet-bridge/  Trae 扩展（转发事件到本地桥）
Remielle_Dan-clawd-theme-v0.5.4/  主题资源（GIF + theme.json）
```

## 已知问题修复（v1.0）

修复了 Windows 显示缩放非 100%（如 125% / 150%）的屏幕上，拖拽宠物时窗口不断变大的问题（Electron 上游 bug electron#48247）。拖拽现在通过 `setBounds` 显式携带固定宽高实现，并附带 `resize` 事件兜底，窗口尺寸恒定 180×180。
