using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace BrowserManager {
    public sealed class VaultLockedException : InvalidOperationException {
        public VaultLockedException() : base("密码库已锁定，请先输入主密码解锁。") { }
    }
    public sealed class MasterKey : IDisposable {
        public const int DefaultIterations = 600000;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("BAMLOCK1");
        static readonly byte[] FactorMagic = Encoding.ASCII.GetBytes("BAMLOCK2");
        const int HeaderSize = 64, TagSize = 32, MaxSize = 17 * 1024 * 1024;
        byte[] encryption, authentication, salt; readonly int iterations; bool disposed;
        MasterKey(string password, byte[] saltValue, int rounds) {
            salt = (byte[])saltValue.Clone(); iterations = rounds;
            byte[] derived = null;
            try {
                using (var kdf = new Rfc2898DeriveBytes(password, salt, rounds, HashAlgorithmName.SHA256)) derived = kdf.GetBytes(64);
                encryption = derived.Take(32).ToArray(); authentication = derived.Skip(32).ToArray();
            } finally { if (derived != null) Array.Clear(derived, 0, derived.Length); }
        }
        public static bool Protected(byte[] bytes) { return bytes != null && bytes.Length >= 8 && (bytes.Take(8).SequenceEqual(Magic) || bytes.Take(8).SequenceEqual(FactorMagic)); }
        public static bool HasFactor(byte[] bytes) { return bytes != null && bytes.Length >= 8 && bytes.Take(8).SequenceEqual(FactorMagic); }
        public static bool FileProtected(string path) {
            if (!File.Exists(path)) return false;
            using (var stream = File.OpenRead(path)) { var prefix = new byte[8]; return stream.Read(prefix, 0, 8) == 8 && Protected(prefix); }
        }
        static void Header(byte[] bytes) {
            if (!Protected(bytes) || bytes.Length < HeaderSize + TagSize + 16 || bytes.Length > MaxSize) throw new CryptographicException();
            int count = BitConverter.ToInt32(bytes, 8), length = BitConverter.ToInt32(bytes, 60);
            if (count < 10000 || count > 1200000 || length < 16 || length % 16 != 0 || (long)length + HeaderSize + TagSize != bytes.Length) throw new CryptographicException();
        }
        internal static MasterKey Create(string password, int rounds) {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 10 || password.Length > 256) throw new InvalidOperationException("主密码需要 10–256 个字符，不能全为空白。");
            if (rounds < 10000 || rounds > 1200000) throw new ArgumentOutOfRangeException("rounds");
            byte[] random = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            try { return new MasterKey(password, random, rounds); } finally { Array.Clear(random, 0, random.Length); }
        }
        public static MasterKey Derive(string password, byte[] packet) {
            Header(packet);
            if (password == null || password.Length > 256) throw new CryptographicException();
            byte[] descriptorSalt = packet.Skip(12).Take(32).ToArray();
            try { return new MasterKey(password, descriptorSalt, BitConverter.ToInt32(packet, 8)); } finally { Array.Clear(descriptorSalt, 0, descriptorSalt.Length); }
        }
        static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; int difference = 0; for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i]; return difference == 0; }
        void Active() { if (disposed) throw new VaultLockedException(); }
        public byte[] Seal(byte[] inner, bool factor = false) {
            Active(); if (inner == null || inner.Length > 16 * 1024 * 1024) throw new InvalidDataException();
            byte[] iv = new byte[16], cipher = null, tag = null;
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);
            try {
                using (var aes = Aes.Create()) { aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encryption; aes.IV = iv;
                    using (var transform = aes.CreateEncryptor()) cipher = transform.TransformFinalBlock(inner, 0, inner.Length); }
                var result = new byte[HeaderSize + cipher.Length + TagSize];
                Buffer.BlockCopy(factor ? FactorMagic : Magic, 0, result, 0, 8); Buffer.BlockCopy(BitConverter.GetBytes(iterations), 0, result, 8, 4);
                Buffer.BlockCopy(salt, 0, result, 12, 32); Buffer.BlockCopy(iv, 0, result, 44, 16); Buffer.BlockCopy(BitConverter.GetBytes(cipher.Length), 0, result, 60, 4);
                Buffer.BlockCopy(cipher, 0, result, HeaderSize, cipher.Length);
                using (var hmac = new HMACSHA256(authentication)) tag = hmac.ComputeHash(result, 0, HeaderSize + cipher.Length);
                Buffer.BlockCopy(tag, 0, result, HeaderSize + cipher.Length, TagSize); return result;
            } finally { foreach (var bytes in new[] { iv, cipher, tag }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
        }
        public byte[] Open(byte[] packet) {
            Active(); Header(packet);
            byte[] descriptorSalt = packet.Skip(12).Take(32).ToArray(), computed = null, actual = packet.Skip(packet.Length - TagSize).ToArray(), iv = packet.Skip(44).Take(16).ToArray();
            try {
                if (iterations != BitConverter.ToInt32(packet, 8) || !Equal(salt, descriptorSalt)) throw new CryptographicException();
                using (var hmac = new HMACSHA256(authentication)) computed = hmac.ComputeHash(packet, 0, packet.Length - TagSize);
                // Authenticate the complete header and ciphertext before attempting CBC decryption.
                if (!Equal(computed, actual)) throw new CryptographicException();
                using (var aes = Aes.Create()) { aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encryption; aes.IV = iv;
                    using (var transform = aes.CreateDecryptor()) return transform.TransformFinalBlock(packet, HeaderSize, BitConverter.ToInt32(packet, 60)); }
            } finally { foreach (var bytes in new[] { descriptorSalt, computed, actual, iv }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
        }
        public void Dispose() { if (disposed) return; disposed = true; foreach (var bytes in new[] { encryption, authentication, salt }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
    }
    public partial class Store {
        readonly object protectionSync = new object(); MasterKey masterKey;
        internal int PasswordIterations = MasterKey.DefaultIterations;
        internal Func<DateTime> Now = () => DateTime.UtcNow;
        int unlockFailures; DateTime blockedUntil;
        public bool IsProtected { get { return MasterKey.FileProtected(FilePath); } }
        public bool NeedsFactor { get { if (!File.Exists(FilePath)) return false; using (var file = File.OpenRead(FilePath)) { var prefix = new byte[8]; return file.Read(prefix, 0, 8) == 8 && MasterKey.HasFactor(prefix); } } }
        public bool IsLocked { get { lock (protectionSync) return IsProtected && masterKey == null; } }
        public void Lock() { lock (protectionSync) { if (masterKey != null) masterKey.Dispose(); masterKey = null; } }
        static byte[] ReadProtected(string path) { if (new FileInfo(path).Length > 17 * 1024 * 1024) throw new InvalidDataException(); return File.ReadAllBytes(path); }
        Data DecodeActive(byte[] bytes) {
            if (!MasterKey.Protected(bytes)) return Decode(bytes);
            if (masterKey == null) throw new VaultLockedException();
            byte[] inner = masterKey.Open(bytes); try { var result = ImportBytes(inner); CheckFactorHeader(bytes, result); return result; } finally { Array.Clear(inner, 0, inner.Length); }
        }
        static void CheckFactorHeader(byte[] packet, Data data) { if (MasterKey.HasFactor(packet) != (data.Security != null)) throw new CryptographicException(); }
        static Data ImportBytes(byte[] bytes) {
            if (bytes.Take(8).SequenceEqual(header)) return Decode(bytes);
            if (bytes.Take(8).SequenceEqual(oldPasswordHeader)) return DecodeLegacy(bytes, true);
            return DecodeLegacy(bytes, false);
        }
        public Data Unlock(string password, string factor = null) {
            lock (protectionSync) {
                if (Now() < blockedUntil) throw new InvalidOperationException("尝试次数过多，请等待 30 秒再试。");
                byte[] packet = null, inner = null; MasterKey candidate = null;
                try {
                    packet = ReadProtected(FilePath); candidate = MasterKey.Derive(password, packet); inner = candidate.Open(packet);
                    var result = ImportBytes(inner); CheckFactorHeader(packet, result); Totp.Verify(result.Security, factor, Now());
                    if (result.Security != null) { byte[] updated = Encode(result); try { WriteWithoutHistory(FilePath, candidate.Seal(updated, true)); } finally { Array.Clear(updated, 0, updated.Length); } }
                    if (masterKey != null) masterKey.Dispose(); masterKey = candidate; candidate = null; unlockFailures = 0; blockedUntil = DateTime.MinValue; return result;
                } catch { if (++unlockFailures >= 5) { blockedUntil = Now().AddSeconds(30); unlockFailures = 0; } throw new InvalidOperationException("无法解锁：请核对主密码及动态码／恢复码；同一动态码只能使用一次，请等下一组。连续失败后暂停 30 秒。资料读取或保存失败时也不会解锁。"); }
                finally { if (candidate != null) candidate.Dispose(); foreach (var bytes in new[] { packet, inner }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
            }
        }
        public byte[] Export(Data data) {
            lock (protectionSync) {
                if (IsLocked) throw new VaultLockedException();
                if (IsProtected && masterKey != null) {
                    byte[] current = ReadProtected(FilePath), verified = null;
                    try { verified = masterKey.Open(current); var saved = ImportBytes(verified); CheckFactorHeader(current, saved);
                        if (!SameFactor(saved.Security, data.Security)) throw new InvalidOperationException("两步验证设置只能通过保护设置更改。"); }
                    finally { Array.Clear(current, 0, current.Length); if (verified != null) Array.Clear(verified, 0, verified.Length); }
                }
                if (masterKey == null && data.Security != null) throw new InvalidOperationException("两步验证需要主密码保护。");
                byte[] inner = Encode(data); if (masterKey == null) return inner;
                try { return masterKey.Seal(inner, data.Security != null); } finally { Array.Clear(inner, 0, inner.Length); }
            }
        }
        static bool SameFactor(FactorSettings a, FactorSettings b) { return a == null ? b == null : b != null && a.Secret == b.Secret && a.LastStep == b.LastStep && a.RecoveryHashes.SequenceEqual(b.RecoveryHashes); }
        static void WriteWithoutHistory(string path, byte[] bytes) {
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(full)) ReplaceFile(temporary, full, null); else File.Move(temporary, full);
            } finally { Array.Clear(bytes, 0, bytes.Length); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static void ReplaceFile(string temporary, string target, string backup) {
            for (int attempt = 0; ; attempt++) {
                try { File.Replace(temporary, target, backup); return; }
                catch (IOException) { if (attempt >= 5 || !File.Exists(temporary)) throw; System.Threading.Thread.Sleep(50 * (attempt + 1)); }
            }
        }
        void ReplaceProtection(Data data, MasterKey next) {
            byte[] inner = null, before = null, packet = null;
            try {
                inner = Encode(data); packet = next.Seal(inner, data.Security != null);
                if (File.Exists(FilePath)) {
                    before = ReadProtected(FilePath);
                    if (MasterKey.Protected(before)) { if (masterKey == null) throw new VaultLockedException(); var raw = masterKey.Open(before); Array.Clear(before, 0, before.Length); before = raw; }
                } else before = (byte[])inner.Clone();
                string backup = FilePath + ".bak";
                if (File.Exists(backup)) {
                    byte[] history = ReadProtected(backup);
                    try { WriteWithoutHistory(Path.Combine(Home, "data", "protected-history", Guid.NewGuid().ToString("N") + ".bav"), MasterKey.Protected(history) ? (byte[])history.Clone() : next.Seal(history)); }
                    finally { Array.Clear(history, 0, history.Length); }
                }
                // Protect the current backup before atomically publishing the new main vault.
                WriteWithoutHistory(backup, next.Seal(inner, data.Security != null)); WriteWithoutHistory(FilePath, packet); packet = null;
            } finally { foreach (var bytes in new[] { inner, before, packet }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
        }
        public void EnableProtection(Data data, string password) {
            lock (protectionSync) {
                if (IsProtected) throw new InvalidOperationException("密码库已设置主密码，请使用修改主密码。");
                MasterKey next = MasterKey.Create(password, PasswordIterations);
                try { ReplaceProtection(data, next); if (masterKey != null) masterKey.Dispose(); masterKey = next; next = null; }
                finally { if (next != null) next.Dispose(); }
            }
        }
        public void ChangePassword(Data data, string currentPassword, string newPassword, string factor = null) {
            lock (protectionSync) {
                if (!IsProtected) throw new InvalidOperationException("请先设置主密码。");
                var verified = Unlock(currentPassword, factor); data.Security = verified.Security == null ? null : verified.Security.Copy();
                MasterKey next = MasterKey.Create(newPassword, PasswordIterations);
                try { ReplaceProtection(data, next); masterKey.Dispose(); masterKey = next; next = null; }
                finally { if (next != null) next.Dispose(); }
            }
        }
        public Data ReadBackup(string path, string password = null, string factor = null) {
            lock (protectionSync) {
                if (IsLocked) throw new VaultLockedException();
                byte[] bytes = ReadProtected(path), inner = null; MasterKey temporary = null;
                try {
                    if (!MasterKey.Protected(bytes)) return ImportBytes(bytes);
                    if (password != null) temporary = MasterKey.Derive(password, bytes);
                    var key = temporary ?? masterKey; if (key == null) throw new VaultLockedException();
                    try { inner = key.Open(bytes); } catch (CryptographicException) { if (password == null) throw new VaultLockedException(); throw; }
                    var result = ImportBytes(inner); CheckFactorHeader(bytes, result);
                    if (result.Security != null && factor == null) throw new VaultLockedException();
                    Totp.Verify(result.Security, factor, Now()); return result;
                } finally { if (temporary != null) temporary.Dispose(); foreach (var value in new[] { bytes, inner }) if (value != null) Array.Clear(value, 0, value.Length); }
            }
        }
    }
}
