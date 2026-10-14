using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;


using BrowserManager;

namespace CnRecheck {
    public interface RollbackGuard { void RollbackIdle(); void RollbackWrite(byte[] bytes); }
    public class Client : NativeClientBackend, RollbackGuard {
        readonly OfficialNativeClient original = new OfficialNativeClient();
        readonly bool skipRunningCheck;
        public Client(bool skipRunningCheck = false) { this.skipRunningCheck = skipRunningCheck; }
        public string Version { get { return original.Version; } }
        public byte[] ReadConfig() { return original.ReadConfig(); }
        protected virtual bool Running() {
            foreach (string name in new[] { "Battle.net", "Battle.net Helper", "Battle.net Launcher" }) {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length > 0) return true; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return false;
        }
        public void RollbackIdle() { if (Running()) throw new InvalidOperationException("已有战网仍运行，不自动回写旧配置。加密备份已保留。"); }
        public void Idle() { if (!skipRunningCheck && Running()) throw new InvalidOperationException("请先正常退出战网；若要保留已有窗口测试多开，请勾选跳过运行检测。"); }
        public void WriteConfig(byte[] bytes) { Write(bytes, false); }
        public void RollbackWrite(byte[] bytes) { Write(bytes, true); }
        void Write(byte[] bytes, bool rollback) {
            if (rollback) RollbackIdle(); else Idle();
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Battle.net\Battle.net.config");
            string temp = path + ".cn-trial-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (rollback) RollbackIdle(); else Idle(); File.Replace(temp, path, null);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Launch() { Idle(); using (var process = Process.Start(new ProcessStartInfo(OfficialNativeClient.ClientPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(OfficialNativeClient.ClientPath) })) { } }
    }

}
