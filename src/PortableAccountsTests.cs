using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BrowserManager {
    public static class PortableAccountsTests {
        const string Password="FAKE transfer 密码 only 123!";
        static bool Reject(Action action){try{action();return false;}catch{return true;}}
        public static void Run(string report){
            var rows=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);rows.Add("通过："+name);};
            var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"portable-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var original=Store.Initial();var a=Account.New("FAKE_CN","CN");a.Password="  FAKE_ACCOUNT_SECRET_口令  ";a.Note="FAKE_NOTE";a.LoginAccount="fake@example.invalid";a.AuthenticatorMemo="FAKE authenticator memo";a.Favorite=true;a.Archived=true;a.LoginState="confirmed";a.LastLoginAction="native";a.LoginCheckedUtc=DateTime.UtcNow;original.Accounts.Add(a);original.Accounts.Add(Account.New("FAKE_GLOBAL","GLOBAL"));
            original.Security=new FactorSettings{Secret="FAKE_VAULT_FACTOR_MUST_NOT_EXPORT",LastStep=123,RecoveryHashes=new List<string>{"FAKE_RECOVERY_MUST_NOT_EXPORT"}};
            var before=new JavaScriptSerializer().Serialize(original);
            try{
                foreach(var bad in new[]{"","short","          ",new string('a',257)})check(Reject(()=>PortableAccounts.Encode(original,bad,10000)),"拒绝无效迁移密码");
                var packet=PortableAccounts.Encode(original,Password,10000);var second=PortableAccounts.Encode(original,Password,10000);
                check(!packet.SequenceEqual(second),"同一资料和密码导出为不同密文");
                check(!Encoding.UTF8.GetString(packet).Contains(a.Password)&&!Encoding.UTF8.GetString(packet).Contains(a.LoginAccount)&&!Encoding.UTF8.GetString(packet).Contains(Password),"密文不暴露账号、密码或迁移密码");
                check(new JavaScriptSerializer().Serialize(original)==before,"导出不修改原账号状态或验证器设置");
                var decoded=PortableAccounts.Decode(packet,Password);
                check(decoded.Accounts.Accounts.Count==2&&decoded.Accounts.Accounts[0].Id==a.Id&&decoded.Accounts.Accounts[0].Password==a.Password,"两个区服及密码前后空白完整保留");
                check(decoded.Accounts.Accounts[0].Favorite&&decoded.Accounts.Accounts[0].Archived&&decoded.Accounts.Accounts[0].AuthenticatorMemo==a.AuthenticatorMemo,"收藏、隐藏条目和手工验证器备忘保留");
                check(decoded.Accounts.Security==null&&decoded.Accounts.Accounts[0].LoginState=="pending"&&decoded.Accounts.Accounts[0].LastLoginAction==""&&decoded.Accounts.Accounts[0].LoginCheckedUtc==DateTime.MinValue,"本机验证器设置不转移，登录状态重置为待确认");
                // Independently inspect password-only decrypted payload. There must be
                // no DPAPI blob or Windows identity between the password and JSON.
                var inner=packet.Skip(8).ToArray();
                using(var key=MasterKey.Derive(Password,inner)){
                    var raw=key.Open(inner);try{var text=Encoding.UTF8.GetString(raw);var payload=new JavaScriptSerializer().Deserialize<PortableEnvelope>(text);check(payload.Purpose=="BattleAccountManager.PortableAccounts.v1"&&payload.Accounts.Accounts[0].Password==a.Password,"仅凭迁移密码直接获得账号 JSON，无 Windows 解密步骤");check(!text.Contains(original.Security.Secret)&&!text.Contains(original.Security.RecoveryHashes[0]),"管家验证器密钥及恢复码不进入迁移明文");}finally{Array.Clear(raw,0,raw.Length);}
                }
                check(Reject(()=>PortableAccounts.Decode(packet,"FAKE wrong password 999")),"错误密码拒绝解密");
                foreach(int offset in new[]{0,8,20,52,72,packet.Length-1}){var changed=(byte[])packet.Clone();changed[offset]^=1;check(Reject(()=>PortableAccounts.Decode(changed,Password)),"拒绝篡改包，位置 "+offset);}
                check(Reject(()=>PortableAccounts.Decode(packet.Take(packet.Length-1).ToArray(),Password)),"拒绝截断文件");
                var excessive=(byte[])packet.Clone();Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue),0,excessive,16,4);check(Reject(()=>PortableAccounts.Decode(excessive,Password)),"拒绝超额派生参数");
                check(Reject(()=>PortableAccounts.Decode(new byte[18*1024*1024],Password)),"拒绝超大文件");
                var badEnvelope=new PortableEnvelope{Format=2,Purpose="wrong",Accounts=Store.Initial()};
                using(var key=MasterKey.Create(Password,10000)){var badInner=key.Seal(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(badEnvelope)));var badPacket=Encoding.ASCII.GetBytes("BAMPORT1").Concat(badInner).ToArray();check(Reject(()=>PortableAccounts.Decode(badPacket,Password)),"已认证但用途或版本不符的包也拒绝导入");}
                var source=new Store(Path.Combine(root,"source"));var clean=original.Copy();clean.Security=null;source.Save(clean);var saved=File.ReadAllBytes(source.FilePath);
                source.PasswordIterations=10000;source.EnableProtection(clean,"FAKE local master 987");source.Lock();check(Reject(()=>source.Load()),"锁定的本机密码库不可读取用于迁移");
                check(Reject(()=>PortableAccounts.Decode(File.ReadAllBytes(source.FilePath),Password)),"本机备份不能伪装为跨电脑迁移包");
                source.Unlock("FAKE local master 987");var sourceBytes=File.ReadAllBytes(source.FilePath);
                var target=new Store(Path.Combine(root,"other-windows-home"));target.PasswordIterations=10000;var targetData=Store.Initial();var existing=a.Copy();existing.Password="FAKE_NEWER_TARGET_PASSWORD";targetData.Accounts.Add(existing);target.Save(targetData);target.EnableProtection(targetData,"FAKE target master 789");var targetBytes=File.ReadAllBytes(target.FilePath);
                check(Reject(()=>PortableAccounts.Decode(packet,"FAKE wrong password 999"))&&targetBytes.SequenceEqual(File.ReadAllBytes(target.FilePath)),"解密失败不改写目标资料库");
                int added=Accounts.Merge(targetData,decoded.Accounts);target.Save(targetData);target.Lock();check(target.IsProtected&&target.IsLocked,"导入后仍保留目标主密码保护");
                var reopened=target.Unlock("FAKE target master 789");check(added==1&&reopened.Accounts.Count==2&&reopened.Accounts[0].Password==existing.Password,"合并只增加缺失条目，不覆盖已有密码");check(Accounts.Merge(reopened,decoded.Accounts)==0,"重复导入不产生重复账号");check(sourceBytes.SequenceEqual(File.ReadAllBytes(source.FilePath)),"整个迁移过程不写入原资料库");
                string file=Path.Combine(root,"fixture.bammove");Store.WriteAtomic(file,(byte[])packet.Clone());var fromDisk=PortableAccounts.Read(file,Password);check(fromDisk.Accounts.Accounts[0].Password==a.Password,"实际文件读写往返成功");PortableAccounts.Clear(fromDisk.Accounts);
                var production=PortableAccounts.Encode(clean,Password);check(BitConverter.ToInt32(production,16)==600000,"正式迁移包使用 600000 次密码派生");
                var productionData=PortableAccounts.Decode(production,Password);check(productionData.Accounts.Accounts[0].Password==a.Password,"正式强度解密成功");PortableAccounts.Clear(productionData.Accounts);
                var exportUi=new TransferPasswordDialog(null,true,p=>Task.FromResult(0));try{Entry.Render(exportUi,Path.Combine(Path.GetDirectoryName(report),"preview-migration.png"),520,570);}finally{exportUi.Close();}
                var settings=new SettingsDialog(null,"FAKE storage summary");try{Entry.Render(settings,Path.Combine(Path.GetDirectoryName(report),"preview-migration-settings.png"),620,700);}finally{settings.Close();}
                var window=new MainWindow(false,"CN",Path.Combine(root,"ui-home"));try{
                    typeof(MainWindow).GetField("sessionLockPending",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,true);
                    check(Reject(()=>typeof(MainWindow).GetMethod("RequireTransferAccess",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null)),"锁屏待处理时拒绝迁移写入");
                }finally{window.Close();}
                PortableAccounts.Clear(decoded.Accounts);source.Lock();target.Lock();
                rows.Add("全部为虚构资料。已验证格式不依赖 Windows 密钥；未在第二台实体电脑上实测。未启动战网或读取真实账号。");
                File.WriteAllLines(report,rows,new UTF8Encoding(true));
            }finally{var parent=Path.GetFullPath(Path.GetDirectoryName(report)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(Path.GetFullPath(root).StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("portable-fixture-",StringComparison.Ordinal))Directory.Delete(root,true);}
        }
    }
}
