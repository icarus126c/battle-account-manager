using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;using System.Web.Script.Serialization;
namespace BrowserManager {
    public static class ManagerTests {
        static byte[] Legacy(LegacyData data,bool password){byte[] raw=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data));try{var encrypted=ProtectedData.Protect(raw,Encoding.UTF8.GetBytes(password?"BattleLogin.LocalVault.v1":"BattleBrowserManager.Metadata.v1"),DataProtectionScope.CurrentUser);return password?Encoding.ASCII.GetBytes("BLOGIN01").Concat(encrypted).ToArray():encrypted;}finally{Array.Clear(raw,0,raw.Length);}}
        static bool Reject(Action action){try{action();return false;}catch{return true;}}
        public static void Run(string report){
            string parent=Path.GetDirectoryName(Path.GetFullPath(report)),scratch=Path.Combine(parent,"account-manager-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);var results=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new Exception(text);results.Add("通过："+text);};
            try{
                var store=new Store(scratch);var data=store.Load();check(data.Version==2&&data.Accounts.Count==0,"新资料可以从空备忘列表开始");
                var cn=Account.New("虚构国服备忘","CN");cn.LoginMethod="手机号 + 将军令";cn.AuthenticatorMemo="虚构手机备忘";data.Accounts.Add(cn);store.Save(data);check(store.Load().Accounts[0].Password==""&&store.Load().Accounts[0].LoginAccount=="","只写登录方式和备忘，不填账号密码也能保存");
                var global=Account.New("虚构外服备忘","GLOBAL");global.Username="FAKE_NICKNAME";global.LoginAccount="demo@example.invalid";global.Password="  FAKE_SECRET_口令!  ";global.Note="虚构美洲区备忘";data.Accounts.Add(global);store.Save(data);var roundtrip=store.Load();
                check(roundtrip.Accounts[1].Password==global.Password&&roundtrip.Accounts[0].AuthenticatorMemo==cn.AuthenticatorMemo,"密码空格、中文与验证方式备忘完整加密读回");
                var encrypted=File.ReadAllBytes(store.FilePath);string visible=Encoding.UTF8.GetString(encrypted);check(!visible.Contains(global.Password)&&!visible.Contains(global.LoginAccount)&&!visible.Contains(cn.AuthenticatorMemo),"本机资料文件未包含明文密码、账号或验证备忘");
                check(File.Exists(store.FilePath+".bak"),"资料更新保留上一份加密备份");
                check(Accounts.Find(data,"CN","",false,false).Count==1&&Accounts.Find(data,"GLOBAL","",false,false).Count==1,"国服外服列表各显示自己的资料");
                check(Accounts.Find(data,"CN","将军令",false,false).Count==1&&Accounts.Find(data,"GLOBAL","美洲",false,false).Count==1,"按登录方式和备注搜索账号");
                check(Accounts.Find(data,"GLOBAL","FAKE_SECRET",false,false).Count==0&&!global.ToString().Contains(global.Password),"密码不会出现在列表或搜索结果中");
                cn.Archived=true;store.Save(data);check(Accounts.Find(store.Load(),"CN","",false,false).Count==0&&Accounts.Find(store.Load(),"CN","",false,true).Count==1,"归档后隐藏且可以从归档列表找回");cn.Archived=false;
                var duplicate=Store.Decode(Store.Encode(data));duplicate.Accounts[0].Note="不覆盖已有资料";int added=Accounts.Merge(data,duplicate);check(added==0&&data.Accounts[0].Note!="不覆盖已有资料","重复导入备份不覆盖本机已有备忘");
                var extra=Store.Initial();extra.Accounts.Add(Account.New("外服新增备忘","GLOBAL"));check(Accounts.Merge(data,extra)==1&&data.Accounts.Count==3,"导入新增记录保留区服和独立标识");
                string backup=Path.Combine(scratch,"fake-backup.bav");Store.WriteAtomic(backup,Store.Encode(data));check(Store.Import(backup).Accounts.Count==3,"加密备份可以导入并保留账号资料");
                var wrong=cn.Copy();wrong.Region="WRONG";check(Reject(()=>Store.Encode(new Data{Version=2,Accounts=new List<Account>{wrong}})),"不接受错误地区字段");
                var duplicateIds=new Data{Version=2,Accounts=new List<Account>{cn,cn.Copy()}};byte[] before=File.ReadAllBytes(store.FilePath);check(Reject(()=>store.Save(duplicateIds))&&before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"重复标识保存失败不会覆盖原资料");
                encrypted[encrypted.Length-1]^=1;check(Reject(()=>Store.Decode(encrypted)),"损坏的加密备份拒绝读取");
                var old=new LegacyData{Version=1,Accounts=new List<LegacyAccount>{new LegacyAccount{Id=Guid.NewGuid().ToString("N"),Name="旧浏览器示例",Label="old@example.invalid",Note="虚构旧备注",Favorite=true}}};
                byte[] oldBytes=Legacy(old,false);string legacyHome=Path.Combine(scratch,"战网浏览器账号管家-v0.6.0");Directory.CreateDirectory(Path.Combine(legacyHome,"data"));string legacyFile=Path.Combine(legacyHome,"data","accounts.bin");File.WriteAllBytes(legacyFile,oldBytes);var migrated=new Store(legacyHome).Load();
                check(migrated.Accounts[0].Id==old.Accounts[0].Id&&migrated.Accounts[0].Region=="CN"&&migrated.Accounts[0].LoginAccount==old.Accounts[0].Label,"旧浏览器资料迁移为国服备忘并保留原账号标识");
                check(Browser.Profile(legacyHome,migrated.Accounts[0].Id)==Path.Combine(legacyHome,"data","browser","chrome",old.Accounts[0].Id),"旧 Chrome 资料路径保留，无需创建新的账号资料");
                new Store(legacyHome).Save(migrated);check(oldBytes.SequenceEqual(File.ReadAllBytes(legacyFile)),"新版保存到单独加密文件，不覆盖旧版资料");
                string newHome=Path.Combine(scratch,"战网账号管家-v0.7.0");Directory.CreateDirectory(newHome);check(Store.ResolveHome(newHome)==legacyHome,"新程序在旁边时自动找到已有资料目录");
                var local=new Store(newHome);local.Save(Store.Initial());check(Store.ResolveHome(newHome)==newHome,"新版自身已有资料时优先使用本目录");
                var oldPasswords=new LegacyData{Version=1,Accounts=new List<LegacyAccount>{new LegacyAccount{Id=Guid.NewGuid().ToString("N"),Name="旧密码备忘示例",Region="GLOBAL",Username="old-global@example.invalid",Password="FAKE_OLD_PASSWORD",Note="旧备忘"}}};var imported=Store.DecodeLegacy(Legacy(oldPasswords,true),true);check(imported.Accounts[0].Region=="GLOBAL"&&imported.Accounts[0].Password==oldPasswords.Accounts[0].Password&&imported.Accounts[0].LoginAccount==oldPasswords.Accounts[0].Username,"可导入旧密码管家的加密账号备份");
                var package=SecretClipboard.Package("FAKE_COPY_ONLY","OWNED");check(package.GetData("CanIncludeInClipboardHistory") is MemoryStream&&((MemoryStream)package.GetData("CanIncludeInClipboardHistory")).ToArray().SequenceEqual(new byte[4]),"复制数据带有禁止进入剪贴板历史的格式标记");check(package.GetData("CanUploadToCloudClipboard") is MemoryStream,"复制数据带有禁止云剪贴板同步的格式标记");check(SecretClipboard.Owns("OWNED",package.GetData("BattleAccountManager.CopyOwner"))&&!SecretClipboard.Owns("OWNED","OTHER"),"清理复制内容前核对所有权，不清除其他应用的新复制内容");
                byte[] damaged=File.ReadAllBytes(store.FilePath);damaged[damaged.Length/2]^=1;File.WriteAllBytes(store.FilePath,damaged);check(Reject(()=>store.Load())&&damaged.SequenceEqual(File.ReadAllBytes(store.FilePath)),"损坏资料报错并保留原文件，不以空列表覆盖");
                foreach(string bad in new[]{"../A","A",new string('g',32)})check(Reject(()=>Browser.Profile(scratch,bad)),"非法账号标识不能生成浏览器资料路径");
                string routeReport=Path.Combine(scratch,"routing.txt");CnDirect.Tests.Run(routeReport);results.AddRange(File.ReadAllLines(routeReport));
                results.Add("所有密码与账号均为虚构。没有读取真实密码、修改真实资料、操作系统剪贴板或启动真实登录窗口。");File.WriteAllLines(report,results,new UTF8Encoding(true));
            }finally{string full=Path.GetFullPath(scratch);if(full.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("account-manager-test-",StringComparison.Ordinal))Directory.Delete(full,true);}
        }
    }
}
