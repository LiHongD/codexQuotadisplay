using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal static class FakeBackend
{
    private static void Main()
    {
        JavaScriptSerializer json = new JavaScriptSerializer();
        string scenario = Environment.GetEnvironmentVariable("QUOTA_TEST_CASE");
        string line;
        while ((line = Console.ReadLine()) != null) {
            Dictionary<string, object> request = json.Deserialize<Dictionary<string, object>>(line);
            object id;
            if (!request.TryGetValue("id", out id)) continue;
            string method = Convert.ToString(request["method"]);
            object result;
            if (method == "initialize") result = new { userAgent = "quota-test" };
            else if (method == "account/read") {
                object account = scenario == "none" ? null : scenario == "apikey" ? (object)new { type = "apiKey" } : new { type = "chatgpt", planType = scenario == "second-account" ? "pro" : "plus" };
                result = new { account = account, requiresOpenaiAuth = true };
            } else {
                if (scenario == "expired" || scenario == "offline") {
                    Console.WriteLine(json.Serialize(new { id = id, error = new { code = scenario == "expired" ? 401 : -32000, message = scenario == "expired" ? "Unauthorized" : "Network offline" } }));
                    continue;
                }
                object window = new { usedPercent = scenario == "second-account" ? 60 : 25, windowDurationMins = 300, resetsAt = 2000000000 };
                if (scenario == "malformed") window = new { usedPercent = (object)null };
                object limits = scenario == "empty" ? null : new { limitId = "codex", primary = window, secondary = (object)null };
                object buckets = scenario == "other-bucket" ? new { codex_other = limits } : null;
                result = new { rateLimits = limits, rateLimitsByLimitId = buckets };
            }
            Console.WriteLine("{\"method\":\"account/updated\",\"params\":{}}");
            Console.WriteLine(json.Serialize(new { id = id, result = result }));
        }
    }
}
