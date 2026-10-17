using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using SessionProbe;

namespace BrowserManager {
    public static class Browser {
        public const string Home = "https://account.battlenet.com.cn/login/zh/";
        public static bool ValidId(string id) { return id != null && id.Length == 32 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')); }
        public static string Profile(string root, string id) {
            return Profile(root,id,"chrome");
        }
        public static bool External(string mode) { return mode=="chrome" || mode=="edge"; }
        public static string Label(string mode) { if(mode=="webview2")return "内置登录窗口";if(mode=="chrome")return "Chrome";if(mode=="edge")return "Edge";throw new InvalidOperationException("未知登录方式。"); }
        public static string Profile(string root, string id,string mode) {
            if (!ValidId(id)) throw new InvalidOperationException("账号标识无效，未打开浏览器。");
            if(!External(mode))throw new InvalidOperationException("请选 Chrome 或 Edge。");
            return Path.Combine(Path.GetFullPath(root), "data", "browser", mode, id);
        }
        public static bool Allowed(string url) {
            return url != null && url.Length < 32767 && !url.Any(c => char.IsControl(c) || c == '"') && BrowserProfiles.Allowed(url);
        }
        public static ProcessStartInfo StartInfo(string root, string id, string url) {
            return StartInfo(root,id,url,"chrome");
        }
        public static string Executable(string mode) {
            if(mode=="chrome")return BrowserProfiles.Browser;
            if(mode!="edge")throw new InvalidOperationException("请选 Chrome 或 Edge。");
            foreach(var folder in new[]{Environment.GetEnvironmentVariable("ProgramW6432")??Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}){
                string path=Path.Combine(folder,@"Microsoft\Edge\Application\msedge.exe");if(File.Exists(path))return path;
            }
            throw new InvalidOperationException("未找到 Edge。请安装 Edge，或选择内置登录窗口 / Chrome。");
        }
        public static ProcessStartInfo BuildStartInfo(string root,string id,string url,string mode,string executable) {
            if (!Allowed(url)) throw new InvalidOperationException("收到的地址未通过校验，请从战网重新发起登录。不要复用或发送完整登录网址。");
            return new ProcessStartInfo(executable, "--user-data-dir=" + BrowserProfiles.Quote(Profile(root, id,mode)) + " --no-first-run --no-default-browser-check --new-window " + BrowserProfiles.Quote(url)) { UseShellExecute = false };
        }
        public static ProcessStartInfo StartInfo(string root,string id,string url,string mode) {
            return BuildStartInfo(root,id,url,mode,Executable(mode));
        }
        public static void Open(string root, string id, string url) {
            Open(root,id,url,"chrome");
        }
        public static void Open(string root,string id,string url,string mode) {
            var info = StartInfo(root, id, url,mode);
            Directory.CreateDirectory(Profile(root, id,mode));
            Process.Start(info);
        }
    }
    public sealed class LoginRoute {
        public readonly string Id,Name,Mode;
        public bool IsEmbedded {get{return Mode=="webview2";}}
        public LoginRoute(Account account){if(account==null||!Browser.ValidId(account.Id)||account.Region!="CN"||account.Archived)throw new InvalidOperationException("请先选择正常国服账号。");Id=account.Id;Name=account.Name;Mode=string.IsNullOrEmpty(account.LoginBrowser)?"webview2":account.LoginBrowser;Browser.Label(Mode);}
    }
}
