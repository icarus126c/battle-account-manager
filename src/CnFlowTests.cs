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
    public static class Tests {
        public static void EngineCheck(string report) {
            string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "engine-fixture-" + Guid.NewGuid().ToString("N"), "中文目录");
            Directory.CreateDirectory(Path.Combine(fixture, "Engine"));
            foreach (string file in new[] { "TcNo-Acc-Switcher.exe", "Platforms-v4.0.8.json" }) File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Engine", file), Path.Combine(fixture, "Engine", file));
            var a = Account("123456"); var b = Account("fake-b@example.invalid");
            new OriginalEngine(fixture).Prepare(new[] { a, b });
            var json = new JavaScriptSerializer();
            var custom = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(Path.Combine(fixture, "data", "tcno-cn-experiment", "Platforms.custom.json"), Encoding.UTF8));
            var platform = (Dictionary<string, object>)((Dictionary<string, object>)custom["Platforms"])["BattleNet"];
            if (((object[])platform["ExesToEnd"]).Length != 0) throw new Exception("Fake fixture can close real processes");
            var start = new ProcessStartInfo(Path.Combine(fixture, "Engine", "TcNo-Acc-Switcher.exe"), "--list-accounts=BattleNet --json --log-level=error") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.Combine(fixture, "Engine") };
            using (var process = Process.Start(start)) {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) throw new Exception("Original readonly list timeout; fixture preserved");
                if (process.ExitCode != 0) throw new Exception("Original readonly list failed");
                Task.WaitAll(stdout, stderr);
                var result = (Dictionary<string, object>)json.DeserializeObject(stdout.Result);
                var accounts = ((object[])result["accounts"]).OfType<Dictionary<string, object>>().ToArray();
                if (accounts.Length != 2 || !accounts.Any(x => Convert.ToString(x["uniqueId"]) == a.SavedNames) || !accounts.Any(x => Convert.ToString(x["uniqueId"]) == b.SavedNames))
                    throw new Exception("Original engine did not recognize both fake numeric/email accounts");
            }
            File.WriteAllLines(report, new[] {
                "通过：原版引擎和平台定义版本指纹校验。", "通过：真实引擎识别虚构数字国服标识和邮箱标识。",
                "通过：中文独立资料路径可用。", "通过：引擎关闭程序列表为空。",
                "仅运行原版只读账号列表命令，未切换、启动或结束真实战网；没有密码或验证码参与。" }, new UTF8Encoding(true));
        }
        sealed class Fake : NativeClientBackend, RollbackGuard {
            public byte[] Data; public int Reads, Writes, Launches; public bool Busy, Drift;
            public string Version { get { return "FAKE"; } }
            public void Idle() { if (Busy) throw new InvalidOperationException("FAKE_BUSY"); }
            public void RollbackIdle() { Idle(); } public void RollbackWrite(byte[] bytes) { Idle(); WriteConfig(bytes); }
            public byte[] ReadConfig() { Reads++; if (Drift && Reads == 3) Data = Config("changed@example.invalid"); return (byte[])Data.Clone(); }
            public void WriteConfig(byte[] bytes) { Writes++; Data = (byte[])bytes.Clone(); }
            public void Launch() { Launches++; }
        }
        sealed class FakeEngine : Engine {
            public Fake Client; public int Prepared, Swaps; public bool Fail, BecomeBusy, ChangeOther, Pending;
            public void Prepare(NativeSelection[] accounts) { Prepared++; }
            public void Swap(string key) { Swaps++; Client.WriteConfig(Flow.Patch(Client.Data, "")); if (BecomeBusy) Client.Busy = true; if (Pending) throw new PendingEngineException(); if (Fail) throw new InvalidOperationException("FAKE_ENGINE_FAIL"); Client.WriteConfig(Flow.Patch(Client.Data, key)); if (ChangeOther) { Client.Data = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Client.Data).Replace("123.45", "999")); throw new InvalidOperationException("FAKE_OTHER_CHANGED"); } Client.Launch(); }
        }
        static byte[] Config(string names) { return Encoding.UTF8.GetBytes("{\r\n\"Client\":{\"SavedAccountNames\":\"" + names + "\",\"AutoLogin\":\"true\",\"AutoLoginCN\":\"true\"},\"Device\":{\"Services\":{\"LastLoginRegion\":\"CN\"}},\"other\":123.45\r\n}"); }
        static NativeSelection Account(string key) { return new NativeSelection { Format = 1, Region = "CN", AccountId = Guid.NewGuid().ToString("N"), ClientVersion = "FAKE", SavedNames = key }; }
        static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        public static void Run(string path) {
            var rows = new List<string>(); Action<bool,string> check = (ok, name) => { if (!ok) throw new Exception(name); rows.Add("通过：" + name); };
            var a = Account("123456"); var b = Account("fake-b@example.invalid"); var accounts = new[] { a, b }; int backed = 0;
            var client = new Fake { Data = Config(a.SavedNames + "," + b.SavedNames) }; var engine = new FakeEngine { Client = client }; var original = (byte[])client.Data.Clone();
            Flow.Run(client, engine, a, accounts, bytes => { backed++; }, client.Launch, value => { });
            check(backed == 1 && engine.Prepared == 0 && engine.Swaps == 0 && client.Writes == 0 && client.Launches == 1 && client.Data.SequenceEqual(original), "同账号原样启动，完全不改配置和调用切换");
            Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { });
            check(engine.Prepared == 1 && engine.Swaps == 1 && client.Launches == 2, "切号交给原版引擎，只启动一份，不重复启动");
            check(NativeLogin.SavedNames(client.Data) == b.SavedNames && Flow.OthersSame(original, client.Data), "切号只写单个目标标识，不合并列表，其他设置保留");
            Flow.Run(client, engine, a, accounts, bytes => { }, client.Launch, value => { });
            check(NativeLogin.SavedNames(client.Data) == a.SavedNames && engine.Swaps == 2, "数字国服标识与邮箱标识可正反向选择");
            check(Flow.Valid("123456") && Flow.Valid("fake@example.invalid") && Flow.Valid("fake+tag@example.invalid") && !Flow.Valid("a,b") && !Flow.Valid("x\" --switch") && !Flow.Valid("tcno://x"), "数字及含加号邮箱可用，拒绝参数分隔和注入内容");
            client = new Fake { Data = original, Busy = true }; engine = new FakeEngine { Client = client };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && engine.Prepared == 0 && client.Writes == 0, "已有已登录或未登录战网时修改前停止");
            client = new Fake { Data = original, Drift = true }; engine = new FakeEngine { Client = client };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && engine.Swaps == 0, "准备期间配置变化，不执行切换");
            client = new Fake { Data = original }; engine = new FakeEngine { Client = client, Fail = true };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && client.Data.SequenceEqual(original), "引擎清空后失败且仍无客户端时恢复自己改动");
            client = new Fake { Data = original }; engine = new FakeEngine { Client = client, Fail = true, BecomeBusy = true };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && client.Writes == 1, "客户端已运行时不回写旧配置");
            client = new Fake { Data = original }; engine = new FakeEngine { Client = client, Pending = true };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && client.Writes == 1, "引擎超时仍可能继续修改，不与它并发撤回");
            client = new Fake { Data = original }; engine = new FakeEngine { Client = client, ChangeOther = true };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && client.Writes == 2 && Encoding.UTF8.GetString(client.Data).Contains("999"), "其他字段发生变化不强行回滚覆盖");
            var disabled = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\"AutoLogin\":\"true\"", "\"AutoLogin\":\"false\""));
            client = new Fake { Data = disabled }; engine = new FakeEngine { Client = client };
            check(Reject(() => Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { })) && engine.Prepared == 0, "保持登录关闭时不给出伪成功");
            var reordered = Encoding.UTF8.GetBytes("{\"other\":123.45,\"Device\":{\"Services\":{\"LastLoginRegion\":\"CN\"}},\"Client\":{\"AutoLoginCN\":\"true\",\"AutoLogin\":\"true\",\"SavedAccountNames\":\"123456\"}}");
            check(Flow.OthersSame(original, reordered) && !Flow.OthersSame(original, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(reordered).Replace("123.45", "999"))), "核对兼容原版 JSON 字段重排，同时识别其他字段变化");
            var noCnFlag = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace(",\"AutoLoginCN\":\"true\"", ""));
            client = new Fake { Data = noCnFlag }; engine = new FakeEngine { Client = client };
            Flow.Run(client, engine, a, accounts, bytes => { }, client.Launch, value => { });
            check(client.Launches == 1 && client.Writes == 0 && client.Data.SequenceEqual(noCnFlag), "缺少国服专用标记但通用保持登录开启时，同账号可以原样启动，不补写开关");
            Flow.Run(client, engine, b, accounts, bytes => { }, client.Launch, value => { });
            check(engine.Swaps == 1 && NativeLogin.SavedNames(client.Data) == b.SavedNames && Flow.OthersSame(noCnFlag, client.Data), "缺少专用标记时切号仍核对区服、登记版本和其他配置，不修改登录开关");
            check(!Reject(() => NativeLogin.CheckCN(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(noCnFlag).Replace("\"AutoLogin\":\"true\"", "\"AutoLogin\":true")))), "缺少专用标记时兼容布尔型通用保持登录");
            foreach(string value in new[]{"false","null","\"unknown\""}) {
                var explicitDisabled = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\"AutoLoginCN\":\"true\"", "\"AutoLoginCN\":"+value));
                client = new Fake { Data = explicitDisabled }; engine = new FakeEngine { Client = client };
                check(Reject(() => Flow.Run(client,engine,b,accounts,bytes=>{},client.Launch,value2=>{})) && engine.Prepared==0 && client.Launches==0, "国服标记明确关闭或异常时仍阻挡启动："+value);
            }
            check(Reject(() => NativeLogin.CheckCN(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(noCnFlag).Replace("\"AutoLogin\":\"true\"", "\"AutoLogin\":false")))), "缺少专用标记且通用保持登录关闭时停止");
            check(Reject(() => NativeLogin.CheckCN(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(noCnFlag).Replace(",\"AutoLogin\":\"true\"", "")))), "两个保持登录标记均缺失时停止");
            check(Reject(() => NativeLogin.CheckCN(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(noCnFlag).Replace("\"LastLoginRegion\":\"CN\"", "\"LastLoginRegion\":\"US\"")))), "缺少专用标记时仍拒绝外服配置");
            check(Reject(() => NativeLogin.CheckCN(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(noCnFlag).Replace("\"SavedAccountNames\":\""+a.SavedNames+","+b.SavedNames+"\"", "\"SavedAccountNames\":\"\"")))), "缺少专用标记时仍拒绝空账号选择");
            File.WriteAllLines(path, rows, new UTF8Encoding(true));
        }
    }
}
