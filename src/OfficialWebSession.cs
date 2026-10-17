using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CnDirect;

namespace BrowserManager {
    public sealed class OfficialWebSession : AutoWebBackend {
        readonly string home;
        readonly EmbeddedSessions sessions;
        readonly Action<string> stage;
        static readonly SemaphoreSlim attachmentGate = new SemaphoreSlim(1, 1);

        public OfficialWebSession(string path, EmbeddedSessions windows, Action<string> progress) {
            home = path; sessions = windows; stage = progress;
        }
        public async Task PrepareBrowser(LoginRoute choice, CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            stage("准备所选账号的 " + Browser.Label(choice.Mode));
            if (choice.IsEmbedded) await sessions.Prepare(choice.Id, choice.Name, cancel);
            else Browser.StartInfo(home, choice.Id, Browser.Home, choice.Mode);
        }
        public Target[] Targets() { return Hook.Targets().ToArray(); }
        public bool ClientRunning() {
            foreach (string name in new[] { "Battle.net", "Battle.net Launcher" }) {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length > 0) return true; }
                finally { foreach (var p in processes) p.Dispose(); }
            }
            return false;
        }
        public bool HasClientWindow() {
            foreach (var p in Process.GetProcessesByName("Battle.net")) using (p)
                try { if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0) return true; } catch { }
            return false;
        }
        public void LaunchClient() {
            bool found=ClientWindowLaunch.Request(new WindowsClientWindow());
            stage(found?"已找到待登录窗口":"已发送新窗口启动请求，已有账号保持运行");
        }
        public Task Wait(CancellationToken cancel) { return Task.Delay(500, cancel); }
        public async Task<IDisposable> Attach(Target target, CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            WindowsClientWindow.PresentTarget(target.Pid);
            // A cancelled native attachment can finish late; serialize until it is removed.
            if (!await attachmentGate.WaitAsync(0, cancel))
                throw new InvalidOperationException("上一次登录接管仍在收尾，请稍后再试。");
            Hook installed = null;
            try {
                cancel.ThrowIfCancellationRequested();
                stage("准备新的登录请求");
                // Cleanup must still run if cancellation lets the UI close its dispatcher.
                installed = await Task.Run(() => Hook.Attach(target)).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                var result = installed; installed = null; return result;
            } finally {
                if (installed != null) installed.Dispose();
                attachmentGate.Release();
            }
        }
    }
}
