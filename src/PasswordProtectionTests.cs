using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
namespace BrowserManager {
    public static class PasswordProtectionTests {
        const string Password = "FAKE master password 123", Changed = "FAKE changed password 456";
        static bool Reject(Action action) { try { action(); return false; } catch { return true; } }
        static T Field<T>(MainWindow window, string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); }
        static void Call(MainWindow window, string name, params object[] args) { typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, args); }
        static Data Fixture() { var data = Store.Initial(); var account = Account.New("FAKE_ACCOUNT", "CN"); account.Password = "  FAKE_SECRET_口令!  "; account.LoginAccount = "fake@example.invalid"; account.Note = "FAKE_NOTE"; data.Accounts.Add(account); return data; }
        public static void Run(string report) {
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "pw-" + Guid.NewGuid().ToString("N").Substring(0, 12)); Directory.CreateDirectory(root);
            var rows = new List<string>(); Action<bool, string> check = (ok, text) => { if (!ok) throw new Exception(text); rows.Add("通过：" + text); };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (var production = MasterKey.Create(Password, MasterKey.DefaultIterations)) {
                var packet = production.Seal(Encoding.UTF8.GetBytes("FAKE_PRODUCTION_KDF_ONLY"));
                check(BitConverter.ToInt32(packet, 8) == 600000 && Encoding.UTF8.GetString(production.Open(packet)) == "FAKE_PRODUCTION_KDF_ONLY", "生产强度的 600000 次 SHA-256 派生及解密实际可用");
            }
            clock.Stop(); rows.Add("生产强度首次派生耗时（虚构密码）：" + clock.ElapsedMilliseconds + " ms。");
            var data = Fixture(); var store = new Store(Path.Combine(root, "main")); store.PasswordIterations = 10000;
            store.Save(data); var old = File.ReadAllBytes(store.FilePath); store.Save(data);
            var accountId = data.Accounts[0].Id;
            store.EnableProtection(data, Password);
            check(store.IsProtected && !store.IsLocked && MasterKey.Protected(File.ReadAllBytes(store.FilePath)), "主密码保护使用实际加密格式，设置后当前会话已解锁");
            check(store.Load().Accounts[0].Password == data.Accounts[0].Password && store.Load().Accounts[0].Id == accountId, "保护迁移保留原密码空白、账号 ID 和资料");
            var saved = File.ReadAllBytes(store.FilePath); var backup = File.ReadAllBytes(store.FilePath + ".bak");
            check(!Encoding.UTF8.GetString(saved).Contains(data.Accounts[0].Password) && MasterKey.Protected(backup) && Reject(() => Store.Decode(saved)), "旧解码入口读不到受保护资料，当前备份也受保护");
            var history = Directory.GetFiles(Path.Combine(store.Home, "data", "protected-history"), "*.bav");
            check(history.Length == 1 && MasterKey.Protected(File.ReadAllBytes(history[0])), "启用时原自动备份保留为受保护的历史文件");
            using (var key = MasterKey.Derive(Password, saved)) {
                byte[] inner = key.Open(saved); check(Store.Decode(inner).Accounts[0].Password == data.Accounts[0].Password, "主密码解密后仍保留 Windows 当前用户加密层"); Array.Clear(inner, 0, inner.Length);
                var one = key.Seal(old); var two = key.Seal(old); check(!one.SequenceEqual(two), "每次加密使用随机 IV，不重复密文");
                foreach (int offset in new[] { 12, 44, 64, saved.Length - 1 }) { var damaged = (byte[])saved.Clone(); damaged[offset] ^= 1; check(Reject(() => key.Open(damaged)), "密文或认证头被改动时拒绝解密，位置 " + offset); }
                var oversizedRounds = (byte[])saved.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, oversizedRounds, 8, 4); check(Reject(() => MasterKey.Derive(Password, oversizedRounds)), "拒绝不受控的密码派生工作量");
            }
            using (var shortLived = MasterKey.Derive(Password, saved)) { shortLived.Dispose(); check(Reject(() => shortLived.Open(saved)), "释放密钥后不可再次解密"); }
            check(Reject(() => store.EnableProtection(data, Password)), "不会重复配置覆盖受保护密码库");
            store.Lock();
            check(store.IsLocked && Reject(() => store.Load()) && Reject(() => store.Save(Store.Initial())) && saved.SequenceEqual(File.ReadAllBytes(store.FilePath)), "锁定后不可读写，空列表不覆盖资料");
            var reopened = new Store(store.Home); reopened.PasswordIterations = 10000;
            check(reopened.IsLocked && Reject(() => reopened.Unlock("WRONG_PASSWORD")) && reopened.IsLocked && saved.SequenceEqual(File.ReadAllBytes(store.FilePath)), "重开默认锁定，错误主密码不改原文件");
            reopened.Unlock(Password);
            var export = reopened.Export(data); string exportPath = Path.Combine(root, "protected.bav"); Store.WriteAtomic(exportPath, export);
            check(MasterKey.Protected(File.ReadAllBytes(exportPath)) && reopened.ReadBackup(exportPath).Accounts[0].Password == data.Accounts[0].Password, "新导出备份纳入主密码保护");
            var foreign = new Store(Path.Combine(root, "other")); foreign.PasswordIterations = 10000; foreign.Save(Fixture()); foreign.EnableProtection(Fixture(), Changed);
            check(Reject(() => foreign.ReadBackup(exportPath)), "其他密码库不能凭自己的解锁密钥读取这份备份");
            check(foreign.ReadBackup(exportPath, Password).Accounts[0].Password == data.Accounts[0].Password && !foreign.IsLocked, "备份原主密码可导入，不替换当前会话密钥");
            check(Reject(() => reopened.ChangePassword(data, "WRONG_PASSWORD", Changed)) && reopened.Load().Accounts[0].Password == data.Accounts[0].Password, "修改主密码须校验原主密码，失败不损坏当前库");
            reopened.ChangePassword(data, Password, Changed); reopened.Lock();
            check(Reject(() => reopened.Unlock(Password)) && reopened.Unlock(Changed).Accounts[0].Password == data.Accounts[0].Password, "修改后旧主密码不能解锁主库，新主密码保留内容");
            check(reopened.ReadBackup(exportPath, Password).Accounts[0].Password == data.Accounts[0].Password, "之前导出的备份仍可用当时主密码读取");
            var tampered = File.ReadAllBytes(reopened.FilePath); tampered[tampered.Length - 1] ^= 1; File.WriteAllBytes(reopened.FilePath, tampered);
            check(Reject(() => reopened.Save(data)) && tampered.SequenceEqual(File.ReadAllBytes(reopened.FilePath)), "已解锁期间磁盘校验损坏也不会被保存覆盖");
            reopened.Lock(); foreign.Lock();
            var window = new MainWindow(true, "CN", Path.Combine(root, "ui"));
            try {
                var uiStore = Field<Store>(window, "store"); uiStore.PasswordIterations = 10000;
                var uiData = Field<Data>(window, "data"); uiStore.Save(uiData); uiStore.EnableProtection(uiData, Password);
                Call(window, "UpdateVaultAccess"); Call(window, "Reveal"); check(Field<TextBlock>(window, "secret").Text.Contains("FAKE_PREVIEW_SECRET"), "解锁后才可显示密码");
                Call(window, "LockVault");
                check(uiStore.IsLocked && Field<Data>(window, "data").Accounts.Count == 0 && Field<Border>(window, "vaultOverlay").Visibility == Visibility.Visible, "锁定卸下账号资料并显示锁定界面");
                check(!Field<TextBlock>(window, "secret").Text.Contains("FAKE_PREVIEW_SECRET") && Reject(() => Call(window, "Reveal")) && Reject(() => Call(window, "Require")), "锁定后显示和读取操作均被访问检查挡住");
                Entry.Render(window, Path.Combine(Path.GetDirectoryName(report), "preview-vault-locked.png"), 1100, 920);
                var unlocked = uiStore.Unlock(Password); typeof(MainWindow).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, unlocked);
                Call(window, "Refresh", new object[] { null }); Call(window, "UpdateVaultAccess");
                check(Field<Border>(window, "vaultOverlay").Visibility == Visibility.Collapsed && Field<Data>(window, "data").Accounts.Count == uiData.Accounts.Count, "正确解锁后恢复原账号列表和功能");
                Call(window, "CheckVaultIdle", DateTime.UtcNow.AddMinutes(6)); check(uiStore.IsLocked, "闲置超过五分钟自动锁定");
                using (var dialog = new DisposableDialog(null)) { Entry.Render(dialog.Window, Path.Combine(Path.GetDirectoryName(report), "preview-master-dialog.png"), 470, 440); }
            } finally { window.Close(); }
            rows.Add("仅使用虚构密码及工作目录文件；没有读取真实密码、改写真实密码库或操作系统剪贴板。"); File.WriteAllLines(report, rows, new UTF8Encoding(true));
        }
        sealed class DisposableDialog : IDisposable { public MasterDialog Window; public DisposableDialog(Window owner) { Window = new MasterDialog(owner, "设置主密码", (old, next) => System.Threading.Tasks.Task.FromResult(0)); } public void Dispose() { Window.Close(); } }
    }
}
