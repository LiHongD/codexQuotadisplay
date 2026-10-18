using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public enum QuotaState { Connected, NotSignedIn, NoQuota, SessionExpired, Unavailable }

public sealed class QuotaSnapshot
{
    public QuotaState State;
    public string Title, Detail, Plan;
    public Dictionary<string, object> Limits;
    public static QuotaSnapshot Empty(QuotaState state, string title, string detail)
    {
        return new QuotaSnapshot { State = state, Title = title, Detail = detail };
    }
}

// The official helper manages the current Windows user's Codex credentials.
// This client never opens auth.json, exports credentials, or sends inference requests.
public sealed class CodexAccountReader
{
    private readonly string appDirectory;
    internal Action<string> Diagnostic { get; set; }
    public CodexAccountReader(string directory) { appDirectory = Path.GetFullPath(directory); }

    private string FindBackend()
    {
        string bundled = Path.Combine(appDirectory, "runtime", "codex.exe");
        if (File.Exists(bundled)) return bundled;
        // Developer builds can use an existing desktop app or CLI; distribution always includes the helper.
        string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktop)) {
            DirectoryInfo[] versions = new DirectoryInfo(desktop).GetDirectories();
            Array.Sort(versions, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            foreach (DirectoryInfo version in versions) {
                string candidate = Path.Combine(version.FullName, "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            try { string candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        return null;
    }

    public QuotaSnapshot Read() { return ReadWithBackend(FindBackend(), null); }

    // Explicit parameters allow isolated protocol tests without changing or signing out the real account.
    public QuotaSnapshot ReadWithBackend(string backend, string isolatedHome)
    {
        if (String.IsNullOrEmpty(backend) || !File.Exists(backend))
            return QuotaSnapshot.Empty(QuotaState.Unavailable, "找不到运行组件", "请重新安装挂件，然后重新检测");
        JavaScriptSerializer json = new JavaScriptSerializer();
        ProcessStartInfo info = new ProcessStartInfo(backend, "app-server --listen stdio://");
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
        info.WorkingDirectory = appDirectory;
        using (Process process = new Process()) {
            process.StartInfo = info;
            try {
                if (isolatedHome != null) info.EnvironmentVariables["CODEX_HOME"] = Path.GetFullPath(isolatedHome);
                // Host-app transient session tokens must not override the device's persisted Codex account.
                foreach (string name in new[] { "CODEX_ACCESS_TOKEN", "CODEX_API_KEY", "OPENAI_API_KEY", "CODEX_AUTH_JSON" })
                    info.EnvironmentVariables.Remove(name);
                process.Start();
                process.ErrorDataReceived += (s, e) => { /* Drain stderr; do not persist account or credential details. */ };
                process.BeginErrorReadLine();
                Stopwatch deadline = Stopwatch.StartNew();
                Send(process, json, 1, "initialize", new { clientInfo = new { name = "codex-usage-widget", title = "Codex Usage Widget", version = "1.0.0" }, capabilities = new { } });
                ReadReply(process, json, 1, deadline);
                process.StandardInput.WriteLine(json.Serialize(new { method = "initialized" }));
                Send(process, json, 2, "account/read", new { refreshToken = false });
                Dictionary<string, object> identity = ReadReply(process, json, 2, deadline);
                Dictionary<string, object> account = Map(identity, "account");
                if (account == null)
                    return QuotaSnapshot.Empty(QuotaState.NotSignedIn, "未登录 Codex", "没有找到本机账号信息\n请先在 Codex 中登录，再重新检测");
                string type = Text(account, "type");
                if (String.Equals(type, "apiKey", StringComparison.OrdinalIgnoreCase) || type == "amazonBedrock")
                    return QuotaSnapshot.Empty(QuotaState.NoQuota, "找不到套餐额度", "检测到 API 或云服务凭据\n请在 Codex 中使用 ChatGPT 账号登录");
                Send(process, json, 3, "account/rateLimits/read", new { excludeResetCreditDetails = true });
                Dictionary<string, object> usage = ReadReply(process, json, 3, deadline);
                Dictionary<string, object> buckets = Map(usage, "rateLimitsByLimitId");
                Dictionary<string, object> limits = buckets != null && buckets.Count > 0 ? Map(buckets, "codex") : Map(usage, "rateLimits");
                string limitId = limits == null ? "" : Text(limits, "limitId");
                if (limits == null || (limitId != "" && limitId != "codex") ||
                    (!HasWindow(limits, "primary") && !HasWindow(limits, "secondary")))
                    return QuotaSnapshot.Empty(QuotaState.NoQuota, "找不到套餐额度", "已检测到本机登录账号\n服务未返回 Codex 套餐额度信息");
                string plan = Text(limits, "planType");
                if (plan == "") plan = Text(account, "planType");
                return new QuotaSnapshot { State = QuotaState.Connected, Title = "额度已更新", Plan = plan, Limits = limits };
            } catch (QuotaProtocolException ex) {
                return QuotaSnapshot.Empty(ex.State, ex.State == QuotaState.SessionExpired ? "Codex 登录已失效" : "暂时无法获取额度",
                    ex.State == QuotaState.SessionExpired ? "请在 Codex 中重新登录\n登录后点击重新检测" : "请检查网络或 Codex 配置\n稍后重新检测");
            } catch (TimeoutException) {
                return QuotaSnapshot.Empty(QuotaState.Unavailable, "读取额度超时", "请检查网络连接\n稍后重新检测");
            } catch (Exception ex) {
                if (Diagnostic != null) Diagnostic(ex.GetType().FullName + ": " + ex.Message + "\n" + ex.StackTrace);
                return QuotaSnapshot.Empty(QuotaState.Unavailable, "找不到额度信息", "本机 Codex 信息暂时无法读取\n请检查 Codex 登录状态后重试");
            } finally {
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } } catch (InvalidOperationException) { }
            }
        }
    }

    private static void Send(Process process, JavaScriptSerializer json, int id, string method, object parameters)
    {
        process.StandardInput.WriteLine(json.Serialize(new { id = id, method = method, @params = parameters }));
        process.StandardInput.Flush();
    }

    private static Dictionary<string, object> ReadReply(Process process, JavaScriptSerializer json, int id, Stopwatch deadline)
    {
        while (true) {
            int remaining = 18000 - (int)deadline.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException();
            Task<string> pending = process.StandardOutput.ReadLineAsync();
            if (!pending.Wait(remaining)) throw new TimeoutException();
            string line = pending.Result;
            if (line == null) throw new IOException("Backend closed the connection.");
            Dictionary<string, object> response;
            try { response = json.Deserialize<Dictionary<string, object>>(line); } catch (ArgumentException) { continue; }
            object responseId;
            if (response == null || !response.TryGetValue("id", out responseId) || Convert.ToString(responseId) != id.ToString()) continue;
            Dictionary<string, object> error = Map(response, "error");
            if (error != null) {
                string message = Text(error, "message").ToLowerInvariant(), code = Text(error, "code");
                bool expired = code == "401" || code == "-32001" || message.Contains("unauthorized") || message.Contains("refresh token")
                    || message.Contains("token expired") || message.Contains("not authenticated") || message.Contains("not logged in")
                    || message.Contains("sign in") || message.Contains("authentication required");
                throw new QuotaProtocolException(expired ? QuotaState.SessionExpired : QuotaState.Unavailable);
            }
            Dictionary<string, object> result = Map(response, "result");
            if (result == null) throw new IOException("Invalid backend response.");
            return result;
        }
    }

    public static Dictionary<string, object> Map(Dictionary<string, object> source, string key)
    {
        object value;
        return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
    }
    private static string Text(Dictionary<string, object> source, string key)
    {
        object value; return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
    }
    private static bool HasWindow(Dictionary<string, object> limits, string key)
    {
        Dictionary<string, object> window = Map(limits, key);
        double percent;
        return window != null && Double.TryParse(Text(window, "usedPercent"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out percent) && !Double.IsNaN(percent) && !Double.IsInfinity(percent);
    }
    private sealed class QuotaProtocolException : Exception
    {
        public readonly QuotaState State;
        public QuotaProtocolException(QuotaState state) { State = state; }
    }
}
