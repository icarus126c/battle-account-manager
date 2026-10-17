using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Windows;

namespace BrowserManager {
    public static class RegionTests {
        sealed class Fake:RegionBackend {
            public byte[] Bytes;public RegionRecord Backup;public int Writes,Saves,Marked,Undone,Checks;public bool Running,Drift,FailBackup;
            public void Idle(){Checks++;if(Running)throw new InvalidOperationException("FAKE_RUNNING");}public byte[] Read(){return (byte[])Bytes.Clone();}
            public void Write(byte[] expected,byte[] next){if(Drift||!expected.SequenceEqual(Bytes))throw new InvalidOperationException("FAKE_DRIFT");Writes++;Bytes=(byte[])next.Clone();}
            public void Save(RegionRecord value){if(FailBackup)throw new IOException();Saves++;Backup=value;}public void Applied(){Marked++;}public RegionRecord Last(){if(Backup==null)throw new InvalidOperationException();return Backup;}public void Restored(){Undone++;}
        }
        static byte[] B(string text){return new UTF8Encoding(false,true).GetBytes(text);}
        static string S(byte[] bytes){return new UTF8Encoding(false,true).GetString(bytes);}
        static bool Reject(Action run){try{run();return false;}catch{return true;}}
        public static void ReadOnly(string report){byte[] bytes=new OfficialRegionBackend("unused").Read();try{var plan=RegionConfig.Plan(bytes);byte[] next=RegionConfig.Apply(bytes,plan),restored=RegionConfig.Undo(next,plan);try{if(!bytes.SequenceEqual(restored))throw new Exception("Round-trip failed");File.WriteAllLines(report,new[]{"只读预演通过：本机配置已识别，层数="+plan.Entries.Count,"只在内存中生成入口修改并撤销，原文逐字节一致。","未写入真实战网配置、未保存账号标识、未启动客户端。"},new UTF8Encoding(true));}finally{Array.Clear(next,0,next.Length);Array.Clear(restored,0,restored.Length);}}finally{Array.Clear(bytes,0,bytes.Length);}}
        public static void Run(string report){var lines=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);lines.Add("通过："+label);};
            string sample="\uFEFF{\r\n \"Client\":{\"AutoLogin\":true, \"SavedAccountNames\" : \"FAKE_ACCOUNT\",\"Language\":\"zhCN\"},\r\n \"fake_profile\":{\"Client\":{\"LoginSettings\":{\"AllowedRegions\":\"CN\",\"AllowedLocales\":\"zhCN\",\"Extra\":[{\"Ignore\":true}]}},\"Services\":{\"LastLoginRegion\":\"CN\",\"LastLoginAddress\":\"FAKE_HOST\"}},\r\n \"Unrelated\":{\"AllowedRegions\":\"do-not-touch\",\"Number\":999999999999999999999,\"Null\":null},\"String\":\"quoted \\\"AllowedRegions\\\" and \\\\ slash\"\r\n}";
            var before=B(sample);var record=RegionConfig.Plan(before);var after=RegionConfig.Apply(before,record);var parsed=RegionConfig.Plan(after);
            check(record.Entries.Count==2&&record.Entries[0].HadSettings==false,"识别根层与设备层设置，支持根层尚无 LoginSettings");
            check(parsed.Entries.All(e=>new[]{"CN","US","EU","KR"}.All(r=>e.RegionsBefore.Split(';').Contains(r))),"国服配置补齐全部四个登录地区");
            check(parsed.Entries.All(e=>new[]{"zhCN","enUS","zhTW"}.All(r=>e.LocalesBefore.Split(';').Contains(r))),"补充登录语言选项");
            check(S(after).Contains("\"SavedAccountNames\" : \"FAKE_ACCOUNT\"")&&S(after).Contains("\"AutoLogin\":true")&&S(after).Contains("\"Language\":\"zhCN\""),"记住的账号、保持登录与当前语言逐字保留");
            check(S(after).Contains("\"LastLoginRegion\":\"CN\",\"LastLoginAddress\":\"FAKE_HOST\""),"当前登录区服与服务器地址不变");
            check(S(after).Contains("\"AllowedRegions\":\"do-not-touch\"")&&S(after).Contains("999999999999999999999"),"不修改其他位置的同名字段或大整数");
            check(S(after).StartsWith("\uFEFF")&&S(after).Contains("\r\n"),"保留 UTF-8 标记和原换行");
            check(RegionConfig.Undo(after,record).SequenceEqual(before),"撤销新增与已有字段后，原配置逐字节恢复");
            check(!RegionConfig.Needed(parsed)&&RegionConfig.Apply(after,parsed).SequenceEqual(after),"重复启用不重复增加选项");
            var foreign=B("{\"Client\":{\"LoginSettings\":{\"AllowedRegions\":\"US;EU;KR;XX\",\"AllowedLocales\":\"deDE;enUS\"}}}");var international=RegionConfig.Plan(RegionConfig.Apply(foreign,RegionConfig.Plan(foreign))).Entries[0];check(international.RegionsBefore=="US;EU;KR;XX;CN"&&international.LocalesBefore=="deDE;enUS;zhCN;zhTW","外服配置加入中国并保留原有和未知地区、语言");
            foreach(string source in new[]{"{\"Client\":{\"LoginSettings\":{}}}","{\"Client\":{\"LoginSettings\":{\"AllowedRegions\":\"\"}}}","{\"Client\":{\"LoginSettings\":{\"AllowedLocales\":\"\"}}}","{\"Client\":{\"LoginSettings\":{\"AllowedRegions\":\"\",\"AllowedLocales\":\"\"}}}"}){byte[] input=B(source);var plan=RegionConfig.Plan(input);check(RegionConfig.Undo(RegionConfig.Apply(input,plan),plan).SequenceEqual(input),"缺少字段／空值可以启用并原样撤销");}
            byte[] changed=B(S(after).Replace("FAKE_ACCOUNT","FAKE_NEW_ACCOUNT").Replace("\"AutoLogin\":true","\"AutoLogin\":false"));string undone=S(RegionConfig.Undo(changed,record));check(undone.Contains("FAKE_NEW_ACCOUNT")&&undone.Contains("\"AutoLogin\":false"),"撤销时保留之后的账号和保持登录变更");
            var added=new ConfigDocument(after);byte[] withSetting=added.Set(added.Member(added.Client(null),"LoginSettings"),"NewOption","true");string retained=S(RegionConfig.Undo(withSetting,record));check(retained.Contains("\"NewOption\":true")&&!retained.Contains("CN;US;EU;KR"),"撤销新建设置层时保留后来新增的其他选项");
            check(Reject(()=>RegionConfig.Undo(B(S(after).Replace("CN;US;EU;KR","CN;KR")),record)),"入口被外部更改后拒绝覆盖式撤销");
            check(Reject(()=>RegionConfig.Apply(B(sample.Replace("\"AllowedRegions\":\"CN\"","\"AllowedRegions\":\"US\"")),record)),"计划生成后字段变化时拒绝写入");
            foreach(string invalid in new[]{"{}","{\"Client\":null}","{\"Client\":{\"LoginSettings\":[]}}","{\"Client\":{\"LoginSettings\":{\"AllowedRegions\":null}}}","{\"Client\":{},\"Client\":{}}","{\"Client\":{},\"Clien\\u0074\":{}}","{\"Client\":{},}","{\"Client\":{},\"bad\":01}"})check(Reject(()=>RegionConfig.Plan(B(invalid))),"未知结构、重复键或损坏 JSON 停止修改");
            byte[] encrypted=OfficialRegionBackend.Encode(record);var decoded=OfficialRegionBackend.Decode(encrypted);check(RegionConfig.Undo(after,decoded).SequenceEqual(before),"入口备份加密后可解密并正确撤销");check(!S(Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(record))).Contains("FAKE_ACCOUNT"),"备份只保存入口字段，不包含记住的账号或认证资料");encrypted[encrypted.Length/2]^=1;check(Reject(()=>OfficialRegionBackend.Decode(encrypted)),"损坏的加密备份拒绝读取");
            var fake=new Fake{Bytes=(byte[])before.Clone(),Running=true};check(Reject(()=>RegionService.Change(fake,false,()=>true))&&fake.Writes==0&&fake.Saves==0,"任一战网仍运行时停止，不备份或修改");
            fake=new Fake{Bytes=(byte[])before.Clone()};check(Reject(()=>RegionService.Change(fake,false,()=>false))&&fake.Writes==0,"密码库锁定时不操作配置");
            fake=new Fake{Bytes=(byte[])before.Clone(),FailBackup=true};check(Reject(()=>RegionService.Change(fake,false,()=>true))&&fake.Writes==0,"备份失败时不修改配置");
            fake=new Fake{Bytes=(byte[])before.Clone(),Drift=true};check(Reject(()=>RegionService.Change(fake,false,()=>true))&&fake.Writes==0&&fake.Marked==0,"写入前配置变化时停止，不标记已应用");
            fake=new Fake{Bytes=(byte[])before.Clone()};int authChecks=0;check(Reject(()=>RegionService.Change(fake,false,()=>++authChecks==1))&&fake.Writes==0,"操作过程中锁定时在写入前停止");
            fake=new Fake{Bytes=(byte[])before.Clone()};RegionService.Change(fake,false,()=>true);check(fake.Writes==1&&fake.Saves==1&&fake.Marked==1,"先备份后写入，成功后才登记可撤销记录");RegionService.Change(fake,false,()=>true);check(fake.Writes==1&&fake.Saves==1,"已启用时不产生重复修改与备份");RegionService.Change(fake,true,()=>true);check(fake.Bytes.SequenceEqual(before)&&fake.Undone==1,"界面调用的实际流程可以完整撤销");
            string parent=Path.GetDirectoryName(Path.GetFullPath(report));string fixture=Path.Combine(parent,"region-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
            try {
                string file=Path.Combine(fixture,"Battle.net.config"),home=Path.Combine(fixture,"home"),backups=Path.Combine(home,"data","region-backups");File.WriteAllBytes(file,before);var backend=new OfficialRegionBackend(home,file,()=>{});RegionService.Change(backend,false,()=>true);
                check(File.ReadAllBytes(file).SequenceEqual(after)&&Directory.GetFiles(backups,"*.bin").Length==1&&Directory.GetFiles(backups,"*.pending").Length==0,"真实文件后端原子写入虚构配置，并完成加密撤销记录");
                RegionService.Change(new OfficialRegionBackend(home,file,()=>{}),true,()=>true);check(File.ReadAllBytes(file).SequenceEqual(before)&&Directory.GetFiles(backups,"*.restored").Length==1,"重建后端后仍可读备份撤销，已撤销记录保留");
                check(Reject(()=>RegionService.Change(new OfficialRegionBackend(home,file,()=>{}),true,()=>true)),"没有未撤销记录时不再次修改");
                int fileChecks=0;backend=new OfficialRegionBackend(home,file,()=>{if(++fileChecks==3)File.WriteAllBytes(file,B(sample.Replace("FAKE_ACCOUNT","FAKE_DIFFERENT")));});check(Reject(()=>RegionService.Change(backend,false,()=>true))&&S(File.ReadAllBytes(file)).Contains("FAKE_DIFFERENT")&&Directory.GetFiles(backups,"*.bin").Length==0,"写入瞬间检测到磁盘配置变化时保留外部修改，不登记成功备份");
                check(Directory.GetFiles(fixture,"*.tmp").Length==0,"失败后清理本次临时文件");
                File.WriteAllBytes(file,before);using(var hold=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){check(Reject(()=>RegionService.Change(new OfficialRegionBackend(home,file,()=>{}),false,()=>true)),"配置被其他程序占用时停止替换");}check(File.ReadAllBytes(file).SequenceEqual(before),"写入失败后原磁盘配置保持不变");
            }finally{if(Path.GetFullPath(fixture).StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(fixture).StartsWith("region-files-",StringComparison.Ordinal))Directory.Delete(fixture,true);}
            var window=new RegionDialog(null,Path.Combine(parent,"unused"),()=>false);try{window.SetPreview("已识别 2 层战网设置。可以补齐中国、美洲、欧洲、亚洲的登录入口。");Entry.Render(window,Path.Combine(parent,"preview-region.png"),700,650);}finally{window.Close();}
            lines.Add("合计 "+lines.Count+" 项通过；只使用虚构配置与后端，没有写入真实战网配置或启动客户端。");File.WriteAllLines(report,lines,new UTF8Encoding(true));
        }
    }
}
