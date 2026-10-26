// RemielleQoderInstaller.cs — Remielle Qoder 桌宠安装器（WinForms，csc 编译，零依赖）
//
// 只做一件事：把内嵌的 Petdex v2 宠物包写入
//   %USERPROFILE%\.petdex\pets\remielle\{pet.json, spritesheet.webp}
// 不修改 Qoder 程序、设置或缓存，无需管理员权限。
// 安装前若发现同名宠物目录，会先备份到 %LOCALAPPDATA%\RemielleQoderPet\backups。
// 卸载只删除上述 remielle 目录，并校验 pet.json 的 id，避免误删其他宠物。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace RemielleQoderInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ============================ UI ============================
    class MainForm : Form
    {
        const string AppVersion = "1.0.0";

        readonly Button btnInstall = new Button();
        readonly Button btnUninstall = new Button();
        readonly Button btnOpen = new Button();
        readonly Button btnRefresh = new Button();
        readonly TextBox txtLog = new TextBox();
        readonly Label lblStatus = new Label();
        readonly PictureBox picPreview = new PictureBox();

        public MainForm()
        {
            Text = "Remielle Qoder 桌宠安装器 v" + AppVersion;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 536);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(255, 249, 252);

            var accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 5;
            accent.BackColor = Color.FromArgb(244, 114, 182);
            Controls.Add(accent);

            picPreview.Location = new Point(20, 20);
            picPreview.Size = new Size(176, 176);
            picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            picPreview.BackColor = Color.FromArgb(255, 247, 251);
            picPreview.Image = LoadPreview();
            Controls.Add(picPreview);

            var title = new Label();
            title.Text = "蕾米埃尔 · Qoder 原生桌宠";
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(72, 41, 56);
            title.AutoSize = true;
            title.Location = new Point(212, 24);
            Controls.Add(title);

            var desc = new Label();
            desc.Text = "把 Remielle 的 6 套动画装进 Qoder 原生桌宠。\r\n不修改 Qoder 程序、配置或缓存，随时可卸载。";
            desc.ForeColor = Color.FromArgb(110, 82, 96);
            desc.AutoSize = true;
            desc.MaximumSize = new Size(430, 0);
            desc.Location = new Point(212, 60);
            Controls.Add(desc);

            var features = new Label();
            features.Text = "• 运行 / 等待 / 审查 / 失败 / 启动问候\r\n• 悬停跳跃 · 拖拽方向 · 视线跟随";
            features.ForeColor = Color.FromArgb(110, 82, 96);
            features.AutoSize = false;
            features.Size = new Size(424, 44);
            features.Location = new Point(212, 104);
            Controls.Add(features);

            var credit = new Label();
            credit.Text = "角色素材：刘畅 · MIT    |    Petdex v2    |    v" + AppVersion;
            credit.ForeColor = Color.FromArgb(156, 126, 140);
            credit.AutoSize = false;
            credit.Size = new Size(424, 22);
            credit.Location = new Point(212, 152);
            Controls.Add(credit);

            lblStatus.AutoSize = false;
            lblStatus.Size = new Size(620, 30);
            lblStatus.Location = new Point(20, 208);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(8, 0, 8, 0);
            lblStatus.BorderStyle = BorderStyle.FixedSingle;
            lblStatus.BackColor = Color.White;
            lblStatus.Text = "正在检测 ...";
            Controls.Add(lblStatus);

            btnInstall.Text = "安装 / 更新";
            btnInstall.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnInstall.ForeColor = Color.White;
            btnInstall.BackColor = Color.FromArgb(236, 72, 153);
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Size = new Size(190, 40);
            btnInstall.Location = new Point(20, 250);
            btnInstall.Click += (s, e) => RunAsync(Install);
            Controls.Add(btnInstall);

            btnUninstall.Text = "卸载";
            btnUninstall.Size = new Size(150, 40);
            btnUninstall.Location = new Point(220, 250);
            btnUninstall.Click += (s, e) => RunAsync(Uninstall);
            Controls.Add(btnUninstall);

            btnOpen.Text = "打开宠物目录";
            btnOpen.Size = new Size(150, 40);
            btnOpen.Location = new Point(380, 250);
            btnOpen.Click += (s, e) => OpenPetFolder();
            Controls.Add(btnOpen);

            btnRefresh.Text = "重新检测";
            btnRefresh.Size = new Size(100, 40);
            btnRefresh.Location = new Point(540, 250);
            btnRefresh.Click += (s, e) => RefreshStatus();
            Controls.Add(btnRefresh);

            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.WordWrap = true;
            txtLog.Font = new Font("Consolas", 8.5F);
            txtLog.BackColor = Color.White;
            txtLog.Size = new Size(620, 162);
            txtLog.Location = new Point(20, 302);
            Controls.Add(txtLog);

            var hint = new Label();
            hint.Text = "安装后在 Qoder 中：设置 → 桌面宠物 → 刷新 → 选择「蕾米埃尔」";
            hint.AutoSize = false;
            hint.Size = new Size(620, 40);
            hint.Location = new Point(20, 476);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Padding = new Padding(10, 0, 10, 0);
            hint.BackColor = Color.FromArgb(255, 240, 247);
            hint.ForeColor = Color.FromArgb(120, 55, 83);
            Controls.Add(hint);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Load += (s, e) =>
            {
                Log("Remielle Qoder 桌宠安装器 v" + AppVersion);
                Log("宠物目录：" + Installer.TargetDir());
                string qoder = Installer.FindQoderExe();
                Log(qoder != null ? "Qoder：" + qoder : "未检测到 Qoder 可执行文件（仍可安装宠物包）");
                RefreshStatus();
            };
        }

        static Image LoadPreview()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui_preview.png"))
                {
                    if (s == null) return null;
                    using (Image img = Image.FromStream(s))
                        return new Bitmap(img);
                }
            }
            catch { return null; }
        }

        void RunAsync(Action action)
        {
            btnInstall.Enabled = btnUninstall.Enabled = btnOpen.Enabled = btnRefresh.Enabled = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { action(); }
                catch (Exception ex)
                {
                    Log("[错误] " + ex.Message);
                    ShowMessage("操作失败：\r\n" + ex.Message, "Remielle Qoder 桌宠", MessageBoxIcon.Error);
                }
                finally
                {
                    BeginInvoke((Action)(() =>
                    {
                        btnInstall.Enabled = btnUninstall.Enabled = btnOpen.Enabled = btnRefresh.Enabled = true;
                        RefreshStatus();
                    }));
                }
            });
        }

        public void Log(string line)
        {
            string text = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
            Installer.AppendLog(text);
            if (IsHandleCreated)
                BeginInvoke((Action)(() => txtLog.AppendText(text + "\r\n")));
        }

        void SetStatus(string text)
        {
            if (IsHandleCreated)
                BeginInvoke((Action)(() => lblStatus.Text = text));
        }

        DialogResult Confirm(string text, string caption)
        {
            DialogResult r = DialogResult.No;
            Invoke((Action)(() =>
            {
                r = MessageBox.Show(this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            }));
            return r;
        }

        void ShowMessage(string text, string caption, MessageBoxIcon icon)
        {
            Invoke((Action)(() =>
            {
                MessageBox.Show(this, text, caption, MessageBoxButtons.OK, icon);
            }));
        }

        void RefreshStatus()
        {
            string target = Installer.TargetDir();
            string status;
            if (Installer.IsOurPet(target))
            {
                long size = Installer.SpritesheetSize(target);
                status = "已安装：" + target + "（" + (size / 1024 / 1024.0).ToString("0.0") + " MB）";
            }
            else if (Directory.Exists(target))
            {
                status = "检测到同名目录（非本安装器包）：" + target;
            }
            else
            {
                status = "未安装：" + target;
            }
            SetStatus(status);
        }

        void Install()
        {
            string target = Installer.TargetDir();
            SetStatus("正在安装 ...");

            if (Installer.IsOurPet(target))
            {
                Log("检测到已安装的 Remielle，准备覆盖更新。");
            }
            else if (Directory.Exists(target))
            {
                Log("[提示] 目标目录已存在但不是本安装器的宠物包，将先备份再安装。");
            }

            string backup = Installer.Install(Log);
            Log("—— 安装完成 ——");
            if (!string.IsNullOrEmpty(backup))
                Log("旧目录备份：" + backup);
            Log("下一步：Qoder → 设置 → 桌面宠物 → 刷新 → 选择「蕾米埃尔」。");

            BeginInvoke((Action)(() => new GuideForm(backup).ShowDialog(this)));
        }

        void Uninstall()
        {
            string target = Installer.TargetDir();
            if (!Directory.Exists(target))
            {
                Log("未安装，无需卸载。");
                return;
            }
            if (!Installer.IsOurPet(target))
            {
                ShowMessage(
                    "该目录不是本安装器安装的宠物包：\r\n" + target +
                    "\r\n\r\n为避免误删其他宠物，已取消卸载。请手动检查该目录。",
                    "无法卸载", MessageBoxIcon.Warning);
                Log("[跳过] 目标目录不是本安装器的宠物包，未删除。");
                return;
            }

            DialogResult r = Confirm(
                "确定卸载蕾米埃尔 Qoder 桌宠吗？\r\n\r\n将删除：\r\n" + target +
                "\r\n\r\n不会修改 Qoder 配置或缓存；备份目录也会保留。",
                "卸载 Remielle");
            if (r != DialogResult.Yes) { Log("已取消卸载。"); return; }

            SetStatus("正在卸载 ...");
            Installer.Uninstall(Log);
            Log("—— 卸载完成 ——");
            Log("如 Qoder 仍显示该宠物，请到桌面宠物设置中刷新或切换其他宠物。");
        }

        void OpenPetFolder()
        {
            string target = Installer.TargetDir();
            string dir = Directory.Exists(target) ? target : Installer.PetRoot();
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\""));
            }
            catch (Exception ex)
            {
                ShowMessage("无法打开目录：\r\n" + ex.Message, "打开目录", MessageBoxIcon.Error);
            }
        }
    }

    // 安装完成后的 Qoder 设置指引
    class GuideForm : Form
    {
        public GuideForm(string backup)
        {
            Text = "安装完成 — 在 Qoder 中启用";
            Font = new Font("Microsoft YaHei UI", 9.5F);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(590, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var rtb = new RichTextBox();
            rtb.ReadOnly = true;
            rtb.BorderStyle = BorderStyle.None;
            rtb.BackColor = Color.White;
            rtb.Font = new Font("Microsoft YaHei UI", 9.5F);
            rtb.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtb.WordWrap = true;
            rtb.Size = new Size(550, 360);
            rtb.Location = new Point(20, 18);

            string s =
                "安装成功！\r\n\r\n" +
                "在 Qoder 中启用：\r\n" +
                "  1. 打开 Qoder（无需重启，安装包不修改 Qoder 配置）。\r\n" +
                "  2. 进入 设置 → 桌面宠物（Desktop Pet）。\r\n" +
                "  3. 点击「刷新」，在宠物列表中选择「蕾米埃尔」。\r\n" +
                "  4. 若桌宠总开关未开启，请先打开。\r\n\r\n" +
                "动作对应：\r\n" +
                "  Agent 运行 / 编辑  → editing（忙碌）\r\n" +
                "  等待你的确认      → notification（等待）\r\n" +
                "  代码审查          → thinking（思考）\r\n" +
                "  任务失败          → error（懊恼）\r\n" +
                "  启动问候 / 悬停   → notification / attention\r\n" +
                "  拖拽左右 / 视线跟  → attention / idle 左右看\r\n\r\n" +
                "卸载：重新打开本安装器 → 卸载。只删除\r\n" +
                "~/.petdex/pets/remielle，不修改 Qoder 配置。\r\n";
            if (!string.IsNullOrEmpty(backup))
                s += "\r\n旧宠物目录已备份到：\r\n" + backup + "\r\n";
            rtb.Text = s;

            var panel = new Panel();
            panel.BackColor = Color.White;
            panel.Size = new Size(590, 390);
            panel.Location = new Point(0, 0);
            panel.Controls.Add(rtb);
            Controls.Add(panel);

            var btnFolder = new Button();
            btnFolder.Text = "打开宠物目录";
            btnFolder.Size = new Size(150, 34);
            btnFolder.Location = new Point(20, 414);
            btnFolder.Click += (s2, e2) =>
            {
                try
                {
                    Directory.CreateDirectory(Installer.TargetDir());
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Installer.TargetDir() + "\""));
                }
                catch { }
            };
            Controls.Add(btnFolder);

            var ok = new Button();
            ok.Text = "完成";
            ok.DialogResult = DialogResult.OK;
            ok.Size = new Size(110, 34);
            ok.Location = new Point(460, 414);
            Controls.Add(ok);
            AcceptButton = ok;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
        }
    }

    // ============================ 安装引擎 ============================
    static class Installer
    {
        public const string PetId = "remielle";
        const string PetJsonResource = "pet_pet.json";
        const string SheetResource = "pet_spritesheet.webp";

        public static string PetRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".petdex", "pets");
        }

        public static string TargetDir()
        {
            return Path.Combine(PetRoot(), PetId);
        }

        static string DataDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemielleQoderPet");
        }

        static string BackupRoot()
        {
            return Path.Combine(DataDir(), "backups");
        }

        public static void AppendLog(string line)
        {
            try
            {
                Directory.CreateDirectory(DataDir());
                File.AppendAllText(
                    Path.Combine(DataDir(), "install.log"),
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { }
        }

        public static string FindQoderExe()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Qoder CN", "Qoder CN.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Qoder CN", "Qoder CN.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Qoder", "Qoder.exe"),
            };
            foreach (string p in candidates)
                if (File.Exists(p)) return p;
            return null;
        }

        public static bool IsOurPet(string dir)
        {
            string pet = Path.Combine(dir, "pet.json");
            if (!File.Exists(pet)) return false;
            try
            {
                string text = File.ReadAllText(pet, Encoding.UTF8);
                return Regex.IsMatch(text, "\"id\"\\s*:\\s*\"" + PetId + "\"");
            }
            catch { return false; }
        }

        public static long SpritesheetSize(string dir)
        {
            try
            {
                string p = Path.Combine(dir, "spritesheet.webp");
                return File.Exists(p) ? new FileInfo(p).Length : 0;
            }
            catch { return 0; }
        }

        public static string Install(Action<string> log)
        {
            string target = TargetDir();
            string staging = Path.Combine(Path.GetTempPath(), "RemielleQoderPet-" + Guid.NewGuid().ToString("N"));
            string backup = null;
            try
            {
                Directory.CreateDirectory(staging);
                string petTmp = Path.Combine(staging, "pet.json");
                string sheetTmp = Path.Combine(staging, "spritesheet.webp");
                ExtractResource(PetJsonResource, petTmp);
                ExtractResource(SheetResource, sheetTmp);
                ValidatePackage(petTmp, sheetTmp);
                log("已校验内嵌宠物包（pet.json + spritesheet.webp）。");

                Directory.CreateDirectory(PetRoot());

                bool exists = Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length > 0;
                bool same = exists && IsSamePackage(target, petTmp, sheetTmp);

                if (exists && !same)
                {
                    backup = MakeBackup(target, log);
                }
                else if (same)
                {
                    log("[跳过] 目标目录已经是相同版本，不创建备份。");
                }

                Directory.CreateDirectory(target);
                File.Copy(petTmp, Path.Combine(target, "pet.json"), true);
                File.Copy(sheetTmp, Path.Combine(target, "spritesheet.webp"), true);

                string finalSheet = Path.Combine(target, "spritesheet.webp");
                if (!File.Exists(finalSheet) || new FileInfo(finalSheet).Length < 100 * 1024)
                    throw new IOException("写入后的 spritesheet.webp 异常，请重试。");

                log("[OK] 已安装 " + target);
                return backup;
            }
            catch
            {
                if (backup != null && Directory.Exists(backup))
                {
                    try
                    {
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        CopyDirectory(backup, target);
                        log("[回滚] 安装失败，已恢复安装前目录。");
                    }
                    catch (Exception rex)
                    {
                        log("[警告] 自动回滚失败：" + rex.Message + "（备份保留在 " + backup + "）");
                    }
                }
                throw;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch { }
            }
        }

        public static void Uninstall(Action<string> log)
        {
            string target = TargetDir();
            if (!Directory.Exists(target))
            {
                log("[跳过] 未找到 " + target);
                return;
            }
            if (!IsOurPet(target))
                throw new InvalidOperationException("目标目录不是本安装器的宠物包，已取消删除。");

            Directory.Delete(target, true);
            log("[OK] 已删除 " + target);
            log("备份目录未被删除：" + BackupRoot());
        }

        static void ValidatePackage(string petJson, string sheet)
        {
            string text = File.ReadAllText(petJson, Encoding.UTF8);
            if (!Regex.IsMatch(text, "\"id\"\\s*:\\s*\"" + PetId + "\""))
                throw new InvalidDataException("内嵌 pet.json 的 id 不是 " + PetId);
            if (!text.Contains("\"spriteVersionNumber\""))
                throw new InvalidDataException("内嵌 pet.json 缺少 spriteVersionNumber");

            long size = new FileInfo(sheet).Length;
            if (size < 100 * 1024 || size > 32L * 1024 * 1024)
                throw new InvalidDataException("内嵌 spritesheet.webp 大小异常：" + size + " 字节");

            byte[] head = new byte[12];
            using (FileStream f = File.OpenRead(sheet))
            {
                if (f.Read(head, 0, head.Length) != head.Length)
                    throw new InvalidDataException("内嵌 spritesheet.webp 不完整");
            }
            string riff = Encoding.ASCII.GetString(head, 0, 4);
            string webp = Encoding.ASCII.GetString(head, 8, 4);
            if (riff != "RIFF" || webp != "WEBP")
                throw new InvalidDataException("内嵌 spritesheet.webp 不是有效的 WebP 文件");
        }

        static string MakeBackup(string target, Action<string> log)
        {
            Directory.CreateDirectory(BackupRoot());
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest = Path.Combine(BackupRoot(), "remielle-" + stamp);
            int n = 2;
            while (Directory.Exists(dest))
                dest = Path.Combine(BackupRoot(), "remielle-" + stamp + "-" + (n++));
            CopyDirectory(target, dest);
            log("[备份] 已保存安装前目录：" + dest);
            return dest;
        }

        static bool IsSamePackage(string dir, string petTmp, string sheetTmp)
        {
            try
            {
                string pet = Path.Combine(dir, "pet.json");
                string sheet = Path.Combine(dir, "spritesheet.webp");
                if (!File.Exists(pet) || !File.Exists(sheet)) return false;
                return Sha256(pet) == Sha256(petTmp) && Sha256(sheet) == Sha256(sheetTmp);
            }
            catch { return false; }
        }

        static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f));
        }

        static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        static void ExtractResource(string name, string dest)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("缺少内嵌资源：" + name);
                using (FileStream f = File.Create(dest))
                    s.CopyTo(f);
            }
        }
    }
}
