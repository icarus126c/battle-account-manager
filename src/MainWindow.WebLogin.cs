using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CnDirect;

namespace BrowserManager {
    public partial class MainWindow {
        async void PrepareWebLogin() { await PrepareWebLoginAsync(null); }

        async void Arm() {
            var target = targets.SelectedItem as Target;
            if (target == null) { status.Text = "请先刷新并选择国服登录窗口。"; return; }
            await PrepareWebLoginAsync(target);
        }

        // Automatic discovery and explicit window selection share cancellation, timeout and cleanup.
        async Task PrepareWebLoginAsync(Target selectedTarget) {
            if (busy) return;
            LoginRoute choice;
            try { choice = new LoginRoute(Require()); }
            catch (Exception error) { status.Text = error.Message; return; }
            Stop(); HidePassword(); SetBusy(true);
            var cancellation = new CancellationTokenSource();
            webPreparation = cancellation;
            cancelWeb.Visibility = Visibility.Visible;
            var trace = new StartTrace(store.Home);
            PreparedWebSession prepared = null;
            startStage = "网页登录";
            startWatch.Start();
            Action<string> progress = stage => {
                if (closed || cancellation.IsCancellationRequested) return;
                startStage = stage; trace.Stage(stage); status.Text = stage + "…";
            };
            try {
                var backend = new OfficialWebSession(store.Home, embedded, progress);
                prepared = selectedTarget == null
                    ? await WebSessionStarter.Automatic(backend, choice, cancellation.Token, progress)
                    : await WebSessionStarter.Selected(backend, choice, selectedTarget, cancellation.Token, progress);
                cancellation.Token.ThrowIfCancellationRequested();
                // Saving usage refreshes the selection; it must not cancel this completed operation.
                webPreparation = null;
                if (closed || VaultDenied) return;
                Used(choice.Id);
                AdoptWebSession(prepared);
                trace.Stage("准备就绪，等待用户发起新网页登录");
                status.Text = "准备就绪 · " + choice.Name + " · " + Browser.Label(choice.Mode)
                    + "。请在战网点“登录或注册”，完成网页确认。";
            } catch (OperationCanceledException) {
                status.Text = "已取消本次准备。原账号资料保留，已打开的战网保持运行。";
            } catch (Exception error) {
                trace.Failed(error);
                ShowRecovery();
                try { if (Hook.Targets().Count > 1) { RefreshTargets(); route.IsExpanded = true; } } catch { }
                status.Text = error is InvalidOperationException ? error.Message
                    : "网页登录准备未完成，请查看启动记录；原有战网窗口保持运行。";
            } finally {
                if (prepared != null) prepared.Dispose();
                webPreparation = null;
                cancellation.Dispose();
                cancelWeb.Visibility = hook == null ? Visibility.Collapsed : Visibility.Visible;
                startWatch.Stop();
                SetBusy(false);
            }
        }

        void AdoptWebSession(PreparedWebSession prepared) {
            var installed = prepared.Handle as Hook;
            if (installed == null) throw new InvalidOperationException("接管未能完成，请保留登录窗口并重试。");
            hook = installed;
            prepared.Handle = null;
            armedRoute = prepared.Choice;
            armedAt = DateTime.UtcNow;
            targets.ItemsSource = new[] { prepared.Target };
            targets.SelectedIndex = 0;
            hookTimer.Start();
        }
    }
}
