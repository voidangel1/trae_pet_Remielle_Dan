// ClawdPetInstaller.cs — Remielle 桌宠 v2.1.0 安装器（WinForms，csc 编译，零依赖）
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
using System.Text.RegularExpressions;
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
            Text = "ClawdPet v2.1.0 安装器（Remielle × Trae）";
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
        // v2.1：按代码结构正则匹配，不依赖混淆变量名；electron screen 别名动态探测。
        //   gate  形象开关：仅 Trae ≤3.4.4 存在（3.4.7 官方已删，右键「切换外观」原生支持），
        //                    无命中属正常
        //   size  尺寸表：  X={ball:60,traebao:100|105} → 140（两代官方值都接受）
        //   drag  拖拽修复：X.on("icube-floating-chat:drag-move",(e,i,r)=>{ → 主进程权威光标坐标
        const string ReGateGate  = @"function (\w+)\((\w)\)\{return""ball""\}";
        const string ReGatePatch = "function $1($2){return $2===\"ball\"?\"ball\":\"traebao\"}";
        const string ReGateNorm  = @"function (\w+)\(\w\)\{return \w+===""ball""\?""ball"":""traebao""\}";
        const string ReGateOrig  = "function $1(t){return\"ball\"}";
        const string ReSizeGate  = @"(\w+)=\{ball:60,traebao:(?:100|105)\}";
        const string ReSizePatch = "$1={ball:60,traebao:140}";
        const string ReSizeNorm  = @"(\w+)=\{ball:60,traebao:(?:140|180)\}";   // 含 v2.0 早期 180px 版
        const string ReSizeOrig  = "$1={ball:60,traebao:105}";
        const string ReDragGate  = @"(\w+)\.on\(""icube-floating-chat:drag-move"",\((\w+),(\w+),(\w+)\)=>\{";
        const string ReDragNorm  = @"\.on\(""icube-floating-chat:drag-move"",\((\w+),(\w+),(\w+)\)=>\{try\{const __cp=\w+\.getCursorScreenPoint\(\);\2=__cp\.x;\3=__cp\.y\}catch\(__e\)\{\}";
        const string ReDragOrig  = ".on(\"icube-floating-chat:drag-move\",($1,$2,$3)=>{";
        // 已打补丁的判定（IsV2Installed / 备份跳过共用）
        const string ReSizeDone  = "{ball:60,traebao:140}";
        // HTML：禁用官方 3D 渲染（3.4.7 空闲态被 three.js 接管，禁用后回落 img 素材模式）
        const string RendererGate  = "script.src = './traebao/renderer.js';";
        const string RendererPatch = "script.src = './traebao/renderer.clawdpet-disabled.js';";
        // HTML：尺寸 CSS（3.4.7 官方 105/85 → 140/113，保持比例）
        const string ReCssBtnGate  = @"(\.icon-traebao \.btn \{\s*width: )105px(;\s*height: )105px(;)";
        const string ReCssBtnPatch = "${1}140px${2}140px${3}";
        const string ReCssBtnNorm  = @"(\.icon-traebao \.btn \{\s*width: )(?:140|113|100|180)px(;\s*height: )(?:140|113|100|180)px(;)";
        const string ReCssBtnOrig  = "${1}105px${2}105px${3}";
        const string ReCssImgGate  = @"(\.btn img \{[^}]*?width: )85px(;\s*height: )85px(;)";
        const string ReCssImgPatch = "${1}113px${2}113px${3}";
        const string ReCssImgNorm  = @"(\.btn img \{[^}]*?width: )(?:113|140|80|180)px(;\s*height: )(?:113|140|80|180)px(;)";
        const string ReCssImgOrig  = "${1}85px${2}85px${3}";
        const string ReCssContentGate  = @"(\.icon-traebao \.btnContent \{[^}]*?width: )85px(;\s*height: )85px(;)";
        const string ReCssContentPatch = "${1}113px${2}113px${3}";
        const string ReCssContentNorm  = @"(\.icon-traebao \.btnContent \{[^}]*?width: )(?:113|140|80|180)px(;\s*height: )(?:113|140|80|180)px(;)";
        const string ReCssContentOrig  = "${1}85px${2}85px${3}";
        const string PollStart = "<!--CLAWD-PET-POLL-START-->";
        const string PollEnd = "<!--CLAWD-PET-POLL-END-->";

        // 官方位素材（3.4.7 文件名）+ hooks 空闲动画 + 六态扩展
        static readonly string[] SkinFiles = { "default.png", "busy.webp", "super-busy.webp", "hover-drag.webp", "voice.webp", "default.webp" };
        static readonly string[] ExtraFiles = { "thinking.webp", "editing.webp", "notification.webp", "error.webp" };
        // 官方原始素材（卸载时无备份也可还原；3.4.7 五件套）
        static readonly string[] OfficialFiles = { "default.png", "busy.webp", "super-busy.webp", "hover-drag.webp", "voice.webp" };

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
                return Regex.IsMatch(main, ReGateNorm) || main.Contains(ReSizeDone);
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
            req.UserAgent = "ClawdPetInstaller/2.1.0";
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
            bool already = Regex.IsMatch(mainText, ReGateNorm) || mainText.Contains(ReSizeDone);
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

            // main.js：形象/尺寸/拖拽补丁（先归一化旧版补丁再打，幂等；结构正则，任意版本适用）。
            mainText = NormalizeMain(mainText);
            mainText = PatchMain(mainText);
            File.WriteAllText(t.MainJs, mainText);
            // 命中报告：gate 仅 ≤3.4.4 存在；size/drag 失配时明确告警
            var gateDone = Regex.Match(mainText, ReGateNorm);
            bool sizeDone = mainText.Contains(ReSizeDone);
            bool dragFixed = mainText.Contains("__cp=");
            if (gateDone.Success)
                log("[OK] main.js 补丁：形象 gate（" + gateDone.Groups[1].Value + "）解锁 + 窗口 140px" + (dragFixed ? " + 拖拽高 DPI 修复" : ""));
            else
                log("[OK] main.js：Trae 3.4.7+ 官方已原生解锁 trae宝（右键「切换外观」）");
            if (!sizeDone)
                log("[警告] main.js 尺寸锚点未命中（Trae 结构变化？）——窗口可能保持官方 105px，请附带 Trae 版本号反馈");
            if (!dragFixed)
                log("[警告] main.js 拖拽锚点未命中（Trae 结构变化？）——非 16:9 屏拖拽可能错位");

            // floatingButton.html：禁用官方 3D（回落素材模式）+ CSS 尺寸（两种模式都改）
            if (File.Exists(t.ButtonHtml))
            {
                string html = File.ReadAllText(t.ButtonHtml);
                html = NormalizeHtml(html, false);
                html = html.Replace(RendererGate, RendererPatch);
                html = Regex.Replace(html, ReCssBtnGate, ReCssBtnPatch);
                html = Regex.Replace(html, ReCssImgGate, ReCssImgPatch);
                html = Regex.Replace(html, ReCssContentGate, ReCssContentPatch);
                File.WriteAllText(t.ButtonHtml, html);
                bool btn140 = html.Contains("width: 140px");
                bool no3d = html.Contains(RendererPatch);
                if (btn140 && no3d)
                    log("[OK] floatingButton.html：140px 尺寸 + 官方 3D 已禁用（回落素材模式）");
                else if (btn140)
                    log("[警告] 140px 已生效，但未找到官方 3D 渲染引用（Trae ≤3.4.4 无此机制，正常）");
                else
                    log("[警告] floatingButton.html 尺寸锚点未命中（Trae 结构变化？）——立绘可能保持官方 105px");
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
            // 1) 素材还原：优先本地备份，缺失时用嵌入官方素材（3.4.7 五件套）
            string backup = Path.Combine(RuntimeDir(), "official-backup");
            foreach (string f in OfficialFiles)
            {
                string src = Path.Combine(backup, f);
                if (!File.Exists(src)) ExtractResource("official_" + f, src);
                File.Copy(src, Path.Combine(t.PetDir, f), true);
            }
            foreach (string f in ExtraFiles)
                try { File.Delete(Path.Combine(t.PetDir, f)); } catch { }
            // default.webp 是我们加的 hooks 空闲动画（3.4.7 官方无此文件）；旧版官方备份里有则一并还原
            string defW = Path.Combine(backup, "default.webp");
            if (File.Exists(defW))
                File.Copy(defW, Path.Combine(t.PetDir, "default.webp"), true);
            else
                try { File.Delete(Path.Combine(t.PetDir, "default.webp")); } catch { }
            log("[OK] 官方素材已还原（3.4.7 五件套），六态素材已清理");

            // 2) main.js 归一化
            string main = File.ReadAllText(t.MainJs);
            main = NormalizeMain(main);
            File.WriteAllText(t.MainJs, main);
            log("[OK] main.js 已还原官方（形象/尺寸/拖拽补丁回滚）");

            // 3) html 归一化（3D 渲染恢复 + CSS + 轮询脚本移除）
            if (File.Exists(t.ButtonHtml))
            {
                string html = File.ReadAllText(t.ButtonHtml);
                html = NormalizeHtml(html, true);
                File.WriteAllText(t.ButtonHtml, html);
                log("[OK] floatingButton.html 已还原官方（3D 渲染/尺寸/轮询回滚）");
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

        // ---------- 补丁归一化与打补丁（部署/卸载共用，幂等，任意版本混淆名自适应） ----------
        static string NormalizeMain(string s)
        {
            s = Regex.Replace(s, ReGateNorm, ReGateOrig);
            s = Regex.Replace(s, ReSizeNorm, ReSizeOrig);
            s = Regex.Replace(s, ReDragNorm, ReDragOrig);
            return s;
        }

        // drag-move 附近混淆的 electron screen 别名各版本不同（em/lm/...），
        // 取离 handler 最近的 getCursorScreenPoint 引用名，随版本自适应。
        static string FindScreenAlias(string s, int handlerIndex)
        {
            string best = null; int bestDist = int.MaxValue;
            foreach (Match m in Regex.Matches(s, @"(\w+)\.getCursorScreenPoint\("))
            {
                int d = Math.Abs(m.Index - handlerIndex);
                if (d < bestDist) { bestDist = d; best = m.Groups[1].Value; }
            }
            return best;
        }

        static string PatchMain(string s)
        {
            s = Regex.Replace(s, ReGateGate, ReGatePatch);
            s = Regex.Replace(s, ReSizeGate, ReSizePatch);
            Match h = Regex.Match(s, ReDragGate);
            if (h.Success)
            {
                string alias = FindScreenAlias(s, h.Index);
                if (alias != null)
                {
                    string patched = h.Groups[1].Value + ".on(\"icube-floating-chat:drag-move\",("
                        + h.Groups[2].Value + "," + h.Groups[3].Value + "," + h.Groups[4].Value + ")=>{try{const __cp="
                        + alias + ".getCursorScreenPoint();" + h.Groups[3].Value + "=__cp.x;"
                        + h.Groups[4].Value + "=__cp.y}catch(__e){}";
                    s = s.Replace(h.Value, patched);
                }
            }
            return s;
        }

        static string NormalizeHtml(string s, bool removePoll)
        {
            s = s.Replace(RendererPatch, RendererGate);
            s = Regex.Replace(s, ReCssBtnNorm, ReCssBtnOrig);
            s = Regex.Replace(s, ReCssImgNorm, ReCssImgOrig);
            s = Regex.Replace(s, ReCssContentNorm, ReCssContentOrig);
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
