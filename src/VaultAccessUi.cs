using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BrowserManager {
    public sealed class MasterDialog : Window {
        readonly PasswordBox current = new PasswordBox(), password = new PasswordBox(), confirm = new PasswordBox();
        readonly TextBlock message = Ui.Text("", 12); readonly Button submit;
        readonly string mode; bool working; readonly PasswordBox factor = new PasswordBox(); readonly Func<string,string,string,Task> securedOperation;
        public MasterDialog(Window owner, string action, Func<string, string, Task> callback) : this(owner, action, (old, next, code) => callback(old, next), false) { }
        public MasterDialog(Window owner, string action, Func<string,string,string,Task> callback, bool needsFactor) {
            Owner = owner; mode = action; securedOperation = callback; Title = action; Width = 470; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14; Background = Ui.Brush("#F4F6FB"); var body = new StackPanel(); Content = new Border { Padding = new Thickness(26), Background = Background, Child = body };
            body.Children.Add(Ui.Text(action, 23));
            body.Children.Add(Ui.Text(action == "移除主密码" ? "关闭后可以免主密码使用，仍保留 Windows 用户加密。两步验证也会关闭。旧备份保持原保护方式。" : action == "关闭两步验证" ? "验证主密码和动态码后关闭。旧备份仍保留当时的验证设置。" : action == "设置主密码" ? "主密码可选。设置后先解锁才能使用资料；请记牢，遗忘后无法解开受保护资料。" : action == "修改主密码" ? "请输入原主密码。之前导出的备份仍使用当时的主密码。" : "输入主密码后查看账号资料和密码。", 12));
            if (action == "修改主密码") { body.Children.Add(Ui.Text("原主密码", 13)); current.Padding = new Thickness(9); current.MaxLength = 256; body.Children.Add(current); }
            body.Children.Add(Ui.Text(action == "修改主密码" ? "新主密码" : "主密码", 13)); password.Padding = new Thickness(9); password.MaxLength = 256; body.Children.Add(password);
            if (action == "设置主密码" || action == "修改主密码") {
                body.Children.Add(Ui.Text("再次输入", 13)); confirm.Padding = new Thickness(9); confirm.MaxLength = 256; body.Children.Add(confirm);
                body.Children.Add(Ui.Text("10–256 个字符，可以使用容易记住的短句。", 11));
            }
            if (needsFactor) { body.Children.Add(Ui.Text("6 位动态码 / 备用恢复码", 13)); factor.Padding = new Thickness(9); factor.MaxLength = 40; body.Children.Add(factor); body.Children.Add(Ui.Text("刚用过的动态码请等下一组；恢复码只能使用一次。",11)); }
            message.Foreground = Ui.Brush("#B84438"); body.Children.Add(message);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; body.Children.Add(row);
            var cancel = Ui.Button("取消", delegate { if (!working) DialogResult = false; }, false); cancel.IsCancel = true; row.Children.Add(cancel);
            submit = Ui.Button(action == "设置主密码" ? "设置并解锁" : action == "修改主密码" ? "修改" : action == "移除主密码" ? "确认移除" : action == "关闭两步验证" ? "验证并关闭" : "解锁", Run, true); submit.IsDefault = true; row.Children.Add(submit);
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; };
            Closed += delegate { current.Clear(); password.Clear(); confirm.Clear(); factor.Clear(); };
            Loaded += delegate { if (action == "修改主密码") current.Focus(); else password.Focus(); };
        }
        async void Run() {
            if (working) return;
            string next = password.Password, old = current.Password;
            if (next.Length == 0) { message.Text = "请输入主密码。"; return; }
            if (mode == "设置主密码" || mode == "修改主密码") {
                if (string.IsNullOrWhiteSpace(next) || next.Length < 10) { message.Text = "主密码至少需要 10 个字符，不能全为空白。"; return; }
                if (next != confirm.Password) { message.Text = "两次输入不一致。"; return; }
            }
            string code = factor.Password; working = true; submit.IsEnabled = current.IsEnabled = password.IsEnabled = confirm.IsEnabled = factor.IsEnabled = false; message.Text = "正在校验并处理…";
            try { await securedOperation(old, next, code); working = false; DialogResult = true; }
            catch (Exception e) { message.Text = e is InvalidOperationException ? e.Message : "资料无法读取／保存，原资料已保留。"; factor.Clear(); }
            finally { working = false; submit.IsEnabled = current.IsEnabled = password.IsEnabled = confirm.IsEnabled = factor.IsEnabled = true; }
        }
    }
    public partial class MainWindow {
        bool vaultDialogOpen, sessionLockPending; DateTime lastVaultActivity = DateTime.UtcNow;
        Border vaultOverlay; TextBlock vaultHeadline, vaultDescription, vaultState; Button vaultAction, vaultSettings, vaultLock;
        readonly DispatcherTimer vaultIdle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        Data LoadInitialData() { try { return store.Load(); } catch (VaultLockedException) { return Store.Initial(); } }
        bool VaultDenied { get { return store.IsLocked; } }
        void RequireVaultAccess() { if (VaultDenied) throw new VaultLockedException(); }
        void BuildVaultAccess(Grid root, StackPanel sidebar) {
            vaultState = Ui.Text("", 11); sidebar.Children.Add(vaultState);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; sidebar.Children.Add(row);
            vaultSettings = Ui.Button("保护设置", ConfigureVault, false); row.Children.Add(vaultSettings);
            vaultLock = Ui.Button("锁定", LockVault, false); row.Children.Add(vaultLock);
            vaultOverlay = new Border { Background = Ui.Brush("#F4F6FB"), Padding = new Thickness(40), Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(vaultOverlay, 2); Panel.SetZIndex(vaultOverlay, 100); root.Children.Add(vaultOverlay);
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 460 };
            vaultOverlay.Child = panel; vaultHeadline = Ui.Text("密码库已锁定", 28); panel.Children.Add(vaultHeadline);
            vaultDescription = Ui.Text("", 14); panel.Children.Add(vaultDescription);
            vaultAction = Ui.Button("解锁密码库", ConfigureVault, true); panel.Children.Add(vaultAction);
            panel.Children.Add(Ui.Text("主密码仅在软件里输入。解锁后闲置 5 分钟自动锁定；锁屏立即锁定。", 12));
            var exit = Ui.Button("退出管家", delegate { Close(); }, false); panel.Children.Add(exit);
            vaultIdle.Tick += delegate { CheckVaultIdle(DateTime.UtcNow); };
            InputManager.Current.PreProcessInput += VaultInput;
            if (!preview) { SystemEvents.SessionSwitch += VaultSessionSwitch; vaultIdle.Start(); }
            Closed += delegate {
                vaultIdle.Stop(); InputManager.Current.PreProcessInput -= VaultInput;
                if (!preview) SystemEvents.SessionSwitch -= VaultSessionSwitch;
                store.Lock(); ClearVaultData();
            };
            UpdateVaultAccess();
        }
        void VaultInput(object sender, PreProcessInputEventArgs e) {
            var routed = e.StagingItem.Input.RoutedEvent;
            if (routed == Keyboard.PreviewKeyDownEvent || routed == Mouse.PreviewMouseDownEvent || routed == Mouse.PreviewMouseMoveEvent || routed == Mouse.PreviewMouseWheelEvent || routed == TextCompositionManager.PreviewTextInputEvent)
                lastVaultActivity = DateTime.UtcNow;
        }
        void VaultSessionSwitch(object sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
                Dispatcher.BeginInvoke(new Action(() => { if (!closed) { sessionLockPending = true; LockVault(); } }));
        }
        void CheckVaultIdle(DateTime now) { if (!busy && !vaultDialogOpen && store.IsProtected && !store.IsLocked && now - lastVaultActivity >= TimeSpan.FromMinutes(5)) LockVault(); }
        void ClearVaultData() { if (data != null) { foreach (var account in data.Accounts) account.Password = ""; data.Security = null; data = Store.Initial(); } }
        void UpdateVaultAccess() {
            if (vaultOverlay == null) return;
            bool denied = VaultDenied;
            vaultState.Text = store.IsProtected ? (store.IsLocked ? "密码库已锁定" : "已解锁" + (store.NeedsFactor ? " · 两步验证已开启" : " · 主密码保护")) : "免主密码使用 · Windows 用户加密";
            vaultSettings.Content = "保护设置";
            vaultSettings.IsEnabled = !busy && !store.IsLocked; vaultLock.IsEnabled = !busy && store.IsProtected && !store.IsLocked;
            vaultOverlay.Visibility = denied ? Visibility.Visible : Visibility.Collapsed;
            vaultHeadline.Text = store.IsProtected ? "密码库已锁定" : "设置主密码，保护账号资料";
            vaultDescription.Text = store.NeedsFactor ? "输入主密码和验证器动态码／恢复码后，才能查看、复制和编辑资料。" : "输入主密码解锁后，才能查看、复制和编辑账号密码。";
            vaultAction.Content = store.IsProtected ? "解锁密码库" : "设置主密码";
            vaultAction.IsEnabled = !busy && !vaultDialogOpen;
        }
        void LockVault() {
            if (!store.IsProtected) { UpdateVaultAccess(); return; }
            if (vaultDialogOpen) return;
            sessionLockPending = false;
            foreach (var editor in OwnedWindows.OfType<Editor>().ToArray()) editor.Close();
            foreach (var settings in OwnedWindows.OfType<SettingsDialog>().ToArray()) settings.Close();
            Stop(); HidePassword(); clipboard.ClearOwn(); store.Lock(); ClearVaultData(); search.Clear(); Refresh();
            status.Text = "密码库已锁定。请先解锁后使用账号资料。"; UpdateVaultAccess();
        }
        void ConfigureVault() {
            if (preview || busy || vaultDialogOpen) return;
            string mode = !store.IsProtected ? "设置主密码" : store.IsLocked ? "解锁密码库" : "修改主密码";
            Stop(); HidePassword(); clipboard.ClearOwn(); vaultDialogOpen = true; UpdateVaultAccess();
            Data snapshot = data;
            try {
                if (!store.IsLocked) { var settings = new VaultOptionsDialog(this, store.IsProtected, store.NeedsFactor); if (settings.ShowDialog() != true) return; mode = settings.Choice; }
                if (mode == "启用两步验证") {
                    var enrollment = new FactorEnrollmentDialog(this, (password, seed, code, recovery) => Task.Run(() => { snapshot = store.EnableTwoFactor(password, seed, code, recovery); }));
                    if (enrollment.ShowDialog() == true) { data = snapshot; lastVaultActivity = DateTime.UtcNow; Refresh(); status.Text = "两步验证已开启。下次解锁需要主密码和手机动态码；请保管备用恢复码。"; }
                    return;
                }
                var dialog = new MasterDialog(this, mode, (old, next, code) => Task.Run(() => {
                    if (mode == "设置主密码") store.EnableProtection(snapshot, next);
                    else if (mode == "修改主密码") store.ChangePassword(snapshot, old, next, code);
                    else if (mode == "移除主密码") snapshot = store.RemoveProtection(next, code);
                    else if (mode == "关闭两步验证") snapshot = store.DisableTwoFactor(next, code);
                    else snapshot = store.Unlock(next, code);
                }), store.NeedsFactor);
                if (dialog.ShowDialog() == true) {
                    data = snapshot; lastVaultActivity = DateTime.UtcNow; Refresh();
                    status.Text = mode == "解锁密码库" ? "密码库已解锁。" : mode == "移除主密码" ? "已切换为免主密码使用，原账号资料保留。" : mode == "关闭两步验证" ? "两步验证已关闭，主密码保护保留。" : "主密码保护已更新。请妥善保管主密码。";
                }
            } finally { vaultDialogOpen = false; if (sessionLockPending) LockVault(); UpdateVaultAccess(); }
        }
        Data ImportVaultBackup(string path) {
            try { return store.ReadBackup(path); }
            catch (VaultLockedException) {
                Data imported = null; vaultDialogOpen = true;
                try {
                    var dialog = new MasterDialog(this, "解锁加密备份", (old, password, code) => Task.Run(() => { imported = store.ReadBackup(path, password, code); }), MasterKey.HasFactor(File.ReadAllBytes(path)));
                    return dialog.ShowDialog() == true ? imported : null;
                } finally { vaultDialogOpen = false; lastVaultActivity = DateTime.UtcNow; if (sessionLockPending) LockVault(); }
            }
        }
    }
}
