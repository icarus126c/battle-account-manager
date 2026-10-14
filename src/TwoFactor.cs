using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BrowserManager {
    public sealed class FactorSettings {
        public string Secret { get; set; }
        public long LastStep { get; set; }
        public List<string> RecoveryHashes { get; set; }
        public FactorSettings Copy() { return new FactorSettings { Secret = Secret, LastStep = LastStep, RecoveryHashes = RecoveryHashes.ToList() }; }
        public void Validate() {
            if (Secret == null || Secret.Length != 32 || !Secret.All(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".Contains(c)) || LastStep < -1 || RecoveryHashes == null || RecoveryHashes.Count > 8 || RecoveryHashes.Distinct().Count() != RecoveryHashes.Count || RecoveryHashes.Any(h => h == null || h.Length != 64 || !h.All(c => "0123456789ABCDEF".Contains(c)))) throw new InvalidDataException("两步验证资料无法读取。");
        }
    }
    public static class Totp {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long Step(DateTime now) { return (long)(now.ToUniversalTime() - Epoch).TotalSeconds / 30; }
        public static string NewSecret() { byte[] bytes = Random(20); try { return Base32(bytes); } finally { Array.Clear(bytes, 0, bytes.Length); } }
        static byte[] Random(int length) { var bytes = new byte[length]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return bytes; }
        public static string Base32(byte[] bytes) { var text = new StringBuilder(); int value = 0, bits = 0; foreach (byte b in bytes) { value = (value << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; text.Append(Alphabet[(value >> bits) & 31]); } } if (bits > 0) text.Append(Alphabet[(value << (5 - bits)) & 31]); return text.ToString(); }
        static byte[] Decode(string text) { var result = new List<byte>(); int value = 0, bits = 0; foreach (char c in text) { int n = Alphabet.IndexOf(c); if (n < 0) throw new InvalidDataException(); value = (value << 5) | n; bits += 5; if (bits >= 8) { bits -= 8; result.Add((byte)(value >> bits)); } } return result.ToArray(); }
        public static string Code(string secret, long step, int digits = 6) {
            byte[] key = Decode(secret), counter = new byte[8], hash = null;
            try { for (int i = 7; i >= 0; i--) { counter[i] = (byte)step; step >>= 8; } using (var hmac = new HMACSHA1(key)) hash = hmac.ComputeHash(counter);
                int offset = hash[hash.Length - 1] & 15; int number = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
                return (number % (digits == 8 ? 100000000 : 1000000)).ToString(digits == 8 ? "D8" : "D6", CultureInfo.InvariantCulture);
            } finally { foreach (var bytes in new[] { key, counter, hash }) if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
        }
        static bool Equal(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0; }
        public static long Match(string secret, string input, DateTime now, long after = -1) {
            string code = (input ?? "").Trim(); if (code.Length != 6 || !code.All(c => c >= '0' && c <= '9')) return -1;
            long current = Step(now); foreach (long step in new[] { current, current - 1, current + 1 }) if (step >= 0 && step > after && Equal(Code(secret, step), code)) return step; return -1;
        }
        public static string RecoveryHash(string secret, string code) {
            string normalized = (code ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + ":" + normalized))).Replace("-", "");
        }
        public static string[] NewRecoveryCodes() { return Enumerable.Range(0, 8).Select(i => { byte[] raw = Random(10); try { string value = BitConverter.ToString(raw).Replace("-", ""); return value.Substring(0, 5) + "-" + value.Substring(5, 5) + "-" + value.Substring(10, 5) + "-" + value.Substring(15, 5); } finally { Array.Clear(raw, 0, raw.Length); } }).ToArray(); }
        public static void Verify(FactorSettings settings, string code, DateTime now) {
            if (settings == null) return; settings.Validate(); long step = Match(settings.Secret, code, now, settings.LastStep);
            if (step >= 0) { settings.LastStep = step; return; }
            string hash = RecoveryHash(settings.Secret, code); int found = settings.RecoveryHashes.FindIndex(h => Equal(h, hash));
            if (found >= 0) { settings.RecoveryHashes.RemoveAt(found); return; }
            throw new InvalidOperationException("动态码不正确、已使用或已过期。请等下一组动态码，或使用备用恢复码。");
        }
    }
}
