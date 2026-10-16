// ClawdPetInstaller.cs — Remielle 桌宠 v2.0.2 安装器（WinForms，csc 编译，零依赖）
//
// 功能：
//   完整安装：原生六态（Trae Hooks 驱动）+ 官方四态可切换 + 扩展 + hooks.json
//             + 部署 hook-handler 运行时 + 安装后指引
//             （无 node 时自动从国内镜像 npmmirror 下载 Node LTS，官方源兜底）
//   轻量安装：仅官方四态皮肤 + 尺寸补丁（不动 floatingButton.html / 扩展 / Hooks，最稳定）
//   卸载    ：还原官方状态——回滚 v2.0 全部补丁与素材、清理旧版本（1.0-1.4 独立宠物、
//             扩展、hooks.json、settings.json、自启动、运行时目录、下载的 Node）
//
// Trae 定位：运行中进程 → 常见位置 + 全盘符 Program Files → 注册表卸载表
//            （大小写不敏感 + DisplayIcon 推导，覆盖任意自定义安装路径），
//            全部失败时弹文件选择框让用户手动指认 Trae CN.exe。
//
// 皮肤素材 / 官方原始素材 / 扩展 / hook-handler / 轮询脚本全部嵌入 EXE 资源，
// 单文件分发，无需携带项目目录。
// 网络端口参考：27315（独立版宠物桥）、27416（原生悬浮窗状态服务器，由扩展内置）。
//
// 构建见 build-installer.ps1（csc /resource 嵌入）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Drawing;

