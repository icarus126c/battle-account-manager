using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace BrowserManager {
    public class EmbeddedSessions : IDisposable {
        readonly string home;
        readonly Window owner;
        readonly Action<string> notice;
        readonly Func<Task<CoreWebView2Environment>> createEnvironment;
        readonly Dictionary<string, EmbeddedWindow> windows = new Dictionary<string, EmbeddedWindow>();
        readonly SemaphoreSlim preparationGate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        Task<CoreWebView2Environment> environment;
        bool disposed;

        public EmbeddedSessions(string path, Window parent, Action<string> report = null)
            : this(path, parent, report, null) { }
        internal EmbeddedSessions(string path, Window parent, Action<string> report,
            Func<Task<CoreWebView2Environment>> factory) {
            home = path; owner = parent; notice = report;
            createEnvironment = factory ?? CreateEnvironment;
        }
        Task<CoreWebView2Environment> CreateEnvironment() {
            EnvironmentCheck.RequireWebView();
            Directory.CreateDirectory(EmbeddedPolicy.DataRoot(home));
            return CoreWebView2Environment.CreateAsync(null, EmbeddedPolicy.DataRoot(home), EmbeddedPolicy.Options());
        }
        Task<CoreWebView2Environment> EnvironmentTask() {
            if (environment == null || environment.IsFaulted || environment.IsCanceled) {
                environment = createEnvironment();
            }
            return environment;
        }
        public async Task<EmbeddedWindow> Prepare(string id, string name, CancellationToken cancel = default(CancellationToken)) {
            if (disposed) throw new ObjectDisposedException("EmbeddedSessions");
            EmbeddedPolicy.Profile(id);
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancel, lifetime.Token)) {
                operation.CancelAfter(30000);
                var token = operation.Token;
                if (!await preparationGate.WaitAsync(0, token))
                    throw new InvalidOperationException("上一次内置窗口准备仍在收尾，请稍后再试。");
                EmbeddedWindow created = null;
                try {
                    token.ThrowIfCancellationRequested();
                    EmbeddedWindow existing;
                    if (windows.TryGetValue(id, out existing) && !existing.ClosedAlready) {
                        await PendingWork.Wait(existing.Ready, token);
                        token.ThrowIfCancellationRequested();
                        existing.Show(); existing.Activate();
                        return existing;
                    }
                    var env = await PendingWork.Wait(EnvironmentTask(), token);
                    token.ThrowIfCancellationRequested();
                    created = new EmbeddedWindow(env, id, name, owner, false, notice);
                    windows[id] = created;
                    created.Show();
                    await PendingWork.Wait(created.Initialize(), token);
                    token.ThrowIfCancellationRequested();
                    if (created.ClosedAlready) throw new InvalidOperationException("内置登录窗口已关闭，请重新准备。");
                    created.Wait();
                    var result = created; created = null;
                    return result;
                } catch (OperationCanceledException) {
                    if (cancel.IsCancellationRequested || lifetime.IsCancellationRequested) throw;
                    throw new InvalidOperationException("内置浏览器准备超时，请稍后重试或选择 Chrome / Edge。原浏览器资料保留。");
                } finally {
                    // Close only the window created by this operation, never delete the profile.
                    if (created != null) {
                        EmbeddedWindow current;
                        if (windows.TryGetValue(id, out current) && ReferenceEquals(current, created)) windows.Remove(id);
                        if (!created.ClosedAlready) created.Close();
                    }
                    preparationGate.Release();
                }
            }
        }
        public async Task Open(string id, string name, string url) {
            if (!Browser.Allowed(url)) throw new InvalidOperationException("国服新请求未通过校验，请重新从战网发起登录。");
            var window = await Prepare(id, name);
            if (disposed) throw new ObjectDisposedException("EmbeddedSessions");
            window.Navigate(url, url != Browser.Home);
        }
        public void OpenReady(string id, string url) {
            if (disposed) throw new ObjectDisposedException("EmbeddedSessions");
            if (!Browser.Allowed(url)) throw new InvalidOperationException("国服新请求未通过校验，请重新发起登录。");
            EmbeddedWindow window;
            if (!windows.TryGetValue(id, out window) || window.ClosedAlready || !window.IsReady)
                throw new InvalidOperationException("内置登录窗口已关闭，请重新准备接管并从战网发起新登录。");
            window.Navigate(url, true); window.Show(); window.Activate();
        }
        public void Dispose() {
            if (disposed) return;
            disposed = true; lifetime.Cancel();
            foreach (var window in windows.Values.ToArray()) if (!window.ClosedAlready) window.Close();
            windows.Clear();
        }
    }
}
