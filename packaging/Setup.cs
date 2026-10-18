using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class SetupForm : Form
{
    private readonly Button install = new Button(), cancel = new Button();
    private readonly CheckBox desktop = new CheckBox(), launch = new CheckBox();
    private readonly Label state = new Label();
    private readonly ProgressBar progress = new ProgressBar();
    private bool installing;
    public SetupForm()
    {
        Text = "安装 " + InstallCommon.Product;
        ClientSize = new Size(540, 385); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(22, 32, 41); ForeColor = Color.FromArgb(237, 247, 250);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(new Label { Text = InstallCommon.Product, Location = new Point(30, 27), Size = new Size(460, 40), Font = new Font(Font.FontFamily, 20, FontStyle.Bold) });
        Controls.Add(new Label { Text = "安装后自动读取这台电脑上的 Codex 登录账号。\n未登录时显示提示；登录后可以重新检测。", Location = new Point(32, 85), Size = new Size(480, 58), ForeColor = Color.FromArgb(174, 194, 205) });
        Controls.Add(new Label { Text = "安装位置", Location = new Point(32, 158), Size = new Size(450, 24) });
        Controls.Add(new TextBox { Text = InstallCommon.Target, ReadOnly = true, Location = new Point(32, 187), Size = new Size(476, 28), BackColor = Color.FromArgb(34, 47, 58), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle });
        desktop.Text = "创建桌面快捷方式"; desktop.Checked = true; desktop.SetBounds(32, 231, 250, 26); Controls.Add(desktop);
        launch.Text = "安装后启动挂件"; launch.Checked = true; launch.SetBounds(290, 231, 215, 26); Controls.Add(launch);
        state.Text = "运行组件已包含，无需另装 Node.js 或 Codex CLI。"; state.SetBounds(32, 273, 476, 26); state.ForeColor = Color.FromArgb(174, 194, 205); state.Font = new Font(Font.FontFamily, 9); Controls.Add(state);
        progress.SetBounds(32, 307, 476, 5); progress.Style = ProgressBarStyle.Marquee; progress.Visible = false; Controls.Add(progress);
        install.Text = "安装"; install.SetBounds(300, 331, 100, 35); install.BackColor = Color.FromArgb(88, 218, 200); install.ForeColor = Color.FromArgb(18, 43, 48); install.FlatStyle = FlatStyle.Flat; Controls.Add(install);
        cancel.Text = "取消"; cancel.SetBounds(410, 331, 98, 35); cancel.FlatStyle = FlatStyle.Flat; Controls.Add(cancel);
        cancel.Click += (s, e) => Close(); install.Click += Install;
        FormClosing += (s, e) => { if (installing) e.Cancel = true; };
    }
    private async void Install(object sender, EventArgs args)
    {
        installing = true; install.Enabled = cancel.Enabled = desktop.Enabled = launch.Enabled = false;
        progress.Visible = true; state.Text = "正在安装，请稍候…";
        bool createDesktop = desktop.Checked;
        try {
            await Task.Run(() => InstallPayload(createDesktop));
            installing = false;
            if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(InstallCommon.Target, InstallCommon.Executable)) { WorkingDirectory = InstallCommon.Target, UseShellExecute = true });
            MessageBox.Show(this, "安装完成。挂件会读取本机当前 Codex 登录账号。", InstallCommon.Product, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        } catch (Exception ex) {
            installing = false; progress.Visible = false; install.Enabled = cancel.Enabled = desktop.Enabled = launch.Enabled = true;
            state.Text = "安装未完成，可以重试。";
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    internal static void Extract(string destination)
    {
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        HashSet<string> expected = new HashSet<string>(InstallCommon.Files, StringComparer.OrdinalIgnoreCase);
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WidgetPayload.zip"))
        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read)) {
            foreach (ZipArchiveEntry entry in archive.Entries) {
                string name = entry.FullName.Replace('\\', '/');
                if (!expected.Remove(name)) throw new IOException("安装包包含非预期文件。");
                string path = Path.GetFullPath(Path.Combine(root, name));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装包路径校验失败。");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (Stream input = entry.Open()) using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write)) input.CopyTo(output);
            }
        }
        if (expected.Count != 0) throw new IOException("安装包文件不完整。");
    }
    private static void InstallPayload(bool desktop)
    {
        InstallCommon.CheckTarget(InstallCommon.Target);
        if (Directory.Exists(InstallCommon.Target) && !File.Exists(Path.Combine(InstallCommon.Target, "install.id")) && Directory.GetFileSystemEntries(InstallCommon.Target).Length > 0)
            throw new IOException("安装目录已有其他文件，请先选择其他位置保存这些文件。");
        string staging = Path.Combine(Path.GetTempPath(), "CodexWidgetInstall-" + Guid.NewGuid().ToString("N"));
        try {
            Extract(staging); InstallCommon.StopWidget();
            Directory.CreateDirectory(InstallCommon.Target);
            foreach (string name in InstallCommon.Files) {
                string path = Path.Combine(InstallCommon.Target, name);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("目标文件不能是文件链接。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.Copy(Path.Combine(staging, name), path, true);
            }
            InstallCommon.Shortcut(Path.Combine(InstallCommon.MenuFolder, InstallCommon.Product + ".lnk"), Path.Combine(InstallCommon.Target, InstallCommon.Executable));
            InstallCommon.Shortcut(Path.Combine(InstallCommon.MenuFolder, "卸载.lnk"), Path.Combine(InstallCommon.Target, "Uninstall.exe"));
            if (desktop) InstallCommon.Shortcut(InstallCommon.DesktopLink, Path.Combine(InstallCommon.Target, InstallCommon.Executable));
            InstallCommon.Register();
        } finally {
            // Only remove files extracted from this embedded, allowlisted payload.
            foreach (string name in InstallCommon.Files) { string path = Path.Combine(staging, name); if (File.Exists(path)) File.Delete(path); }
            foreach (string folder in new[] { "runtime", "licenses", "" }) {
                string path = Path.Combine(staging, folder); if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path);
            }
        }
    }
    [STAThread] private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--extract-only") {
            try { Extract(args[1]); Environment.ExitCode = 0; } catch (Exception) { Environment.ExitCode = 1; }
            return;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Build < 22000) {
            MessageBox.Show("此安装包适用于 Windows 11 64 位系统。", InstallCommon.Product, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new SetupForm());
    }
}