namespace ClawdPetInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Program Files 补丁需要管理员：非提权时自我重启
            if (!IsAdmin())
            {
                try
                {
                    var pi = new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true };
                    Process.Start(pi);
                }
                catch { MessageBox.Show("需要管理员权限才能安装/卸载。", "ClawdPet 安装器"); }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    // ============================ UI ============================
    class MainForm : Form
    {
        readonly RadioButton rbFull = new RadioButton();
        readonly RadioButton rbLight = new RadioButton();
        readonly Button btnInstall = new Button();
        readonly Button btnUninstall = new Button();
        readonly TextBox txtLog = new TextBox();
        readonly Label lblStatus = new Label();

        public MainForm()
        {
            Text = "ClawdPet v2.0.2 安装器（Remielle × Trae）";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 460);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(590, 22);
            lblStatus.Location = new Point(15, 12);
            lblStatus.Text = "正在检测 Trae ...";
            Controls.Add(lblStatus);

            var g = new GroupBox();
            g.Text = "安装模式";
            g.Size = new Size(590, 92);
            g.Location = new Point(15, 40);

            rbFull.Text = "完整安装：Hooks 六态（编辑/思考/通知/提问）+ 可切换官方四态 + 扩展 + 安装后指引";
            rbFull.AutoSize = false;
            rbFull.Size = new Size(570, 26);
            rbFull.Location = new Point(10, 24);
            rbFull.Checked = true;
            g.Controls.Add(rbFull);

            rbLight.Text = "轻量安装：仅官方四态皮肤（静立/忙碌/卖力/悬停），不动悬浮窗页面与 Hooks，最稳定";
            rbLight.AutoSize = false;
            rbLight.Size = new Size(570, 26);
            rbLight.Location = new Point(10, 54);
            g.Controls.Add(rbLight);
            Controls.Add(g);

            btnInstall.Text = "安 装";
            btnInstall.Size = new Size(180, 38);
            btnInstall.Location = new Point(15, 145);
            btnInstall.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnInstall.Click += (s, e) => RunAsync(() => Install(rbFull.Checked));
            Controls.Add(btnInstall);

            btnUninstall.Text = "卸载（v2.0 + 旧版 1.0-1.4，还原官方）";
            btnUninstall.Size = new Size(390, 38);
            btnUninstall.Location = new Point(215, 145);
            btnUninstall.Click += (s, e) => RunAsync(UninstallAll);
            Controls.Add(btnUninstall);

            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.Size = new Size(590, 250);
            txtLog.Location = new Point(15, 195);
            Controls.Add(txtLog);

            Load += (s, e) => RunAsync(Detect);
        }

        void RunAsync(Action action)
        {
            btnInstall.Enabled = btnUninstall.Enabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { action(); }
                catch (Exception ex) { Log("[错误] " + ex.Message); }
                BeginInvoke((Action)(() => btnInstall.Enabled = btnUninstall.Enabled = true));
            });
        }

        public void Log(string line)
        {
            BeginInvoke((Action)(() =>
            {
                txtLog.AppendText(line + "\r\n");
            }));
        }

        void Detect()
        {
            TraeInfo t = Installer.DetectTrae();
            if (t == null)
            {
                lblStatus.BeginInvoke((Action)(() => lblStatus.Text = "未找到 Trae CN（可点「安装」手动选择 Trae CN.exe 位置）"));
                Log("[提示] 未自动检测到 Trae CN（未运行 + 非常见安装位置）。");
                Log("           点击「安装」后会弹出文件选择框，手动指定 Trae CN.exe 即可。");
                return;
            }
            bool patched = Installer.IsV2Installed(t);
            bool oldExt = Installer.HasExtension();
            string old = oldExt ? "，发现旧版扩展" : "";
            string v2 = patched ? "已安装 v2.0 补丁" : "未安装";
            lblStatus.BeginInvoke((Action)(() => lblStatus.Text = string.Format("Trae: {0}（{1}{2}）", t.BaseDir, v2, old)));
            Log("检测到 Trae：" + t.BaseDir);
            Log("原生素材目录：" + t.PetDir);
            Log("当前状态：" + v2 + old);
            string node = Installer.FindNodeExe() ?? Installer.FindNodeOnPath();
            Log(node != null
                ? "Hook 运行时：" + node
                : "[提示] 未找到 node，完整安装时将从国内镜像（npmmirror）自动下载 Node LTS（约 30 MB）");
        }

        // 自动检测失败时让用户手动指认 Trae CN.exe（跨线程调用，转发到 UI 线程）
        TraeInfo BrowseForTrae()
        {
            TraeInfo result = null;
            var iar = BeginInvoke((Action)(() => { result = BrowseForTraeUi(); }));
            iar.AsyncWaitHandle.WaitOne();
            return result;
        }

        TraeInfo BrowseForTraeUi()
        {
            DialogResult r = MessageBox.Show(this,
                "未自动找到 Trae CN（可能安装在非常规位置）。\n\n是否手动选择「Trae CN.exe」？",
                "未找到 Trae", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return null;
            using (var ofd = new OpenFileDialog
            {
                Title = "选择 Trae CN.exe（通常在 Trae CN 安装目录下）",
                Filter = "Trae CN.exe|Trae CN.exe|可执行文件|*.exe",
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return null;
                TraeInfo t = Installer.BuildTraeInfo(Path.GetDirectoryName(ofd.FileName));
                if (t == null)
                    MessageBox.Show(this,
                        "该目录不像 Trae CN 安装目录（未找到 resources\\app\\out\\main.js）。\n请选择 Trae CN.exe 所在的安装根目录。",
                        "无效位置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return t;
            }
        }

        void Install(bool full)
        {
            var t = Installer.DetectTrae();
            if (t == null) t = BrowseForTrae();
            if (t == null) { Log("[错误] 未检测到 Trae，已取消安装。"); return; }

            if (Installer.IsTraeRunning())
            {
                DialogResult r = MessageBox.Show(
                    "检测到 Trae 正在运行！\n\n" +
                    "Trae 只在启动时读取 hooks.json / settings.json，运行中修改不会生效，\n" +
                    "且 Trae 退出时可能把旧配置写回覆盖。\n\n" +
                    "强烈建议：先完全退出 Trae（托盘右键 → 退出），再点「是」继续。\n\n是否继续？",
                    "Trae 正在运行", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { Log("已取消。"); return; }
                Log("[提示] Trae 正在运行，安装后必须完全重启 Trae（不是重载窗口）才能生效。");
            }

            Log(full ? "—— 开始完整安装 ——" : "—— 开始轻量安装 ——");
            Installer.InstallSkin(t, Log, full);                // 素材 + 官方备份 + qF/XF + CSS 尺寸
            Installer.SetButtonIcon(t.BaseDir, Log);              // settings.json: buttonIcon=traebao
            if (full)
            {
                Installer.InjectPoller(t, Log);                   // 悬浮窗六态轮询脚本
                string runtime = Installer.DeployRuntime(Log);    // %LOCALAPPDATA%\ClawdPet-v2\hook-handler.js
                Installer.InstallExtension(t, runtime, Log);
                Installer.RegisterHooks(runtime, Log);
                Installer.ConfigureBridge(runtime, Log);          // settings.json: petPath + autoLaunch=false
            }
            Log("—— 安装完成，重启 Trae 后生效 ——");

            // 安装后指引
            BeginInvoke((Action)(() => new GuideForm(full).ShowDialog(this)));
        }

        void UninstallAll()
        {
            var t = Installer.DetectTrae();
            Log("—— 卸载：还原官方状态 ——");
            if (t != null)
            {
                Installer.RestoreSkin(t, Log);
                Installer.SetButtonIcon(t.BaseDir, Log, restore: true);
            }
            else Log("[跳过] 未找到 Trae，跳过 v2.0 还原。");
            Installer.RemoveExtension(Log);
            Installer.RemoveHooks(Log);
            Installer.CleanupSettings(Log);
            Installer.RemoveRuntime(Log);
            Installer.CleanupOldPet(this, Log);                   // 1.0-1.4 独立宠物 + 自启动
            Log("—— 卸载完成，重启 Trae 后回到官方原样 ——");
        }
    }

    // 安装后指引
    class GuideForm : Form
    {
        public GuideForm(bool full)
        {
            Text = full ? "安装完成 — 后续指引（完整模式）" : "安装完成 — 后续指引（轻量模式）";
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            var rtb = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Size = new Size(540, 340), Location = new Point(10, 10) };
            rtb.Font = new Font("Microsoft YaHei UI", 9.5F);
            string s = "安装成功！按以下步骤启用：\n\n" +
                       "1. 完全退出 Trae：右下角托盘图标右键 → 退出（重载窗口无效，\n" +
                       "   Trae 只在启动时读取 Hooks 配置）。\n" +
                       "2. 重新打开 Trae，屏幕右下角出现 140px 的 Remielle 悬浮宠物。\n";
            if (full)
            {
                s += "3. Trae 状态栏右下角显示 Pet: 原生·六态 —— 点击可在\n" +
                     "   「Hooks 六态 / 官方四态」之间切换（会记住选择）。\n\n" +
                     "状态语义（Hooks 六态）：\n" +
                     "   你提问            → thinking（思考）\n" +
                     "   编辑/查看/调用命令 → editing（编辑）\n" +
                     "   模型向你提问      → notification（常驻等待回答）\n" +
                     "   任务完成/异常     → idle / error\n\n" +
                     "常见问题：\n" +
                     "   · Trae 升级后宠物变回圆球：重新运行本安装器（完整或轻量均可），\n" +
                     "     一键恢复全部补丁。\n" +
                     "   · 想回到官方圆球：运行本安装器 → 卸载。\n" +
                     "   · 想用独立窗口版（v1.x）：使用项目内的独立版安装。\n";
            }
            else
            {
                s += "3. 轻量模式不依赖任何扩展/Hook/后台进程：宠物状态由 Trae 官方逻辑驱动\n" +
                     "   （静立 / 会话忙碌 / 卖力 / 悬停拖拽），最稳定。\n\n" +
                     "常见问题：\n" +
                     "   · Trae 升级后宠物变回圆球：重新运行本安装器。\n" +
                     "   · 想要六态表情（编辑/思考/提问）：重新运行安装器选「完整安装」。\n" +
                     "   · 想回到官方圆球：运行本安装器 → 卸载。\n";
            }
            rtb.Text = s;
            Controls.Add(rtb);
            var ok = new Button { Text = "知道了", DialogResult = DialogResult.OK, Size = new Size(100, 32), Location = new Point(450, 375) };
            Controls.Add(ok);
        }
    }

    // ============================ 安装引擎 ============================
    class TraeInfo { public string BaseDir, MainJs, PetDir, ButtonHtml; }

    static class Installer
    {
        // ---- 补丁锚点（与 deploy-step-main.ps1 完全一致） ----
        const string QGate = "function qF(t){return\"ball\"}";
        const string QPatch = "function qF(t){return t===\"ball\"?\"ball\":\"traebao\"}";
        const string XfGate = "XF={ball:60,traebao:100}";
        const string XfPatch = "XF={ball:60,traebao:140}";
        const string XfLegacy = "XF={ball:60,traebao:180}";
        // Trae 3.5（2026-10 重构）：qF gate 改名 tB（function tB(t){return"ball"}，语义未变，
        // 强制归一为 ball 并写回 settings），尺寸表 XF 改名 oB —— 新旧锚点都打，升级后重跑安装器即可恢复
        const string ObGate = "oB={ball:60,traebao:100}";
        const string ObPatch = "oB={ball:60,traebao:140}";
        const string TbGate = "function tB(t){return\"ball\"}";
        const string TbPatch = "function tB(t){return t===\"ball\"?\"ball\":\"traebao\"}";
        // 拖拽修复：非 16:9 屏（16:10/3:2 等）Windows 默认缩放 125%/150%，高 DPI 下 renderer 的
        // e.screenX 与主进程 DIP 坐标错位 → 拖动时窗口加速跑离鼠标（"宠物自行反向移动"），
        // 鼠标甩出窗口后 mouseup 丢失 → 拖拽状态残留 → 松开后点击失灵。
        // 补丁：drag-move 改用 screen.getCursorScreenPoint() 主进程权威 DIP 坐标。
        // em 为 bundle 中混淆的 screen 模块（同文件有 em.getDisplayNearestPoint），跨版本改名时
        // ReferenceError 被 catch 吞掉、退回原始坐标，仅失去修复不引入新问题。
        const string DragGate = "an.on(\"icube-floating-chat:drag-move\",(e,i,r)=>{";
        const string DragPatch = "an.on(\"icube-floating-chat:drag-move\",(e,i,r)=>{try{const __cp=em.getCursorScreenPoint();i=__cp.x;r=__cp.y}catch(__e){}";
        const string CssBtnGate = ".icon-traebao .btn {\n\twidth: 100px;\n\theight: 100px;\n}";
        const string CssBtnPatch = ".icon-traebao .btn {\n\twidth: 140px;\n\theight: 140px;\n}";
        const string CssBtnLegacy = ".icon-traebao .btn {\n\twidth: 180px;\n\theight: 180px;\n}";
        const string CssImgGate = ".btn img { object-fit: cover; pointer-events: none; width: 80px; height: 80px; }";
        const string CssImgPatch = ".btn img { object-fit: cover; pointer-events: none; width: 140px; height: 140px; }";
        const string CssImgLegacy = ".btn img { object-fit: cover; pointer-events: none; width: 180px; height: 180px; }";
        const string PollStart = "<!--CLAWD-PET-POLL-START-->";
        const string PollEnd = "<!--CLAWD-PET-POLL-END-->";

        static readonly string[] SkinFiles = { "default.webp", "busy.webp", "super-busy.webp", "hover-drag.webp" };
        static readonly string[] ExtraFiles = { "thinking.webp", "editing.webp", "notification.webp", "error.webp" };
        // 官方原始素材（卸载时无备份也可还原）
        static readonly string[] OfficialFiles = { "busy.webp", "default.webp", "hover-drag.webp", "super-busy.webp" };

        static string TraeDir()
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appdata, "Trae CN");
        }
        static string UserRoot() { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        static string SettingsJson() { return Path.Combine(TraeDir(), "User", "settings.json"); }
        static string HooksJson() { return Path.Combine(UserRoot(), ".trae-cn", "hooks.json"); }
        public static string RuntimeDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawdPet-v2"); }
        public static string ExtDir() { return Path.Combine(UserRoot(), ".trae-cn", "extensions"); }

        // ---------- Trae 检测 ----------
        // 检测链：运行中进程 → 常见/全盘符安装位置 → 注册表卸载信息（任意自定义安装路径）。
        // 任一环节命中后用 BuildTraeInfo 校验 main.js / 素材目录存在，防止拿到假目录。
        public static TraeInfo DetectTrae()
        {
            string baseDir = null;
            // 1) 运行中的 Trae（最准）
            var procs = Process.GetProcessesByName("Trae CN");
            foreach (var p in procs)
            {
                try { baseDir = Path.GetDirectoryName(p.MainModule.FileName); }
                catch { }
                break;
            }
            // 2) 常见位置 + 所有盘符的 Program Files（覆盖 D:\Program Files\Trae CN 等自定义盘安装）
            if (baseDir == null)
            {
                var guesses = new List<string>
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Trae CN"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Trae CN"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Trae CN"),
                };
                try
                {
                    foreach (string drive in Environment.GetLogicalDrives())
                    {
                        guesses.Add(Path.Combine(drive, "Program Files", "Trae CN"));
                        guesses.Add(Path.Combine(drive, "Program Files (x86)", "Trae CN"));
                    }
                }
                catch { }
                foreach (var guess in guesses)
                    if (Directory.Exists(guess) && BuildTraeInfo(guess) != null) { baseDir = guess; break; }
            }
            // 3) 注册表卸载表（Trae 用 Inno Setup 安装，DisplayName 实际是 "TRAE CN (User)"，
            //    大小写不敏感匹配 + WOW6432Node + DisplayIcon 推导）
            if (baseDir == null) baseDir = FindTraeInRegistry();
            if (baseDir == null) return null;
            return BuildTraeInfo(baseDir);
        }

        static string FindTraeInRegistry()
        {
            try
            {
                foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                    foreach (string wow in new[]
                    {
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                    })
                    {
                        using (var k = root.OpenSubKey(wow))
                        {
                            if (k == null) continue;
                            foreach (var sub in k.GetSubKeyNames())
                            {
                                using (var sk = k.OpenSubKey(sub))
                                {
                                    if (sk == null) continue;
                                    string name = (sk.GetValue("DisplayName") ?? "").ToString();
                                    if (name.IndexOf("trae", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    // InstallLocation 优先；缺失/失效时从 DisplayIcon（指向 Trae CN.exe）推导
                                    string dir = sk.GetValue("InstallLocation") as string;
                                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                                    {
                                        string icon = (sk.GetValue("DisplayIcon") ?? "").ToString().Split(',')[0].Trim('"').Trim();
                                        if (icon.Length > 0 && File.Exists(icon)) dir = Path.GetDirectoryName(icon);
                                    }
                                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && BuildTraeInfo(dir) != null)
                                        return dir;
                                }
                            }
                        }
                    }
            }
            catch { }
            return null;
        }

        // 从安装根目录构造 TraeInfo（目录不完整时返回 null，供多级回退校验）
        public static TraeInfo BuildTraeInfo(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;
            var t = new TraeInfo
            {
                BaseDir = baseDir,
                MainJs = Path.Combine(baseDir, "resources", "app", "out", "main.js"),
                PetDir = Path.Combine(baseDir, "resources", "app", "out", "vs", "platform", "iCubeFloatingChat", "electron-main", "media", "icons", "traebao"),
                ButtonHtml = Path.Combine(baseDir, "resources", "app", "out", "vs", "platform", "iCubeFloatingChat", "electron-main", "media", "floatingButton.html"),
            };
            if (!File.Exists(t.MainJs) || !Directory.Exists(t.PetDir)) return null;
            return t;
        }

        public static bool IsTraeRunning()
        {
            return Process.GetProcessesByName("Trae CN").Length > 0;
        }

        public static bool IsV2Installed(TraeInfo t)
        {
            try
            {
                string main = File.ReadAllText(t.MainJs);
                return main.Contains(XfPatch) || main.Contains(ObPatch);
            }
            catch { return false; }
        }

        public static bool HasExtension()
        {
            return Directory.Exists(ExtDir()) && Directory.EnumerateDirectories(ExtDir(), "remielle.trae-pet-bridge-*").Any();
        }

        // ---------- Node 运行时（Hook 执行器） ----------
        // 查找顺序：Trae 托管 node → 系统 PATH → 上次下载的副本 → 国内镜像自动下载安装
        // （Trae 的 binaries\node 只在用户触发过某功能后才下载，全新环境往往没有）
        public static string FindNodeExe()
        {
            string baseDir = Path.Combine(UserRoot(), ".trae-cn", "binaries", "node", "versions");
            if (!Directory.Exists(baseDir)) return null;
            var dirs = Directory.GetDirectories(baseDir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                string node = Path.Combine(d, "node.exe");
                if (File.Exists(node)) return node;
            }
            return null;
        }

        public static string FindNodeOnPath()
        {
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in pathEnv.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string p = Path.Combine(dir.Trim(), "node.exe");
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        // 完整安装时调用：保证有一个可用的 node.exe，没有就从国内镜像装一个
        public static string EnsureNodeExe(Action<string> log)
        {
            string node = FindNodeExe();
            if (node != null) { log("[OK] 使用 Trae 内置 node：" + node); return node; }

            node = FindNodeOnPath();
            if (node != null) { log("[OK] 使用系统 node：" + node); return node; }

            node = Path.Combine(RuntimeDir(), "node", "node.exe");
            if (File.Exists(node)) { log("[OK] 使用已下载的 node：" + node); return node; }

            log("未找到可用 node，开始从国内镜像（npmmirror）下载 Node LTS ...");
            return DownloadNode(log);
        }

        // Node LTS（Krypton）；hook-handler 只用 net/fs/path 基础模块，任何 LTS 均可。
        // 源顺序：npmmirror（原淘宝镜像，国内直连）→ 官方 nodejs.org 兜底。
        const string NodeLtsVersion = "24.21.0";

        static string DownloadNode(Action<string> log)
        {
            // npmmirror / nodejs.org 均要求 TLS 1.2+（.NET 4.0 枚举缺失，用数值强转）
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;

            string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            string zipName = string.Format("node-v{0}-win-{1}.zip", NodeLtsVersion, arch);
            string[] sources =
            {
                "https://cdn.npmmirror.com/binaries/node/v" + NodeLtsVersion + "/" + zipName,
                "https://npmmirror.com/mirrors/node/v" + NodeLtsVersion + "/" + zipName,
                "https://nodejs.org/dist/v" + NodeLtsVersion + "/" + zipName,
            };

            string downloadDir = Path.Combine(RuntimeDir(), "downloads");
            string zipPath = Path.Combine(downloadDir, zipName);
            Directory.CreateDirectory(downloadDir);

            bool ok = false;
            foreach (string url in sources)
            {
                try
                {
                    log("[下载] " + url);
                    DownloadFile(url, zipPath, log);
                    ok = true;
                    break;
                }
                catch (Exception e) { log("[警告] 下载失败（" + e.Message + "），尝试下一个源 ..."); }
            }
            if (!ok) { try { File.Delete(zipPath); } catch { } return null; }

            // 校验 zip 魔数（源返回错误页时不是 zip）
            try
            {
                using (var fs = File.OpenRead(zipPath))
                    if (fs.ReadByte() != 'P' || fs.ReadByte() != 'K')
                        throw new Exception("下载内容不是有效的 zip 文件");
            }
            catch (Exception e)
            {
                log("[警告] Node 安装包校验失败：" + e.Message);
                try { File.Delete(zipPath); } catch { }
                return null;
            }

            // 解压（zip 内含顶层目录 node-vX-win-x64\），并把该目录上提为 node\
            try
            {
                string nodeDir = Path.Combine(RuntimeDir(), "node");
                if (Directory.Exists(nodeDir)) Directory.Delete(nodeDir, true);
                string tmp = Path.Combine(downloadDir, "extract");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tmp);
                string nodeExe = FindFileRecursive(tmp, "node.exe");
                if (nodeExe == null) throw new Exception("压缩包内未找到 node.exe");
                string srcDir = Path.GetDirectoryName(nodeExe);
                Directory.Move(srcDir, nodeDir);
                if (!string.Equals(srcDir, tmp, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(tmp, true); } catch { }
                try { File.Delete(zipPath); } catch { }
                log("[OK] Node v" + NodeLtsVersion + " 已安装到 " + nodeDir);
                return Path.Combine(nodeDir, "node.exe");
            }
            catch (Exception e)
            {
                log("[警告] Node 解压失败：" + e.Message);
                return null;
            }
        }

        static void DownloadFile(string url, string dst, Action<string> log)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ClawdPetInstaller/2.0.2";
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var f = File.Create(dst))
            {
                long total = resp.ContentLength, done = 0, last = 0;
                byte[] buf = new byte[64 * 1024];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    f.Write(buf, 0, n);
                    done += n;
                    if (total > 0 && done - last >= 5 * 1024 * 1024) // 每 5MB 报一次进度
                    {
                        log(string.Format("  已下载 {0:F1} / {1:F1} MB", done / 1048576.0, total / 1048576.0));
                        last = done;
                    }
                }
                if (total > 0 && done != total) throw new Exception("下载不完整");
                log(string.Format("  下载完成（{0:F1} MB）", done / 1048576.0));
            }
        }

        static string FindFileRecursive(string dir, string file)
        {
            string p = Path.Combine(dir, file);
            if (File.Exists(p)) return p;
            foreach (string d in Directory.GetDirectories(dir))
            {
                string r = FindFileRecursive(d, file);
                if (r != null) return r;
            }
            return null;
        }

        // ---------- 资源提取 ----------
        static void ExtractResource(string name, string dst)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("缺少嵌入资源 " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                using (var f = File.Create(dst)) s.CopyTo(f);
            }
        }

        // ---------- 安装：素材 + main.js/html 补丁 ----------
        public static void InstallSkin(TraeInfo t, Action<string> log, bool full)
        {
            // 官方素材首次备份：未打过补丁且本地还没有完整备份时才备份
            // （防两个坑：把我们的皮肤误存成"官方备份"；重复备份把还原基线覆盖掉）
            string mainText = File.ReadAllText(t.MainJs);
            bool already = mainText.Contains(QPatch) || mainText.Contains(XfPatch) || mainText.Contains(ObPatch);
            string backup = Path.Combine(RuntimeDir(), "official-backup");
            Directory.CreateDirectory(backup);
            bool backupComplete = Array.TrueForAll(OfficialFiles, f => File.Exists(Path.Combine(backup, f)));
            if (!already && !backupComplete)
            {
                foreach (string f in OfficialFiles)
                {
                    string src = Path.Combine(t.PetDir, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(backup, f), true);
                }
                log("[OK] 官方素材已备份（用于卸载还原）");
            }
            // 备份缺失（如用户手删）时用嵌入的官方素材兜底
            foreach (string f in OfficialFiles)
                if (!File.Exists(Path.Combine(backup, f))) ExtractResource("official_" + f, Path.Combine(backup, f));

            // 皮肤素材：官方四态位（两种模式共用）+ 六态扩展位（仅完整模式，轮询脚本按状态名引用）
            foreach (string f in SkinFiles) ExtractResource("skin_" + f, Path.Combine(t.PetDir, f));
            if (full)
            {
                foreach (string f in ExtraFiles) ExtractResource("skin_" + f, Path.Combine(t.PetDir, f));
                log("[OK] 皮肤已部署（官方四态 4 张 + 六态扩展 4 张）");
            }
            else
            {
                log("[OK] 官方四态皮肤已替换（" + SkinFiles.Length + " 张）");
            }

            // main.js：形象/尺寸补丁（先归一化旧版再打，幂等）。
            // 旧版（≤3.4）：qF gate + XF 尺寸表；新版（3.5+，2026-10）：gate 改名 tB、尺寸表改名 oB
            // —— 两套锚点都尝试，各自命中才生效。
            mainText = NormalizeMain(mainText);
            mainText = mainText.Replace(QGate, QPatch).Replace(TbGate, TbPatch)
                               .Replace(XfGate, XfPatch).Replace(ObGate, ObPatch)
                               .Replace(DragGate, DragPatch);
            File.WriteAllText(t.MainJs, mainText);
            bool newAnchor = mainText.Contains(ObPatch);
            bool dragFixed = mainText.Contains("__cp=em.getCursorScreenPoint()");
            log((newAnchor ? "[OK] main.js 补丁：tB gate 解锁 + 窗口 140px（3.5 新锚点）" : "[OK] main.js 补丁：qF 解锁（traebao 形象）+ 窗口 140px")
                + (dragFixed ? " + 拖拽高 DPI 修复" : ""));

            // floatingButton.html：CSS 尺寸（两种模式都改）
            if (File.Exists(t.ButtonHtml))
            {
                string html = File.ReadAllText(t.ButtonHtml);
                html = NormalizeHtml(html, false);
                html = html.Replace(CssBtnGate, CssBtnPatch).Replace(CssImgGate, CssImgPatch);
                File.WriteAllText(t.ButtonHtml, html);
                log("[OK] floatingButton.html：立绘 140px");
            }
        }

        // ---------- 安装：完整模式专属 ----------
        public static string DeployRuntime(Action<string> log)
        {
            string dir = RuntimeDir();
            Directory.CreateDirectory(dir);
            ExtractResource("rt_hook-handler.js", Path.Combine(dir, "hook-handler.js"));
            log("[OK] Hook 运行时已部署：" + dir);
            return dir;
        }

        public static void InstallExtension(TraeInfo t, string runtime, Action<string> log)
        {
            string dst = Path.Combine(ExtDir(), "remielle.trae-pet-bridge-2.0.2");

            // 首选：VSIX + Trae CLI 官方安装流程。直接往 extensions 目录拷贝的"旁载"方式
            // 已被 Trae CN 封锁：启动扫描时对未注册目录执行 "Marked extension as removed"，
            // 写入 .obsolete 并拒绝加载（状态栏/六态服务器全部不出现）。
            // CLI 安装会走 Trae 自己的注册流程（写 extensions.json + unset 移除标记）。
            if (t != null && TryInstallViaCli(t, log))
            {
                CleanObsolete(log);
                log("[OK] 桥接扩展已通过 Trae CLI 安装注册（remielle.trae-pet-bridge-2.0.2）");
                return;
            }

            // 回退：CLI 不可用/失败时直接拷目录（Trae 若清除它，重跑安装器并先退出 Trae）
            Directory.CreateDirectory(ExtDir());
            // 清掉旧版本目录（0.x/2.0.0 及更早）
            foreach (string old in Directory.EnumerateDirectories(ExtDir(), "remielle.trae-pet-bridge-*"))
                try { Directory.Delete(old, true); } catch { }
            Directory.CreateDirectory(dst);
            ExtractResource("ext_extension.js", Path.Combine(dst, "extension.js"));
            ExtractResource("ext_package.json", Path.Combine(dst, "package.json"));
            CleanObsolete(log);
            log("[警告] 已回退为目录拷贝方式。若重启 Trae 后状态栏不显示，请完全退出 Trae 后重跑安装器（走 CLI 注册）。");
        }

        // ---------- VSIX + CLI 安装 ----------
        // 调用 Trae 自带的 CLI（等价于 bin\trae-cn.cmd）：
        //   ELECTRON_RUN_AS_NODE=1 "Trae CN.exe" resources\app\out\cli.js --install-extension <vsix> --force
        static bool TryInstallViaCli(TraeInfo t, Action<string> log)
        {
            try
            {
                string traeExe = Path.Combine(t.BaseDir, "Trae CN.exe");
                string cliJs = Path.Combine(t.BaseDir, "resources", "app", "out", "cli.js");
                if (!File.Exists(traeExe) || !File.Exists(cliJs))
                {
                    log("[提示] 未找到 Trae CLI，扩展将退回目录拷贝方式。");
                    return false;
                }
                string vsix = BuildVsix();
                if (IsTraeRunning())
                    log("[提示] Trae 正在运行：扩展注册需要在 Trae 完全退出后才能成功，本次 CLI 安装可能失败。");
                var psi = new ProcessStartInfo(traeExe)
                {
                    Arguments = "\"" + cliJs + "\" --install-extension \"" + vsix + "\" --force",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
                psi.EnvironmentVariables.Remove("VSCODE_DEV");          // 与 bin\trae-cn.cmd 行为一致
                log("[安装] Trae CLI：注册扩展 remielle.trae-pet-bridge-2.0.2 ...");
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(90000))
                    {
                        try { p.Kill(); } catch { }
                        log("[警告] CLI 安装超时。");
                        return false;
                    }
                    if (p.ExitCode != 0)
                    {
                        string tail = (output + err).Trim();
                        if (tail.Length > 400) tail = tail.Substring(tail.Length - 400);
                        log("[警告] CLI 安装失败（exit " + p.ExitCode + "）：" + tail);
                        return false;
                    }
                }
                // 硬校验：extensions.json 真正出现注册条目才算成功
                if (!IsRegisteredInExtensionsJson())
                {
                    log("[警告] CLI 执行完成但未写入注册信息（可能 Trae 正在运行锁定了扩展目录）。");
                    return false;
                }
                return true;
            }
            catch (Exception e) { log("[警告] CLI 安装异常：" + e.Message); return false; }
        }

        static bool IsRegisteredInExtensionsJson()
        {
            try
            {
                string file = Path.Combine(ExtDir(), "extensions.json");
                if (!File.Exists(file)) return false;
                return File.ReadAllText(file).IndexOf("remielle.trae-pet-bridge", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        // 用嵌入资源现场打包 VSIX（zip 结构：extension.vsixmanifest + [Content_Types].xml + extension/*）
        static string BuildVsix()
        {
            string vsix = Path.Combine(RuntimeDir(), "downloads", "trae-pet-bridge-2.0.2.vsix");
            Directory.CreateDirectory(Path.GetDirectoryName(vsix));
            using (var zip = System.IO.Compression.ZipFile.Open(vsix, System.IO.Compression.ZipArchiveMode.Create))
            {
                AddTextEntry(zip, "extension.vsixmanifest", VsixManifest);
                AddTextEntry(zip, "[Content_Types].xml", VsixContentTypes);
                AddResourceEntry(zip, "extension/extension.js", "ext_extension.js");
                AddResourceEntry(zip, "extension/package.json", "ext_package.json");
            }
            return vsix;
        }

        static void AddTextEntry(System.IO.Compression.ZipArchive zip, string entryName, string content)
        {
            System.IO.Compression.ZipArchiveEntry e = zip.CreateEntry(entryName);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                w.Write(content);
        }

        static void AddResourceEntry(System.IO.Compression.ZipArchive zip, string entryName, string resourceName)
        {
            System.IO.Compression.ZipArchiveEntry e = zip.CreateEntry(entryName);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (s == null) throw new Exception("缺少嵌入资源 " + resourceName);
                using (var os = e.Open()) s.CopyTo(os);
            }
        }

        // Identity Id+Publisher 拼成扩展 ID remielle.trae-pet-bridge，与 extension/package.json 一致
        const string VsixManifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<PackageManifest Version=\"2.0.0\" xmlns=\"http://schemas.microsoft.com/developer/vsx-schema/2011\">\n" +
            "  <Metadata>\n" +
            "    <Identity Language=\"en-US\" Id=\"trae-pet-bridge\" Version=\"2.0.2\" Publisher=\"remielle\"/>\n" +
            "    <DisplayName>Trae Pet Bridge</DisplayName>\n" +
            "    <Description xml:space=\"preserve\">Push Trae coding events to desktop pet</Description>\n" +
            "    <Properties>\n" +
            "      <Property Id=\"Microsoft.VisualStudio.Code.Engine\" Value=\"^1.75.0\"/>\n" +
            "      <Property Id=\"Microsoft.VisualStudio.Code.ExtensionKind\" Value=\"workspace\"/>\n" +
            "    </Properties>\n" +
            "  </Metadata>\n" +
            "  <Installation>\n" +
            "    <InstallationTarget Id=\"Microsoft.VisualStudio.Code\"/>\n" +
            "  </Installation>\n" +
            "  <Assets>\n" +
            "    <Asset Type=\"Microsoft.VisualStudio.Code.Manifest\" Path=\"extension/package.json\"/>\n" +
            "  </Assets>\n" +
            "</PackageManifest>\n";

        const string VsixContentTypes =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
            "  <Default Extension=\"json\" ContentType=\"application/json\"/>\n" +
            "  <Default Extension=\"vsixmanifest\" ContentType=\"text/xml\"/>\n" +
            "  <Default Extension=\"js\" ContentType=\"text/javascript\"/>\n" +
            "</Types>\n";

        // 清理 extensions\.obsolete 中 remielle.trae-pet-bridge-* 条目（Trae/VS Code 的延迟删除清单）
        static void CleanObsolete(Action<string> log)
        {
            try
            {
                string file = Path.Combine(ExtDir(), ".obsolete");
                if (!File.Exists(file)) return;
                var ser = new JavaScriptSerializer();
                var dict = ser.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                if (dict == null) return;
                string[] stale = dict.Keys.Where(k => k.StartsWith("remielle.trae-pet-bridge-", StringComparison.Ordinal)).ToArray();
                if (stale.Length == 0) return;
                foreach (string k in stale) dict.Remove(k);
                if (dict.Count == 0) File.Delete(file);
                else File.WriteAllText(file, ser.Serialize(dict));
                log(string.Format("[OK] 已清除 .obsolete 延迟删除标记（{0} 条）", stale.Length));
            }
            catch (Exception e) { log("[警告] .obsolete 清理失败（若 Trae 正在运行，其进程锁定了该文件）：" + e.Message); }
        }

        public static void RegisterHooks(string runtime, Action<string> log)
        {
            // node 缺失不再跳过：PATH 有就用系统的，否则从国内镜像（npmmirror）自动下载一份
            string node = EnsureNodeExe(log);
            if (node == null)
            {
                log("[警告] 无法获得 Node 运行时（内置/系统/在线下载均失败），跳过 Hooks 注册（宠物将只有官方四态）。");
                return;
            }
            string handler = Path.Combine(runtime, "hook-handler.js").Replace("\\", "/");
            string hooksFile = HooksJson();
            Directory.CreateDirectory(Path.GetDirectoryName(hooksFile));

            // 备份原始 hooks.json（只备份一次，且当前不含我们的 handler）
            string backup = hooksFile + ".clawd-backup";
            bool ours = File.Exists(hooksFile) && File.ReadAllText(hooksFile).Contains("hook-handler.js");
            if (File.Exists(hooksFile) && !ours && !File.Exists(backup))
                File.Copy(hooksFile, backup);

            var ser = new JavaScriptSerializer();
            var root = File.Exists(hooksFile)
                ? (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(hooksFile)) ?? new Dictionary<string, object>()
                : new Dictionary<string, object>();
            var hooks = root.ContainsKey("hooks") ? (Dictionary<string, object>)root["hooks"] : new Dictionary<string, object>();
            foreach (string ev in new[] { "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop" })
            {
                var entry = new Dictionary<string, object>
                {
                    { "type", "command" },
                    // 注意 1：Trae 的 hook 命令解析不支持引号（带引号会静默不执行），
                    // 两个路径均无空格，安全无引号拼接
                    { "command", node + " " + handler },
                    { "timeout", 10 }
                };
                // 注意 2：Trae（原生 ai-agent，serde 反序列化）要求每个事件是
                //「matcher 配置数组」，且工具类事件带 matcher:"*"，与官方 Hooks UI
                // 生成格式一致；写成纯对象会被静默丢弃（hooks 完全不触发）。
                bool toolEvent = (ev == "PreToolUse" || ev == "PostToolUse" || ev == "Notification");
                var matcherCfg = new Dictionary<string, object>();
                if (toolEvent) matcherCfg["matcher"] = "*";
                matcherCfg["hooks"] = new object[] { entry };
                hooks[ev] = new object[] { matcherCfg };
            }
            root["hooks"] = hooks;
            root["version"] = 1;
            File.WriteAllText(hooksFile, ser.Serialize(root));
            // 回读校验：确认条目真正落盘（防写入被占用/编码问题静默失败）
            if (!File.ReadAllText(hooksFile).Contains(handler))
                log("[警告] hooks.json 写入后校验失败，请完全退出 Trae 后重试安装！");
            else
                log("[OK] Trae Hooks 已注册（五事件 → hook-handler）");
        }

        public static void ConfigureBridge(string runtime, Action<string> log)
        {
            EditSettings(dict =>
            {
                dict["traePetBridge.petPath"] = runtime;
                dict["traePetBridge.autoLaunch"] = false; // 原生模式避免双宠
            });
            log("[OK] settings.json：petPath/autoLaunch 已配置");
        }

        public static void SetButtonIcon(string baseDir, Action<string> log, bool restore = false)
        {
            EditSettings(dict =>
            {
                if (restore) dict.Remove("trae.floatingChat.buttonIcon");
                else dict["trae.floatingChat.buttonIcon"] = "traebao";
            });
            log(restore ? "[OK] settings.json：buttonIcon 已还原（官方默认）" : "[OK] settings.json：buttonIcon = traebao");
        }

        static void EditSettings(Action<Dictionary<string, object>> edit)
        {
            string file = SettingsJson();
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var ser = new JavaScriptSerializer();
            string text = File.Exists(file) ? File.ReadAllText(file) : "{}";
            // 尾注释（// …）在 settings.json 中合法，序列化前剥掉
            var dict = (Dictionary<string, object>)ser.DeserializeObject(text) ?? new Dictionary<string, object>();
            edit(dict);
            File.WriteAllText(file, ser.Serialize(dict));
        }

        // ---------- 卸载 ----------
        public static void RestoreSkin(TraeInfo t, Action<string> log)
        {
            // 1) 素材还原：优先本地备份，缺失时用嵌入官方素材
            string backup = Path.Combine(RuntimeDir(), "official-backup");
            foreach (string f in OfficialFiles)
            {
                string src = Path.Combine(backup, f);
                if (!File.Exists(src)) ExtractResource("official_" + f, src);
                File.Copy(src, Path.Combine(t.PetDir, f), true);
            }
            foreach (string f in ExtraFiles)
                try { File.Delete(Path.Combine(t.PetDir, f)); } catch { }
            log("[OK] 官方素材已还原（4 张），六态素材已清理");

            // 2) main.js 归一化
            string main = File.ReadAllText(t.MainJs);
            main = NormalizeMain(main);
            File.WriteAllText(t.MainJs, main);
            log("[OK] main.js 已还原官方（qF/XF 回滚）");

            // 3) html 归一化（CSS + 轮询脚本移除）
            if (File.Exists(t.ButtonHtml))
            {
                string html = File.ReadAllText(t.ButtonHtml);
                html = NormalizeHtml(html, true);
                File.WriteAllText(t.ButtonHtml, html);
                log("[OK] floatingButton.html 已还原官方（尺寸/轮询回滚）");
            }
        }

        public static void RemoveExtension(Action<string> log)
        {
            if (!Directory.Exists(ExtDir())) { log("[跳过] 无扩展目录。"); return; }
            int n = 0;
            foreach (string d in Directory.EnumerateDirectories(ExtDir(), "remielle.trae-pet-bridge-*"))
            {
                try { Directory.Delete(d, true); n++; } catch { }
            }
            log(n > 0 ? string.Format("[OK] 桥接扩展已移除（{0} 个目录）", n) : "[跳过] 未安装桥接扩展。");
        }

        public static void RemoveHooks(Action<string> log)
        {
            string hooksFile = HooksJson();
            if (!File.Exists(hooksFile)) { log("[跳过] 无 hooks.json。"); return; }
            string backup = hooksFile + ".clawd-backup";
            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, hooksFile, true);
                    log("[OK] hooks.json 已从备份还原。");
                }
                else
                {
                    var ser = new JavaScriptSerializer();
                    var root = ser.DeserializeObject(File.ReadAllText(hooksFile)) as Dictionary<string, object>;
                    if (root != null && root.ContainsKey("hooks"))
                    {
                        var hooks = root["hooks"] as Dictionary<string, object>;
                        if (hooks != null)
                        {
                            foreach (var ev in hooks.Keys.ToArray())
                            {
                                // 正确结构：事件 → matcher 配置数组（Trae 原生加载格式）
                                var matchers = hooks[ev] as object[];
                                if (matchers != null)
                                {
                                    var keptMatchers = new List<object>();
                                    foreach (var m in matchers)
                                    {
                                        var mc = m as Dictionary<string, object>;
                                        var inner = (mc != null && mc.ContainsKey("hooks")) ? mc["hooks"] as object[] : null;
                                        if (inner == null) { keptMatchers.Add(m); continue; }
                                        var keptHooks = inner.Where(h =>
                                        {
                                            var d = h as Dictionary<string, object>;
                                            object cmd = (d != null && d.ContainsKey("command")) ? d["command"] : null;
                                            return (cmd == null ? "" : cmd.ToString()).IndexOf("hook-handler.js", StringComparison.Ordinal) < 0;
                                        }).ToArray();
                                        if (keptHooks.Length > 0)
                                        {
                                            mc["hooks"] = keptHooks;
                                            keptMatchers.Add(mc);
                                        }
                                    }
                                    if (keptMatchers.Count == 0) hooks.Remove(ev);
                                    else hooks[ev] = keptMatchers.ToArray();
                                }
                                else
                                {
                                    // 兼容旧错误结构（纯对象）：直接按 command 过滤
                                    var v = hooks[ev] as Dictionary<string, object>;
                                    var list = (v != null && v.ContainsKey("hooks")) ? v["hooks"] as object[] : null;
                                    if (list == null) continue;
                                    var kept = list.Where(h =>
                                    {
                                        var d = h as Dictionary<string, object>;
                                        object cmd = (d != null && d.ContainsKey("command")) ? d["command"] : null;
                                        return (cmd == null ? "" : cmd.ToString()).IndexOf("hook-handler.js", StringComparison.Ordinal) < 0;
                                    }).ToArray();
                                    if (kept.Length == 0) hooks.Remove(ev); else v["hooks"] = kept;
                                }
                            }
                            if (hooks.Count == 0) root.Remove("hooks");
                            File.WriteAllText(hooksFile, ser.Serialize(root));
                        }
                    }
                    log("[OK] hooks.json 已清除 ClawdPet 条目。");
                }
            }
            catch (Exception e) { log("[警告] hooks.json 清理失败：" + e.Message); }
        }

        public static void CleanupSettings(Action<string> log)
        {
            EditSettings(dict =>
            {
                dict.Remove("traePetBridge.petPath");
                dict.Remove("traePetBridge.autoLaunch");
                dict.Remove("traePetBridge.serverPort");
                dict.Remove("traePetBridge.petSize");
            });
            log("[OK] settings.json：traePetBridge 配置已清理。");
        }

        public static void RemoveRuntime(Action<string> log)
        {
            try
            {
                if (Directory.Exists(RuntimeDir()))
                {
                    Directory.Delete(RuntimeDir(), true);
                    log("[OK] 运行时目录已删除：" + RuntimeDir());
                }
                else log("[跳过] 无运行时目录。");
            }
            catch (Exception e) { log("[警告] 运行时删除失败（可能被占用）：" + e.Message); }
        }

        // 旧版本（1.0-1.4 独立宠物）清理
        public static void CleanupOldPet(Form owner, Action<string> log)
        {
            // 1) 杀独立宠物进程
            int killed = 0;
            foreach (string procName in new[] { "ClawdPet" })
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { p.Kill(); killed++; } catch { }
                }
            // v1.x 开发模式：electron.exe 跑 main.js 的进程（命令行含 pet.js 无法直接判，跳过——由用户关闭）
            log(killed > 0 ? string.Format("[OK] 已结束 {0} 个独立宠物进程。", killed) : "[跳过] 无独立宠物进程。");

            // 2) 自启动清理（HKCU Run + 启动文件夹）
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k != null && k.GetValue("ClawdPet") != null)
                    {
                        k.DeleteValue("ClawdPet");
                        log("[OK] 已移除自启动（注册表 Run：ClawdPet）。");
                    }
                }
            }
            catch { }
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (string lnk in Directory.EnumerateFiles(startup, "*ClawdPet*.lnk"))
                {
                    File.Delete(lnk);
                    log("[OK] 已移除自启动（启动文件夹：" + Path.GetFileName(lnk) + "）。");
                }
            }
            catch { }

            // 3) 旧宠物文件夹（询问后删除，可跳过）
            string candidate = null;
            try
            {
                if (File.Exists(SettingsJson()))
                {
                    var dict = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(SettingsJson())) as Dictionary<string, object>;
                    if (dict != null && dict.ContainsKey("traePetBridge.petPath"))
                    {
                        string s = dict["traePetBridge.petPath"] as string;
                        if (s != null && Directory.Exists(s) && File.Exists(Path.Combine(s, "pet.js"))) candidate = s;
                    }
                }
            }
            catch { }
            if (candidate == null) return;

            DialogResult r = DialogResult.No;
            var iar = owner.BeginInvoke((Action)(() =>
            {
                r = MessageBox.Show(string.Format("发现旧版独立宠物目录：\n{0}\n\n是否删除该目录？（包含 Electron 项目文件，仅影响旧版宠物）", candidate),
                    "清理旧版本", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }));
            iar.AsyncWaitHandle.WaitOne();
            if (r == DialogResult.Yes)
            {
                try { Directory.Delete(candidate, true); log("[OK] 旧版宠物目录已删除：" + candidate); }
                catch (Exception e) { log("[警告] 删除失败（可能被占用）：" + e.Message); }
            }
            else log("[跳过] 保留旧版宠物目录：" + candidate);
        }

        // ---------- 补丁归一化（部署/卸载共用，幂等且兼容 180px 旧版与 3.5+ tB/oB/拖拽锚点） ----------
        static string NormalizeMain(string s)
        {
            return s.Replace(QPatch, QGate).Replace(TbPatch, TbGate)
                    .Replace(XfPatch, XfGate).Replace(XfLegacy, XfGate)
                    .Replace(ObPatch, ObGate)
                    .Replace(DragPatch, DragGate);
        }

        static string NormalizeHtml(string s, bool removePoll)
        {
            s = s.Replace(CssBtnPatch, CssBtnGate).Replace(CssBtnLegacy, CssBtnGate);
            s = s.Replace(CssImgPatch, CssImgGate).Replace(CssImgLegacy, CssImgGate);
            if (removePoll)
            {
                int a = s.IndexOf(PollStart, StringComparison.Ordinal);
                if (a >= 0)
                {
                    int b = s.IndexOf(PollEnd, a, StringComparison.Ordinal);
                    if (b >= 0)
                    {
                        int end = b + PollEnd.Length;
                        while (end < s.Length && (s[end] == '\n' || s[end] == '\r')) end++;
                        while (a > 0 && (s[a - 1] == '\n' || s[a - 1] == '\r')) a--;
                        s = s.Substring(0, a) + s.Substring(end);
                    }
                }
            }
            return s;
        }

        // 完整安装时注入轮询脚本（由 build 脚本从 poller.js 嵌入）
        public static void InjectPoller(TraeInfo t, Action<string> log)
        {
            if (!File.Exists(t.ButtonHtml)) return;
            string html = File.ReadAllText(t.ButtonHtml);
            if (html.Contains(PollStart)) { log("[跳过] 轮询脚本已存在。"); return; }
            string poller;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("rt_poller.html"))
                poller = new StreamReader(s, Encoding.UTF8).ReadToEnd();
            html = html.Replace("</body>", poller + "\n</body>");
            File.WriteAllText(t.ButtonHtml, html);
            log("[OK] 悬浮窗已注入 Hooks 六态轮询脚本。");
        }
    }
}
