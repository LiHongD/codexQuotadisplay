using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class InstallCommon
{
    public const string Product = "Codex 用量挂件", Version = "1.2.1", Marker = "CodexUsageWidget/1.0";
    public const string Executable = "CodexQuotaWidget.exe";
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexUsageWidget";
    public static readonly string[] Files = {
        "CodexQuotaWidget.exe", "CodexQuotaWidget.exe.config", "Uninstall.exe", "使用说明.txt", "install.id",
        "runtime/codex.exe", "licenses/CODEX-LICENSE.txt", "licenses/CODEX-NOTICE.txt", "licenses/THIRD-PARTY.txt"
    };
    public static string Target { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexUsageWidget"); } }
    public static string MenuFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product); } }
    public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product + ".lnk"); } }

    public static void CheckTarget(string directory)
    {
        string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (!String.Equals(full, Path.GetFullPath(Target), StringComparison.OrdinalIgnoreCase)) throw new IOException("安装路径校验失败。");
        string current = full;
        while (current != null) {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装路径不能是目录链接。");
            current = Path.GetDirectoryName(current);
        }
        foreach (string sub in new[] { "runtime", "licenses" }) {
            string path = Path.Combine(full, sub);
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装子目录不能是目录链接。");
        }
    }
    public static void StopWidget()
    {
        string executable = Path.Combine(Target, Executable);
        foreach (Process process in Process.GetProcessesByName("CodexQuotaWidget")) using (process) {
            try {
                if (!String.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(2500)) { process.Kill(); process.WaitForExit(2500); }
            } catch (InvalidOperationException) { }
        }
    }
    public static void Shortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType), shortcut = null;
        try {
            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type type = shortcut.GetType();
            type.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { target });
            type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Target });
            type.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(Target, Executable) + ",0" });
            type.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        } finally {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
    public static void Register()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
            key.SetValue("DisplayName", Product); key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Codex Usage Widget Community"); key.SetValue("InstallLocation", Target);
            key.SetValue("DisplayIcon", Path.Combine(Target, Executable));
            key.SetValue("UninstallString", "\"" + Path.Combine(Target, "Uninstall.exe") + "\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long bytes = 0; foreach (string file in Files) bytes += new FileInfo(Path.Combine(Target, file)).Length;
            key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        }
    }
}
