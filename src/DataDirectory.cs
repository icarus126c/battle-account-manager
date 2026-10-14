using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Management;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public sealed class DirectoryInfoRecord { public int Format {get;set;} public string Source {get;set;} public DateTime CreatedUtc {get;set;} public int Files {get;set;} }
    public static class DataDirectory {
        public static string FixedHome { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BattleAccountManager"); } }
        public static bool HasVault(string root) { return File.Exists(Path.Combine(root,"data","vault-v2.bin")) || File.Exists(Path.Combine(root,"data","accounts.bin")); }
        public static bool Ready(string root) {
            string marker=Path.Combine(root,"directory-info.json");if(!File.Exists(marker))return false;
            try { var info=new JavaScriptSerializer().Deserialize<DirectoryInfoRecord>(File.ReadAllText(marker));if(info==null||info.Format!=1||!HasVault(root))throw new InvalidDataException();return true; }
            catch {throw new InvalidOperationException("固定资料目录无法通过检查，原资料未改动。请保留整个目录，通过备份恢复资料。");}
        }
        public static string Canonical(string path) { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar); }
        public static bool Inside(string root,string path) { return Canonical(path).StartsWith(Canonical(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase); }
        static void PlainPath(string path) { if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("资料目录包含链接，未自动复制，原文件保留。"); }
        public static SortedDictionary<string,string> Inventory(string root) {
            var result=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string name in new[]{"data","cn-exp"}) { string folder=Path.Combine(root,name);if(!Directory.Exists(folder))continue;Walk(root,folder,result); }
            return result;
        }
        static void Walk(string root,string folder,SortedDictionary<string,string> result) {
            PlainPath(folder);foreach(string file in Directory.GetFiles(folder)){PlainPath(file);string relative=file.Substring(Canonical(root).Length+1);if(!Inside(root,file)||result.ContainsKey(relative))throw new InvalidDataException();result.Add(relative,file);}
            foreach(string child in Directory.GetDirectories(folder))Walk(root,child,result);
        }
        public static string Digest(string path) { using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)); }
        public static void ProfileIdle(string home) {
            string root=Canonical(Path.Combine(home,"data","browser"));if(!Directory.Exists(root))return;
            try { using(var search=new ManagementObjectSearcher("SELECT Name, CommandLine FROM Win32_Process WHERE Name='chrome.exe' OR Name='msedge.exe' OR Name='msedgewebview2.exe'"))using(var rows=search.Get())foreach(ManagementObject row in rows)using(row){string command=Convert.ToString(row["CommandLine"]);if(command.Replace('/','\\').IndexOf(root,StringComparison.OrdinalIgnoreCase)>=0)throw new InvalidOperationException("请正常关闭管家打开的 Chrome、Edge 和内置登录窗口后重试，以保留完整网页状态。普通浏览器、战网和游戏无需退出。");} }
            catch(InvalidOperationException){throw;}catch{throw new InvalidOperationException("暂时无法检查专用浏览器是否退出，本次未迁移／恢复资料。请稍后重试，原文件保留。");}
        }
        public static void CopyVerified(string source,string target,Action<string> progress=null,Action<string> idle=null) {
            if(idle!=null)idle(source);var files=Inventory(source);var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int count=0;
            foreach(var pair in files){string output=Path.Combine(target,pair.Key);if(!Inside(target,output))throw new InvalidDataException();Directory.CreateDirectory(Path.GetDirectoryName(output));
                using(var input=new FileStream(pair.Value,FileMode.Open,FileAccess.Read,FileShare.None))using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(destination);destination.Flush(true);}
                File.SetLastWriteTimeUtc(output,File.GetLastWriteTimeUtc(pair.Value));string hash=Digest(output);if(hash!=Digest(pair.Value))throw new IOException("资料复制校验失败。");hashes.Add(pair.Key,hash);count++;
                if(progress!=null&&count%50==0)progress("复制并校验资料 "+count+" / "+files.Count);
            }
            if(idle!=null)idle(source);var after=Inventory(source);if(!after.Keys.SequenceEqual(files.Keys))throw new IOException("复制期间源资料发生变化。");
            foreach(var pair in after)if(hashes[pair.Key]!=Digest(pair.Value))throw new IOException("复制期间源资料发生变化。");
        }
        public static string Ensure(string appRoot,string fixedHome=null,Action<string> progress=null,Action<string> idle=null) {
            string target=Canonical(fixedHome??FixedHome);if(Ready(target))return target;
            if(Directory.Exists(target))throw new InvalidOperationException("固定目录已存在但尚未完成迁移，本次未覆盖。请保留现有目录并查看资料位置。");
            string source=Store.ResolveLegacyHome(appRoot);if(Inside(source,target)||Inside(target,source)||Canonical(source)==target)throw new InvalidOperationException("源目录与目标目录重叠，未迁移。");
            string stage=target+".stage-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(stage);
            try { if(progress!=null)progress("保留原目录，准备复制账号和登录资料");
                if(HasVault(source))CopyVerified(source,stage,progress,idle??ProfileIdle);
                else {var initial=new Store(stage);initial.Save(Store.Initial());}
                if(!HasVault(stage))throw new IOException("没有复制到账号资料，原目录保留。");
                var record=new DirectoryInfoRecord{Format=1,Source=HasVault(source)?Canonical(source):"",CreatedUtc=DateTime.UtcNow,Files=Inventory(stage).Count};
                File.WriteAllText(Path.Combine(stage,"directory-info.json"),new JavaScriptSerializer().Serialize(record),new UTF8Encoding(false));
                Directory.CreateDirectory(Path.GetDirectoryName(target));Directory.Move(stage,target);if(progress!=null)progress("资料复制及校验完成，原目录仍保留");return target;
            } catch {CleanupStage(target,stage);throw;}
        }
        public static void CleanupStage(string target,string stage) {
            string canonical=Canonical(stage),expected=Canonical(target)+".stage-";
            if(!canonical.StartsWith(expected,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(canonical)!=Path.GetDirectoryName(Canonical(target)))throw new InvalidDataException();
            if(Directory.Exists(canonical)){foreach(string path in Directory.GetDirectories(canonical,"*",SearchOption.AllDirectories))PlainPath(path);PlainPath(canonical);Directory.Delete(canonical,true);}
        }
    }
    public sealed class DataSetupWindow:Window {
        public string Home;readonly TextBlock message=Ui.Text("正在检查原账号资料…",14);bool working=true;
        public DataSetupWindow(string root){Title="准备账号资料";Width=540;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterScreen;ResizeMode=ResizeMode.NoResize;
            var panel=new StackPanel();panel.Children.Add(Ui.Text("整理资料目录",24));panel.Children.Add(message);panel.Children.Add(Ui.Text("复制并核对完成后才启用新目录，旧目录和原文件保留。",12));Content=new Border{Padding=new Thickness(28),Child=panel};
            Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(working)e.Cancel=true;};
            Loaded+=async delegate{try{Home=await Task.Run(()=>DataDirectory.Ensure(root,null,value=>Dispatcher.BeginInvoke(new Action(()=>message.Text=value))));working=false;DialogResult=true;}catch(Exception e){working=false;MessageBox.Show(this,e is InvalidOperationException?e.Message:"资料复制未完成，原文件已保留，请关闭专用浏览器后重试。","资料未迁移");DialogResult=false;}};
        }
    }
}
