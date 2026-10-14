using System; using System.IO; using System.Linq; using System.Text; using System.Diagnostics; using System.Collections.Generic;
namespace SessionProbe {
    public static class BrowserProfiles {
        public static string Browser {
            get {
                foreach (var p in new[] {
                    Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe") }) if (File.Exists(p)) return p;
                throw new InvalidOperationException("未找到 Chrome。请安装 Chrome，或选择内置登录窗口 / Edge。");
            }
        }
        public static bool Allowed(string text) {
            Uri u;
            if (text == null || text.Length > 32768 || text.Any(char.IsControl) ||
                !Uri.TryCreate(text, UriKind.Absolute, out u) || u.Scheme != "https" || !u.IsDefaultPort || !string.IsNullOrEmpty(u.UserInfo)) return false;
            if (u.Host.Equals("account.battlenet.com.cn", StringComparison.OrdinalIgnoreCase))
                return u.AbsolutePath == "/" || u.AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.StartsWith("/login/", StringComparison.OrdinalIgnoreCase);
            if (!u.Host.Equals("oauth.g.mkey.163.com", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath != "/oauth-front/" || u.Fragment.Length > 0) return false;
            // Both apph and app were observed in requests from the official CN launcher.
            // Never follow a supplied return address to an unrelated site.
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            try {
                foreach (var part in u.Query.TrimStart('?').Split('&')) {
                    var pair = part.Split(new[] { '=' }, 2);
                    string name = Uri.UnescapeDataString(pair[0].Replace("+", " "));
                    if (query.ContainsKey(name)) return false;
                    query.Add(name, pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : "");
                }
                string redirect, client, from; Uri r;
                return query.TryGetValue("client_id", out client) && client == "ld12" && query.TryGetValue("fromApp", out from) && (from == "apph" || from == "app") &&
                    query.TryGetValue("redirect_uri", out redirect) && Uri.TryCreate(redirect, UriKind.Absolute, out r) &&
                    r.Scheme == "https" && r.IsDefaultPort && r.UserInfo.Length == 0 && r.Host.Equals("account.battlenet.com.cn", StringComparison.OrdinalIgnoreCase) &&
                    r.AbsolutePath == "/login/signin/ntes" && r.Query.Length == 0 && r.Fragment.Length == 0;
            } catch { return false; }
        }
        public static string Quote(string value) {
            var b = new StringBuilder("\""); int slash = 0;
            foreach (char c in value) {
                if (c == '\\') { slash++; continue; }
                if (c == '"') b.Append('\\', slash * 2 + 1).Append('"');
                else b.Append('\\', slash).Append(c);
                slash = 0;
            }
            return b.Append('\\', slash * 2).Append('"').ToString();
        }
        public static void Open(string root, string slot, string url) {
            if (slot != "A" && slot != "B") throw new InvalidOperationException("未知账号。");
            if (!Allowed(url)) throw new InvalidOperationException("未识别此国服登录请求。请从战网重新发起登录；不要把临时登录参数发到聊天。");
            // Old directories may have been opened by Edge and contain OS sign-in data.
            // Keep them intact and use a fresh, Chrome-only namespace.
            string dir = Path.Combine(root, "Chrome", "CN-" + slot);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(Browser, "--user-data-dir=" + Quote(dir) + " --no-first-run --no-default-browser-check --new-window " + Quote(url)) { UseShellExecute = false });
        }
    }
}
