using System;
using System.Threading;
using System.Threading.Tasks;
using CnDirect;

namespace BrowserManager {
    public interface WebSessionBackend {
        Task PrepareBrowser(LoginRoute choice, CancellationToken cancel);
        Target[] Targets();
        Task<IDisposable> Attach(Target target, CancellationToken cancel);
    }
    public interface AutoWebBackend : WebSessionBackend {
        bool ClientRunning();
        bool HasClientWindow();
        void LaunchClient();
        Task Wait(CancellationToken cancel);
    }
    public sealed class PreparedWebSession : IDisposable {
        public readonly LoginRoute Choice;
        public readonly Target Target;
        public IDisposable Handle;
        public PreparedWebSession(LoginRoute choice, Target target, IDisposable handle) {
            Choice = choice; Target = target; Handle = handle;
        }
        public void Dispose() { if (Handle != null) { Handle.Dispose(); Handle = null; } }
    }

    public static class WebSessionStarter {
        public static async Task<PreparedWebSession> Automatic(AutoWebBackend backend, LoginRoute choice,
            CancellationToken cancel, Action<string> progress) {
            cancel.ThrowIfCancellationRequested();
            var candidates = backend.Targets();
            if (candidates.Length == 0) {
                if (!backend.ClientRunning()) {
                    cancel.ThrowIfCancellationRequested();
                    progress("正在打开官方战网，请等待登录窗口");
                    backend.LaunchClient();
                } else progress("战网正在运行，正在等待登录窗口");
                // A visible window can still be a startup screen. Wait for an actual login target.
                for (int attempt = 0; attempt < 90 && candidates.Length == 0; attempt++) {
                    await backend.Wait(cancel);
                    cancel.ThrowIfCancellationRequested();
                    candidates = backend.Targets();
                }
            }
            cancel.ThrowIfCancellationRequested();
            if (candidates.Length == 0)
                throw new InvalidOperationException("等待战网登录窗口超时。请检查是否已进入账号、正在更新或出现提示，处理后再点“网页登录”；无需重复启动。");
            if (candidates.Length != 1)
                throw new InvalidOperationException("有多个待登录窗口，请展开“选择登录窗口”指定目标。");
            return await Selected(backend, choice, candidates[0], cancel, progress);
        }

        public static Task<PreparedWebSession> Existing(WebSessionBackend backend, LoginRoute choice) {
            var candidates = backend.Targets();
            if (candidates.Length == 0) throw new InvalidOperationException("请先让战网停在国服登录页，再点“网页登录”。");
            if (candidates.Length != 1) throw new InvalidOperationException("有多个待登录窗口，请展开“选择登录窗口”指定目标。");
            return Selected(backend, choice, candidates[0], CancellationToken.None, s => { });
        }

        public static async Task<PreparedWebSession> Selected(WebSessionBackend backend, LoginRoute choice,
            Target target, CancellationToken cancel, Action<string> progress, int timeoutMs = 30000) {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                deadline.CancelAfter(timeoutMs);
                IDisposable handle = null;
                try {
                    deadline.Token.ThrowIfCancellationRequested();
                    progress("正在准备所选账号的浏览器");
                    await PendingWork.Wait(backend.PrepareBrowser(choice, deadline.Token), deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    handle = await PendingWork.Wait(backend.Attach(target, deadline.Token), deadline.Token, h => h.Dispose());
                    deadline.Token.ThrowIfCancellationRequested();
                    var result = new PreparedWebSession(choice, target, handle);
                    handle = null;
                    return result;
                } catch (OperationCanceledException) {
                    if (cancel.IsCancellationRequested) throw;
                    throw new InvalidOperationException("准备浏览器或登录接管超时，已停止本次准备。请稍后重试，也可改用 Chrome / Edge；原账号资料保留。");
                } finally { if (handle != null) handle.Dispose(); }
            }
        }
    }
}
