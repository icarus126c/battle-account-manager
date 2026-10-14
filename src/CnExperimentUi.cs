using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CnRecheck;
using CnEngine;

namespace BrowserManager {
    public static class CnRegistration {
        public static bool Eligible(Account account) { return account != null && account.Region == "CN" && !account.Archived; }
        public static NativeSelection[] LoadAccounts(string root, Data data) {
            return data.Accounts.Where(a => Eligible(a) && NativeSelections.Exists(root, a.Id)).Select(a => NativeSelections.Load(root, a.Id)).ToArray();
        }
        static NativeSelection Save(string root, Account account, Data data, NativeSelection selection) {
            foreach (var other in data.Accounts.Where(a => a.Region == "CN" && a.Id != account.Id && NativeSelections.Exists(root, a.Id))) {
                var registered = NativeSelections.Load(root, other.Id);
                if (Flow.Same(Flow.First(registered.SavedNames), Flow.First(selection.SavedNames))) throw new InvalidOperationException("该客户端标识已登记在其他国服条目中，请选择原条目。");
            }
            string path = NativeSelections.PathFor(root, account.Id);
            if (File.Exists(path)) Store.WriteAtomic(Path.Combine(root, "data", "history", Guid.NewGuid().ToString("N") + ".bin"), File.ReadAllBytes(path));
            NativeSelections.Save(root, selection); return selection;
        }
        public static NativeSelection Import(string root, Account account, Data data, string sourceRoot, string sourceId, string clientVersion) {
            if (!Eligible(account)) throw new InvalidOperationException("请选择对应的国服条目。");
            var source = NativeSelections.Load(sourceRoot, sourceId);
            if (source.ClientVersion != clientVersion || !Flow.Valid(Flow.First(source.SavedNames))) throw new InvalidOperationException("已验证记录不适用于当前版本，请重新登录并登记。");
            using (var gate = new Mutex(false, @"Local\BattleAccountManager.NativeConfig")) {
                bool held = false;
                try {
                    try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new InvalidOperationException("另一项账号操作仍在进行。");
                    return Save(root, account, data, new NativeSelection { Format = 1, Region = "CN", AccountId = account.Id,
                        ClientVersion = source.ClientVersion, SavedNames = source.SavedNames, RegisteredUtc = source.RegisteredUtc });
                } finally { if (held) gate.ReleaseMutex(); }
            }
        }
        public static NativeSelection Register(string root, Account account, Data data, NativeClientBackend backend) {
            if (!Eligible(account)) throw new InvalidOperationException("请选择未归档的国服账号。");
            byte[] before = null, current = null;
            using (var gate = new Mutex(false, @"Local\BattleAccountManager.NativeConfig")) {
                bool held = false;
                try {
                    try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new InvalidOperationException("另一项账号操作仍在进行。");
                    backend.Idle(); before = NativeLogin.StableRead(backend); NativeLogin.CheckCN(before);
                    var client = (Dictionary<string, object>)NativeLogin.Parse(before)["Client"]; object auto;
                    if (!client.TryGetValue("AutoLogin", out auto) || !Flow.Same(Convert.ToString(auto), "true")) throw new InvalidOperationException("保持登录尚未开启，请先在官方战网完成登录并正常退出。");
                    var selection = new NativeSelection { Format = 1, AccountId = account.Id, Region = "CN", ClientVersion = backend.Version,
                        SavedNames = NativeLogin.SavedNames(before), RegisteredUtc = DateTime.UtcNow };
                    if (!Flow.Valid(Flow.First(selection.SavedNames))) throw new InvalidOperationException("当前账号标识不适用于国服实验切换。");
                    backend.Idle(); current = backend.ReadConfig();
                    if (!current.SequenceEqual(before)) throw new InvalidOperationException("登记期间配置变化，未保存新登记。");
                    return Save(root, account, data, selection);
                } finally { foreach (var bytes in new[] { before, current }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); if (held) gate.ReleaseMutex(); }
            }
        }
    }
    public partial class MainWindow {
        readonly TextBlock cnExperimentalSummary = Ui.Text("尚未登记客户端账号。", 12);
        Expander cnExperiment; Button cnExperimentalStart, cnExperimentalRegister;
        string CnRegistrationHome { get { return Path.Combine(store.Home, "cn-exp"); } }
        void BuildCnExperiment() {
            cnExperiment = new Expander { Header = "国服一键登录（实验）", IsExpanded = true, Margin = new Thickness(0, 12, 0, 4) };
            var body = new StackPanel { Margin = new Thickness(12) }; cnExperiment.Content = body; details.Children.Insert(3, cnExperiment);
            body.Children.Add(Ui.Text("仅用于正常退出全部战网后的账号切换；多开继续使用网页登录。", 12));
            body.Children.Add(cnExperimentalSummary);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; body.Children.Add(row);
            cnExperimentalStart = ActionButton(row, "一键登录（实验）", StartCnExperiment, true);
            cnExperimentalRegister = ActionButton(row, "记住这个账号", RegisterCnExperiment, false);
            body.Children.Add(Ui.Text("首次使用：先用这个账号登录战网，确认昵称正确，再从菜单退出战网，最后点“记住这个账号”。", 12));
            body.Children.Add(Ui.Text("如果仍然要求登录，请用上方“网页登录”登录一次，退出战网后再点“记住这个账号”。", 11));
            UpdateCnExperiment(Selected);
        }
        void UpdateCnExperiment(Account selected) {
            if (cnExperiment == null) return;
            bool eligible = CnRegistration.Eligible(selected) && region == "CN";
            cnExperiment.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
            bool registered = false;
            try {
                if (eligible && NativeSelections.Exists(CnRegistrationHome, selected.Id)) {
                    NativeSelections.Load(CnRegistrationHome, selected.Id); registered = true;
                }
                cnExperimentalSummary.Text = registered ? "已记住这个账号，可以尝试一键登录。请核对战网实际进入的账号。" : "首次使用需先登录此账号，退出战网后点“记住这个账号”。";
            } catch { cnExperimentalSummary.Text = "需要重新记住账号：请先登录战网，退出后再点“记住这个账号”。"; }
            cnExperimentalRegister.IsEnabled = !busy && eligible;
            cnExperimentalStart.IsEnabled = !busy && eligible && registered;
        }
        async void RegisterCnExperiment() {
            Account account; Data snapshot;
            try { account = Require().Copy(); account.Password = ""; if (!CnRegistration.Eligible(account)) throw new InvalidOperationException("请选择列表中的国服账号。"); snapshot = Clone(); snapshot.Security = null; foreach (var item in snapshot.Accounts) item.Password = ""; }
            catch (Exception e) { status.Text = e.Message; return; }
            Stop(); HidePassword(); SetBusy(true); status.Text = "正在记住你刚退出的战网账号…";
            try {
                await Task.Run(() => CnRegistration.Register(CnRegistrationHome, account, snapshot, new Client()));
                status.Text = "已为“" + account.Name + "”保存了一键登录信息。以后先退出全部战网，再点“一键登录（实验）”。";
            } catch (Exception e) { status.Text = e is InvalidOperationException ? e.Message : "登记未完成，旧资料保留。错误类型：" + e.GetType().Name; }
            finally { SetBusy(false); UpdateLoginStatus(); }
        }
        async void StartCnExperiment() {
            Account account; NativeSelection selected; NativeSelection[] registered;
            try {
                account = Require().Copy(); account.Password = ""; if (!CnRegistration.Eligible(account)) throw new InvalidOperationException("请选择列表中的国服账号。");
                registered = CnRegistration.LoadAccounts(CnRegistrationHome, Clone()); selected = registered.SingleOrDefault(v => v.AccountId == account.Id);
                if (selected == null) throw new InvalidOperationException("请先用这个账号登录战网，退出战网后点“记住这个账号”。");
            } catch (Exception e) { status.Text = e is InvalidOperationException ? e.Message : "保存的信息无法读取，请重新登录后点“记住这个账号”。"; return; }
            Stop(); HidePassword(); SetBusy(true); var trace = new StartTrace(CnRegistrationHome); startStage = "准备国服实验切换"; startWatch.Start();
            Action<string> progress = value => { trace.Stage(value); startStage = value; Dispatcher.BeginInvoke(new Action(() => { if (!closed && busy) status.Text = value + "…"; })); };
            try {
                var client = new Client();
                await Task.Run(() => Flow.Run(client, new OriginalEngine(AppDomain.CurrentDomain.BaseDirectory, CnRegistrationHome), selected, registered,
                    bytes => NativeLogin.SaveBackup(Path.Combine(CnRegistrationHome, "backup", Guid.NewGuid().ToString("N")), bytes),
                    () => Dispatcher.Invoke(new Action(client.Launch)), progress));
                Used(account.Id); LoginRequested(account.Id,"native"); status.Text = "已为“" + account.Name + "”发起实验启动，请核对昵称。若出现登录页，可使用“网页登录”重新认证。";
            } catch (Exception e) { trace.Failed(e); ShowRecovery(); status.Text = e is InvalidOperationException ? e.Message : "国服实验启动未完成，备份保留。错误类型：" + e.GetType().Name; }
            finally { startWatch.Stop(); SetBusy(false); }
        }
    }
}
