using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Uninstaller
{
    [STAThread] private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        try {
            if (args.Length == 2 && args[0] == "--remove") {
                int parent;
                if (!Int32.TryParse(args[1], out parent)) return;
                try { using (Process process = Process.GetProcessById(parent)) process.WaitForExit(10000); } catch (ArgumentException) { }
                Remove(); return;
            }
            if (MessageBox.Show("卸载 Codex 用量挂件？\n挂件设置和 Codex 登录信息会保留。", InstallCommon.Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            InstallCommon.CheckTarget(InstallCommon.Target);
            if (!String.Equals(Path.GetFullPath(Application.ExecutablePath), Path.Combine(InstallCommon.Target, "Uninstall.exe"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("请从已安装目录或系统应用列表运行卸载程序。");
            string helper = Path.Combine(Path.GetTempPath(), "CodexWidgetRemove-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, helper);
            Process.Start(new ProcessStartInfo(helper, "--remove " + Process.GetCurrentProcess().Id) { UseShellExecute = true });
        } catch (Exception ex) { MessageBox.Show(ex.Message, "卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
    }
    private static void Remove()
    {
        InstallCommon.CheckTarget(InstallCommon.Target);
        string marker = Path.Combine(InstallCommon.Target, "install.id");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != InstallCommon.Marker) throw new IOException("未找到有效的挂件安装信息。");
        InstallCommon.StopWidget();
        foreach (string name in InstallCommon.Files) {
            string path = Path.GetFullPath(Path.Combine(InstallCommon.Target, name));
            if (!path.StartsWith(InstallCommon.Target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("卸载路径校验失败。");
            if (File.Exists(path)) File.Delete(path);
        }
        foreach (string directory in new[] { "runtime", "licenses", "" }) {
            string path = Path.Combine(InstallCommon.Target, directory);
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path);
        }
        foreach (string shortcut in new[] { InstallCommon.DesktopLink, Path.Combine(InstallCommon.MenuFolder, InstallCommon.Product + ".lnk"), Path.Combine(InstallCommon.MenuFolder, "卸载.lnk") })
            if (File.Exists(shortcut)) File.Delete(shortcut);
        if (Directory.Exists(InstallCommon.MenuFolder) && Directory.GetFileSystemEntries(InstallCommon.MenuFolder).Length == 0) Directory.Delete(InstallCommon.MenuFolder);
        Registry.CurrentUser.DeleteSubKeyTree(InstallCommon.RegistryPath, false);
        MessageBox.Show("挂件已卸载。", InstallCommon.Product, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
