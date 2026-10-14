using System;
using System.Threading;
using System.Threading.Tasks;

namespace BrowserManager {
    // Stop waiting promptly even when a third-party API cannot cancel its work.
    // Observe late errors and release late resources instead of publishing stale results.
    internal static class PendingWork {
        public static async Task Wait(Task work, CancellationToken cancel) {
            await Wait(AsResult(work), cancel);
        }
        static async Task<bool> AsResult(Task work) { await work; return true; }
        public static async Task<T> Wait<T>(Task<T> work, CancellationToken cancel, Action<T> discard = null) {
            var stopped = new TaskCompletionSource<bool>();
            using (cancel.Register(() => stopped.TrySetResult(true))) {
                await Task.WhenAny(work, stopped.Task);
                if (cancel.IsCancellationRequested) {
                    ObserveLate(work, discard);
                    cancel.ThrowIfCancellationRequested();
                }
                return await work;
            }
        }
        static async void ObserveLate<T>(Task<T> work, Action<T> discard) {
            try { var value = await work.ConfigureAwait(false); if (discard != null) discard(value); } catch { }
        }
    }
}
