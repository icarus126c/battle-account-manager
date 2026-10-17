using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
namespace BrowserManager {
    public partial class Store {
        public Data EnableTwoFactor(string password, string secret, string confirmation, string[] recoveryCodes) {
            lock (protectionSync) {
                if (IsLocked || !IsProtected) throw new VaultLockedException();
                var current = Unlock(password);
                if (current.Security != null) throw new InvalidOperationException("两步验证已经开启。");
                long step = Totp.Match(secret, confirmation, Now()); if (step < 0) throw new InvalidOperationException("请核对验证器中的 6 位动态码，手机与电脑需使用准确时间。");
                if (recoveryCodes == null || recoveryCodes.Length != 8 || recoveryCodes.Any(c => c == null || c.Replace("-", "").Length != 20)) throw new InvalidOperationException("恢复码无效。");
                current.Security = new FactorSettings { Secret = secret, LastStep = step, RecoveryHashes = recoveryCodes.Select(c => Totp.RecoveryHash(secret, c)).ToList() }; current.Security.Validate();
                ReplaceProtection(current, masterKey); return current;
            }
        }
        public Data DisableTwoFactor(string password, string factor) {
            lock (protectionSync) {
                if (IsLocked || !IsProtected) throw new VaultLockedException();
                var current = Unlock(password, factor); current.Security = null; ReplaceProtection(current, masterKey); return current;
            }
        }
        public Data RemoveProtection(string password, string factor = null) {
            lock (protectionSync) {
                if (IsLocked || !IsProtected) throw new VaultLockedException();
                var current = Unlock(password, factor); current.Security = null;
                string history = Path.Combine(Home, "data", "protected-history");
                WriteWithoutHistory(Path.Combine(history, Guid.NewGuid().ToString("N") + ".bav"), ReadProtected(FilePath));
                if (File.Exists(FilePath + ".bak")) WriteWithoutHistory(Path.Combine(history, Guid.NewGuid().ToString("N") + ".bav"), ReadProtected(FilePath + ".bak"));
                // Removing the master layer keeps Windows-user encryption and account identifiers.
                WriteWithoutHistory(FilePath + ".bak", Encode(current)); WriteWithoutHistory(FilePath, Encode(current));
                Lock(); return current;
            }
        }
    }
}
