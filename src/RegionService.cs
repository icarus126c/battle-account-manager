using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace BrowserManager {
    public interface RegionBackend {void Idle();byte[] Read();void Write(byte[] expected,byte[] next);void Save(RegionRecord record);void Applied();RegionRecord Last();void Restored();}
    public static class RegionService {
        public static string Change(RegionBackend backend,bool undo,Func<bool> allowed){
            byte[] before=null,next=null;using(var gate=new Mutex(false,@"Local\BattleAccountManager.NativeConfig")){bool held=false;try{
                try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("另一项战网设置操作仍在进行，请稍后重试。");
                if(!allowed())throw new InvalidOperationException("密码库已锁定，本次未修改。");backend.Idle();before=backend.Read();var record=undo?backend.Last():RegionConfig.Plan(before);
                if(!undo&&!RegionConfig.Needed(record))return "配置已包含国服与外服入口，无需重复修改。请打开官方客户端核对地区选项。";
                next=undo?RegionConfig.Undo(before,record):RegionConfig.Apply(before,record);
                if(!undo)backend.Save(record);
                backend.Idle();if(!allowed())throw new InvalidOperationException("密码库已锁定，本次未修改。");backend.Write(before,next);
                if(undo)backend.Restored();else backend.Applied();
                return undo?"已撤销最近一次区服入口修改。账号选择、保持登录和其他设置均未回退。":"已补齐国服与外服入口，并保留修改前的入口设置。打开战网后，在登录页选择中国／美洲／欧洲／亚洲；是否实际可用请以客户端为准。";
            }finally{if(before!=null)Array.Clear(before,0,before.Length);if(next!=null)Array.Clear(next,0,next.Length);if(held)gate.ReleaseMutex();}}
        }
    }
    public sealed class OfficialRegionBackend:RegionBackend {
        readonly string home,filePath;readonly Action idleOverride;string lastPath;
        static readonly byte[] Entropy=Encoding.UTF8.GetBytes("BattleAccountManager.RegionEntries.v1");
        public OfficialRegionBackend(string dataHome):this(dataHome,ConfigPath,null){}
        internal OfficialRegionBackend(string dataHome,string configuration,Action idle){home=dataHome;filePath=configuration;idleOverride=idle;}
        public static string ConfigPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Battle.net\Battle.net.config");}}
        string Folder {get{return Path.Combine(home,"data","region-backups");}}
        public void Idle(){if(idleOverride!=null){idleOverride();return;}foreach(string name in new[]{"Battle.net","Battle.net Helper","Battle.net Launcher","TcNo-Acc-Switcher"}){var processes=Process.GetProcessesByName(name);try{if(processes.Length!=0)throw new InvalidOperationException("请先正常退出全部战网客户端和切换工具，再修改区服入口。不要退出登录；游戏和更新服务无需关闭。此项不能跳过运行检测。");}finally{foreach(var p in processes)p.Dispose();}}}
        public byte[] Read(){if(!File.Exists(filePath)||new FileInfo(filePath).Length>4*1024*1024)throw new InvalidOperationException("没有可用的战网配置。请先安装并打开一次官方战网，然后正常退出。");return File.ReadAllBytes(filePath);}
        public void Write(byte[] expected,byte[] next){
            string temp=filePath+".region-"+Guid.NewGuid().ToString("N")+".tmp";
            try {using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(next,0,next.Length);file.Flush(true);}Idle();byte[] current=Read();try{if(!current.SequenceEqual(expected))throw new InvalidOperationException("战网配置刚发生变化，本次未覆盖，请重新检查。");}finally{Array.Clear(current,0,current.Length);}File.Replace(temp,filePath,null);byte[] check=Read();try{if(!check.SequenceEqual(next))throw new InvalidOperationException("设置已写入，但随后检测到配置变化，暂时无法确认最终结果。请检查官方战网设置；本次没有自动回退配置。");}finally{Array.Clear(check,0,check.Length);}}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static byte[] Encode(RegionRecord record){byte[] plain=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(record));try{return ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}}
        internal static RegionRecord Decode(byte[] encrypted){if(encrypted.Length>4*1024*1024)throw new InvalidDataException();byte[] plain=ProtectedData.Unprotect(encrypted,Entropy,DataProtectionScope.CurrentUser);try{return new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<RegionRecord>(Encoding.UTF8.GetString(plain));}finally{Array.Clear(plain,0,plain.Length);}}
        public void Save(RegionRecord record){Directory.CreateDirectory(Folder);lastPath=Path.Combine(Folder,DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N")+".pending");byte[] encoded=Encode(record);try{Store.WriteAtomic(lastPath,encoded);}finally{Array.Clear(encoded,0,encoded.Length);}}
        public void Applied(){try{string next=Path.ChangeExtension(lastPath,"bin");File.Move(lastPath,next);lastPath=next;}catch{throw new InvalidOperationException("区服入口已经写入，但撤销记录未能完成标记。原入口设置保存在资料目录 data/region-backups 的 pending 文件中，请保留该目录。");}}
        public RegionRecord Last(){if(!Directory.Exists(Folder))throw new InvalidOperationException("尚无本工具保存的区服入口修改可撤销。");lastPath=Directory.GetFiles(Folder,"*.bin").OrderByDescending(Path.GetFileName,StringComparer.Ordinal).FirstOrDefault();if(lastPath==null)throw new InvalidOperationException("尚无本工具保存的区服入口修改可撤销。");try{return Decode(File.ReadAllBytes(lastPath));}catch{throw new InvalidOperationException("入口设置备份无法读取，当前战网配置未改动。");}}
        public void Restored(){if(lastPath!=null)try{File.Move(lastPath,lastPath+".restored");}catch{/* Preserve the backup; a repeat undo is still guarded by expected values. */}}
    }
}
