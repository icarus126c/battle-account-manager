using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;


using BrowserManager;
using CnRecheck;

namespace CnEngine {
    public sealed class PendingEngineException : InvalidOperationException { public PendingEngineException() : base("原版引擎仍在运行，备份已保留。请先核对客户端，不要再次启动。") { } }
    public interface Engine { void Prepare(NativeSelection[] accounts); void Swap(string key); }
    public static class Flow {
        public static string First(string names) { return (names ?? "").Split(',')[0].Trim(); }
        public static bool Valid(string key) {
            return key != null && key.Length <= 320 && (Regex.IsMatch(key, "^[0-9]+$") ||
                (key.Contains("@") && !key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || ",\"\\/:".Contains(c))));
        }
        public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        public static byte[] Patch(byte[] bytes, string selected) {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            var matches = Regex.Matches(text, @"""SavedAccountNames""\s*:\s*(?<value>""(?:[^""\\]|\\.)*"")");
            if (matches.Count != 1) throw new InvalidOperationException("账号选择字段无法唯一识别。");
            var span = matches[0].Groups["value"]; var json = new JavaScriptSerializer();
            object value;
            var root = NativeLogin.Parse(bytes); var client = (Dictionary<string, object>)root["Client"];
            if (!client.TryGetValue("SavedAccountNames", out value) || !(value is string) || json.Deserialize<string>(span.Value) != (string)value)
                throw new InvalidOperationException("账号选择结构未能核对。");
            return new UTF8Encoding(false).GetBytes(text.Substring(0, span.Index) + json.Serialize(selected) + text.Substring(span.Index + span.Length));
        }
        static object Ordered(object value, string path) {
            var dict = value as Dictionary<string, object>;
            if (dict != null) {
                var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in dict) {
                    string child = path.Length == 0 ? pair.Key : path + "." + pair.Key;
                    if (child != "Client.SavedAccountNames") sorted[pair.Key] = Ordered(pair.Value, child);
                }
                return sorted;
            }
            var items = value as object[]; return items == null ? value : items.Select(v => Ordered(v, path + "[]")).ToArray();
        }
        public static bool OthersSame(byte[] a, byte[] b) {
            var json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            return json.Serialize(Ordered(NativeLogin.Parse(a), "")) == json.Serialize(Ordered(NativeLogin.Parse(b), ""));
        }
        static string Names(byte[] bytes) { return (string)((Dictionary<string, object>)NativeLogin.Parse(bytes)["Client"])["SavedAccountNames"]; }
        public static bool OwnIntermediate(byte[] before, byte[] current, string key) {
            string names = Names(current); return OthersSame(before, current) && (names.Length == 0 || Same(names, key));
        }
        public static void Run(NativeClientBackend client, Engine engine, NativeSelection selected, NativeSelection[] accounts,
            Action<byte[]> backup, Action launchCurrent, Action<string> stage) {
            NativeSelections.Validate(selected, selected.AccountId);
            string key = First(selected.SavedNames);
            if (!Valid(key)) throw new InvalidOperationException("该国服账号标识不适用于本轮原版引擎试验。");
            byte[] before = null, fresh = null; bool changed = false;
            using (var gate = new Mutex(false, @"Local\BattleAccountManager.NativeConfig")) {
                bool held = false;
                try {
                    try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new InvalidOperationException("另一项账号操作仍在进行。");
                    stage("检查所有战网已退出及保持登录"); client.Idle(); before = NativeLogin.StableRead(client); NativeLogin.CheckCN(before);
                    if (client.Version != selected.ClientVersion) throw new InvalidOperationException("战网版本已变化，请重新登记。");
                    var state = (Dictionary<string, object>)NativeLogin.Parse(before)["Client"]; object auto;
                    if (!state.TryGetValue("AutoLogin", out auto) || !Same(Convert.ToString(auto), "true")) throw new InvalidOperationException("自动登录已关闭，请先完成保持登录的官方认证。");
                    backup(before); client.Idle(); fresh = client.ReadConfig();
                    if (!fresh.SequenceEqual(before)) throw new InvalidOperationException("配置刚发生变化，本次没有切换。");
                    if (Same(First(Names(before)), key)) {
                        stage("目标已是当前账号，原样启动"); client.Idle(); launchCurrent(); return;
                    }
                    stage("准备与外服相同的原版切换引擎"); engine.Prepare(accounts);
                    Array.Clear(fresh, 0, fresh.Length); fresh = client.ReadConfig(); client.Idle();
                    if (!fresh.SequenceEqual(before)) throw new InvalidOperationException("引擎准备期间配置变化，本次没有切换。");
                    stage("原版引擎清空选择、写入单个账号并启动"); changed = true; engine.Swap(key);
                    Array.Clear(fresh, 0, fresh.Length); fresh = client.ReadConfig();
                    if (!Same(First(Names(fresh)), key)) throw new InvalidOperationException("引擎返回后的账号选择未能核对。");
                    NativeLogin.CheckCN(fresh);
                    stage("启动已发出，等待核对客户端账号");
                } catch (Exception error) {
                    if (error is PendingEngineException) throw;
                    if (changed && before != null) {
                        bool restored = false;
                        try {
                            var guard = client as RollbackGuard;
                            if (guard != null) guard.RollbackIdle(); else client.Idle();
                            var current = client.ReadConfig();
                            try {
                                if (current.SequenceEqual(before)) restored = true;
                                else if (OwnIntermediate(before, current, key)) {
                                    if (guard != null) guard.RollbackWrite(before); else client.WriteConfig(before);
                                    var check = client.ReadConfig(); try { restored = check.SequenceEqual(before); } finally { Array.Clear(check, 0, check.Length); }
                                }
                            } finally { Array.Clear(current, 0, current.Length); }
                        } catch { }
                        if (!restored) throw new InvalidOperationException("启动未完成，无法安全撤回。备份已保留，请保留当前状态。", error);
                    }
                    throw;
                } finally { foreach (var bytes in new[] { before, fresh }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); if (held) gate.ReleaseMutex(); }
            }
        }
    }
    public sealed class OriginalEngine : Engine {
        readonly string root, folder, engineData;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        public OriginalEngine(string appRoot, string dataRoot = null) { root = appRoot; folder = Path.Combine(root, "Engine"); engineData = Path.Combine(dataRoot ?? root, "data", "tcno-cn-experiment"); }
        public static void Check(string root) {
            foreach (var pair in new[] {
                new[] {"TcNo-Acc-Switcher.exe", "c22ddad5eb76d2b72a3fe527f831198688bfac33e3f4e5f676d42ab5ab6c5c1a"},
                new[] {"Platforms-v4.0.8.json", "e0a8814336fc58ae90ffa8417a626828316456031c095a4310d98382e4667404"}}) {
                using (var stream = File.OpenRead(Path.Combine(root, "Engine", pair[0]))) using (var sha = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), pair[1], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("切换组件版本核对失败。");
            }
        }
        void Save(string path, object value) { Store.WriteAtomic(path, new UTF8Encoding(false).GetBytes(json.Serialize(value))); }
        public void Prepare(NativeSelection[] accounts) {
            Check(root); var others = Process.GetProcessesByName("TcNo-Acc-Switcher");
            try { if (others.Length != 0) throw new InvalidOperationException("请先正常关闭其他 TcNo 窗口。"); } finally { foreach (var p in others) p.Dispose(); }
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in accounts) {
                NativeSelections.Validate(account, account.AccountId); string key = Flow.First(account.SavedNames);
                if (!Flow.Valid(key) || ids.ContainsKey(key)) throw new InvalidOperationException("实验登记重复或账号标识不适用。");
                ids[key] = account.AccountId;
                Save(Path.Combine(engineData, "LoginCache", "BattleNet", account.AccountId, "email.json"), key);
            }
            Save(Path.Combine(engineData, "LoginCache", "BattleNet", "ids.json"), new { ids = ids, lastused = new Dictionary<string, string>() });
            Save(Path.Combine(folder, "TcNo-Acc-Switcher.settings.json"), new Dictionary<string, object> {
                {"version",1},{"language","zh-CN"},{"userDataPath",engineData},{"offlineMode",true},{"statsEnabled",false},
                {"statsShare",false},{"crashReportAutoSubmit",false},{"discordRpc",false},{"prereleaseUpdates",false},{"protocolEnabled",false},
                {"alwaysRunAsAdmin",false},{"startTrayWithWindows",false},{"exitToTray",false},
                {"platformExePaths",new Dictionary<string,string>{{"BattleNet",OfficialNativeClient.ClientPath}}}});
            var catalog = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(Path.Combine(folder, "Platforms-v4.0.8.json"), Encoding.UTF8));
            var descriptor = (Dictionary<string, object>)((Dictionary<string, object>)catalog["Platforms"])["BattleNet"];
            descriptor["ExesToEnd"] = new string[0];
            Save(Path.Combine(engineData, "Platforms.custom.json"), new { Platforms = new Dictionary<string, object> { { "BattleNet", descriptor } } });
            Save(Path.Combine(engineData, "Settings", "BattleNetSettings.json"), new { RunAsAdmin = false, AutoStart = true, ClosingMethod = "TaskKill", PullAccountImagesOnSwitch = false, ShowLastUsed = true, AccountNotes = new Dictionary<string, string>() });
        }
        public void Swap(string key) {
            if (!Flow.Valid(key)) throw new InvalidOperationException("账号标识无效。");
            new Client().Idle(); Check(root);
            var start = new ProcessStartInfo(Path.Combine(folder, "TcNo-Acc-Switcher.exe"), "\"+b:" + key + "\" --log-level=error") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
            using (var p = Process.Start(start)) {
                var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) throw new PendingEngineException();
                if (p.ExitCode != 0) throw new InvalidOperationException("原版引擎返回错误（" + p.ExitCode + "）。");
                Task.WaitAll(new Task[] { stdout, stderr }, 1000);
            }
        }
    }
}
