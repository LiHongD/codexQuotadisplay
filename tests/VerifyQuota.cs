using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class VerifyQuota
{
    private static void Assert(bool passed, string message) { if (!passed) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread] private static int Main(string[] args)
    {
        try {
            string output = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string helper = Path.Combine(output, "FakeBackend.exe");
            string isolated = Path.Combine(output, "empty-codex-home");
            Directory.CreateDirectory(isolated);
            File.WriteAllText(Path.Combine(isolated, "config.toml"), "cli_auth_credentials_store = \"file\"\n");
            CodexAccountReader reader = new CodexAccountReader(args[0]);
            reader.Diagnostic = Console.Error.WriteLine;
            string[] scenarios = { "none", "apikey", "empty", "other-bucket", "expired", "offline", "malformed", "valid", "second-account" };
            QuotaState[] expected = { QuotaState.NotSignedIn, QuotaState.NoQuota, QuotaState.NoQuota, QuotaState.NoQuota, QuotaState.SessionExpired, QuotaState.Unavailable, QuotaState.NoQuota, QuotaState.Connected, QuotaState.Connected };
            QuotaSnapshot first = null, second = null;
            for (int i = 0; i < scenarios.Length; i++) {
                Environment.SetEnvironmentVariable("QUOTA_TEST_CASE", scenarios[i]);
                QuotaSnapshot snapshot = reader.ReadWithBackend(helper, isolated);
                Assert(snapshot.State == expected[i], scenarios[i] + " -> " + snapshot.State);
                if (scenarios[i] == "valid") first = snapshot;
                if (scenarios[i] == "second-account") second = snapshot;
            }
            Environment.SetEnvironmentVariable("QUOTA_TEST_CASE", null);
            string path = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
            QuotaSnapshot unsigned;
            try { unsigned = reader.ReadWithBackend(Path.Combine(args[0], "runtime", "codex.exe"), isolated); }
            finally { Environment.SetEnvironmentVariable("PATH", path); }
            Assert(unsigned.State == QuotaState.NotSignedIn, "official bundled helper, empty login, no Node/CLI in PATH -> NotSignedIn");
            Assert(reader.ReadWithBackend(Path.Combine(output, "missing.exe"), isolated).State == QuotaState.Unavailable, "missing component -> Unavailable");

            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string data = Path.Combine(output, "widget-data"); Directory.CreateDirectory(data);
            using (CodexUsageWidgetV2 widget = new CodexUsageWidgetV2(args[0], data)) {
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                MethodInfo apply = typeof(CodexUsageWidgetV2).GetMethod("ApplySnapshot", flags);
                FieldInfo primary = typeof(CodexUsageWidgetV2).GetField("primary", flags);
                FieldInfo secondary = typeof(CodexUsageWidgetV2).GetField("secondary", flags);
                apply.Invoke(widget, new object[] { first });
                Assert(Convert.ToDouble(primary.GetValue(widget)) == 75, "valid account percentage = 100 - used");
                Assert(secondary.GetValue(widget) == null, "missing secondary remains unknown");
                apply.Invoke(widget, new object[] { second });
                Assert(Convert.ToDouble(primary.GetValue(widget)) == 40, "account change updates quota");
                apply.Invoke(widget, new object[] { unsigned });
                Assert(primary.GetValue(widget) == null && secondary.GetValue(widget) == null, "logout clears previous quota");
                Assert((DateTime)typeof(CodexUsageWidgetV2).GetField("lastUpdated", flags).GetValue(widget) == DateTime.MinValue, "logout clears previous update timestamp");
                MethodInfo create = typeof(CodexUsageWidgetV2).GetMethod("CreateFrame", flags);
                using (Bitmap image = (Bitmap)create.Invoke(widget, null)) image.Save(Path.Combine(output, "not-signed-in.png"));
                apply.Invoke(widget, new object[] { QuotaSnapshot.Empty(QuotaState.NoQuota, "找不到套餐额度", "已检测到本机登录账号\n服务未返回 Codex 套餐额度信息") });
                using (Bitmap image = (Bitmap)create.Invoke(widget, null)) image.Save(Path.Combine(output, "no-quota.png"));
            }
            Console.WriteLine("ALL QUOTA CHECKS PASSED"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Environment.SetEnvironmentVariable("QUOTA_TEST_CASE", null); }
    }
}
