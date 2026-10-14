using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;
using CnEngine;
namespace BrowserManager {
    public static class CnManagerTests {
        sealed class Backend : NativeClientBackend {
            public byte[] Data; public bool Busy, Drift; public int Reads, Writes, Launches;
            public string Version { get { return "FAKE"; } }
            public void Idle() { if (Busy) throw new InvalidOperationException("FAKE_RUNNING"); }
            public byte[] ReadConfig() { Reads++; if (Drift && Reads == 3) Data = Config("99999"); return (byte[])Data.Clone(); }
            public void WriteConfig(byte[] bytes) { Writes++; throw new Exception("Registration must not write real client config"); }
            public void Launch() { Launches++; throw new Exception("Registration must not launch client"); }
        }
        static byte[] Config(string key) { return Encoding.UTF8.GetBytes("{\"Client\":{\"SavedAccountNames\":\"" + key + "\",\"AutoLogin\":\"true\",\"AutoLoginCN\":\"true\"},\"Device\":{\"LastLoginRegion\":\"CN\"}}"); }
        static T Field<T>(MainWindow window, string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); }
        static object Call(MainWindow window, string name, params object[] args) { return typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, args); }
        static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        public static void Run(string report) {
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "cn-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(root);
            var rows = new List<string>(); Action<bool, string> check = (ok, text) => { if (!ok) throw new Exception(text); rows.Add("通过：" + text); };
            var data = Store.Initial(); var a = Account.New("FAKE CN A", "CN"); a.Password = "FAKE_MEMO_PASSWORD"; a.LoginAccount = "not-the-native-id@example.invalid";
            var b = Account.New("FAKE CN B", "CN"); var global = Account.New("FAKE GLOBAL", "GLOBAL"); data.Accounts.Add(a); data.Accounts.Add(b); data.Accounts.Add(global);
            var backend = new Backend { Data = Config("11111") };
            var selection = CnRegistration.Register(root, a, data, backend);
            check(selection.SavedNames == "11111" && selection.AccountId == a.Id && backend.Writes == 0 && backend.Launches == 0, "登记取官方选择，不取备忘邮箱，不改配置或启动");
            check(!new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(selection).Contains(a.Password), "原生登记不包含备忘密码");
            check(CnRegistration.LoadAccounts(root, data).Length == 1, "已登记账号可被引擎加载");
            check(Reject(() => CnRegistration.Register(root, b, data, new Backend { Data = Config("11111") })) && !NativeSelections.Exists(root, b.Id), "同一官方标识不能误登记成另一个账号");
            check(Reject(() => CnRegistration.Register(root, b, data, new Backend { Data = Config("22222"), Busy = true })) && !NativeSelections.Exists(root, b.Id), "残留登录窗口也阻止登记");
            check(Reject(() => CnRegistration.Register(root, b, data, new Backend { Data = Config("22222"), Drift = true })) && !NativeSelections.Exists(root, b.Id), "配置变化时不写新登记");
            check(Reject(() => CnRegistration.Register(root, global, data, new Backend { Data = Config("22222") })), "外服不能进入国服登记流程");
            b.Archived = true; check(Reject(() => CnRegistration.Register(root, b, data, new Backend { Data = Config("22222") })), "归档账号不能登记"); b.Archived = false;
            byte[] old = File.ReadAllBytes(NativeSelections.PathFor(root, a.Id));
            CnRegistration.Register(root, a, data, new Backend { Data = Config("33333") });
            check(NativeSelections.Load(root, a.Id).SavedNames == "33333" && Directory.GetFiles(Path.Combine(root, "data", "history"), "*.bin").Any(p => File.ReadAllBytes(p).SequenceEqual(old)), "重新登记保留旧加密记录，可核对历史");
            var aliasId = Guid.NewGuid().ToString("N"); string proven = Path.Combine(root, "proven");
            NativeSelections.Save(proven, new NativeSelection { Format = 1, Region = "CN", AccountId = aliasId, ClientVersion = "FAKE", SavedNames = "55555" });
            var imported = CnRegistration.Import(root, b, data, proven, aliasId, "FAKE");
            check(imported.AccountId == b.Id && imported.SavedNames == "55555" && NativeSelections.Load(root, b.Id).AccountId == b.Id, "临时验证编号由用户选条目绑定，不猜测名称或账号身份");
            check(Reject(() => CnRegistration.Import(root, a, data, proven, aliasId, "FAKE")) && NativeSelections.Load(root, a.Id).SavedNames == "33333", "验证记录不能误覆盖另一个已绑定条目");
            check(Reject(() => CnRegistration.Import(root, b, data, proven, aliasId, "CHANGED")), "版本不匹配时不能导入旧验证记录");
            var window = new MainWindow(true, "CN", Path.Combine(root, "memo"));
            try {
                var start = Field<Button>(window, "cnExperimentalStart");
                var register = Field<Button>(window, "cnExperimentalRegister"); var selected = (Account)Field<ListBox>(window, "list").SelectedItem;
                check(!start.IsEnabled && register.IsEnabled && Field<Expander>(window, "cnExperiment").IsExpanded, "实验入口展开，首次可记住账号，尚不能启动");
                check(!start.IsEnabled && register.IsEnabled, "未登记账号只能登记，不能伪启动");
                string registrations = Path.Combine(root, "memo", "cn-exp");
                var recorded = new NativeSelection { Format = 1, AccountId = selected.Id, Region = "CN", ClientVersion = "FAKE", SavedNames = "44444" };
                NativeSelections.Save(registrations, recorded); Call(window, "UpdateCnExperiment", selected);
                check(start.IsEnabled, "登记后所选账号可用实验启动");
                Field<Expander>(window, "cnExperiment").IsExpanded = true;
                Entry.Render(window, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "preview-cn-experiment.png"), 1100, 920);
                Call(window, "SetBusy", true); check(!start.IsEnabled && !register.IsEnabled, "操作期间不能重复记住账号或启动");
                Call(window, "SetBusy", false); Call(window, "SetRegion", "GLOBAL");
                check(Field<Expander>(window, "cnExperiment").Visibility == System.Windows.Visibility.Collapsed, "外服页面不混入国服实验");
                check(!Field<CheckBox>(window, "skipClientCheck").IsChecked.Value, "外服跳过检测仍默认关闭，国服启用不影响它");
            } finally { window.Close(); }
            var reopened = new MainWindow(true, "CN", Path.Combine(root, "memo"));
            try { check(Field<Expander>(reopened, "cnExperiment").IsExpanded, "重开管家后实验入口仍默认展开"); } finally { reopened.Close(); }
            File.WriteAllLines(report, rows, new UTF8Encoding(true));
        }
    }
}
