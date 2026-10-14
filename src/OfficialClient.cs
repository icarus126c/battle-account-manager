using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BrowserManager {
    public sealed class ClientPreference { public int Format {get;set;} public string Executable {get;set;} }
    public static class OfficialClient {
        // Installation path only; account credentials remain in the protected store.
        internal static string SettingsHome;
        static string Home {get{return SettingsHome??DataDirectory.FixedHome;}}
        internal static string PreferencePath(string home){return Path.Combine(home,"data","client-location.json");}
        public static string ReadPreference(string home){
            string file=PreferencePath(home);if(!File.Exists(file))return null;
            try {if(new FileInfo(file).Length>16384)throw new InvalidDataException();var saved=new JavaScriptSerializer().Deserialize<ClientPreference>(File.ReadAllText(file));if(saved==null||saved.Format!=1||string.IsNullOrWhiteSpace(saved.Executable))throw new InvalidDataException();return saved.Executable;}
            catch{throw new InvalidOperationException("战网位置设置无法读取。请打开“环境与战网位置”，重新选择或恢复自动查找。");}
        }
        public static bool IsClient(string path){
            try {
                if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||!string.Equals(Path.GetFileName(path),"Battle.net.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return false;
                var info=FileVersionInfo.GetVersionInfo(path);
                return string.Equals(info.OriginalFilename,"Battle.net.exe",StringComparison.OrdinalIgnoreCase)&&(info.CompanyName??"").IndexOf("Blizzard",StringComparison.OrdinalIgnoreCase)>=0;
            }catch{return false;}
        }
        internal static string Resolve(string configured,IEnumerable<string> candidates,Func<string,bool> valid){
            if(!string.IsNullOrWhiteSpace(configured)){
                if(valid(configured))return Path.GetFullPath(configured);
                throw new InvalidOperationException("已选战网位置不可用，可能已移动或卸载。请在“环境与战网位置”重新选择 Battle.net.exe，或恢复自动查找。");
            }
            foreach(string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))if(valid(candidate))return Path.GetFullPath(candidate);
            throw new InvalidOperationException("未找到官方战网。请安装战网，或在“环境与战网位置”选择安装目录里的 Battle.net.exe。");
        }
        static string RegistryPath(RegistryHive hive,RegistryView view,string key,string value){try{using(var root=RegistryKey.OpenBaseKey(hive,view))using(var sub=root.OpenSubKey(key))return sub==null?null:Convert.ToString(sub.GetValue(value));}catch{return null;}}
        internal static string Candidate(string path){if(string.IsNullOrWhiteSpace(path))return null;path=Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));try{return string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)?path:Path.Combine(path,"Battle.net.exe");}catch{return null;}}
        public static IEnumerable<string> Candidates(){
            var result=new List<string>();
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64}){
                result.Add(Candidate(RegistryPath(hive,view,@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Battle.net.exe","")));
                result.Add(Candidate(RegistryPath(hive,view,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net","InstallLocation")));
                result.Add(Candidate(RegistryPath(hive,view,@"SOFTWARE\Blizzard Entertainment\Battle.net","InstallPath")));
            }
            foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetEnvironmentVariable("ProgramW6432"),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})if(!string.IsNullOrWhiteSpace(root))result.Add(Path.Combine(root,@"Battle.net\Battle.net.exe"));
            foreach(var process in Process.GetProcessesByName("Battle.net"))using(process){try{result.Add(process.MainModule.FileName);}catch{}}
            return result.Where(x=>!string.IsNullOrWhiteSpace(x));
        }
        public static string ClientPath {get{return Resolve(ReadPreference(Home),Candidates(),IsClient);}}
        public static void SavePreference(string home,string path,Func<string,bool> validator=null){
            if(!(validator??IsClient)(path))throw new InvalidOperationException("请选择官方战网安装目录中的 Battle.net.exe，不能选择安装包、快捷方式或游戏程序。");
            Store.WriteAtomic(PreferencePath(home),new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(new ClientPreference{Format=1,Executable=Path.GetFullPath(path)})));
        }
        public static void ClearPreference(string home){string path=PreferencePath(home);if(File.Exists(path))File.Delete(path);}
    }
}
