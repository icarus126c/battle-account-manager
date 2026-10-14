using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
namespace BrowserManager {
    public static class TwoFactorTests {
        const string Password = "FAKE master password 123", Changed = "FAKE changed password 456";
        static bool Reject(Action operation) { try { operation(); return false; } catch { return true; } }
        static T Field<T>(MainWindow w, string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w); }
        static void Call(MainWindow w, string method, params object[] args) { typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(w,args); }
        public static void Run(string report) {
            var rows = new List<string>(); Action<bool,string> check = (pass,text) => { if(!pass)throw new Exception(text); rows.Add("通过："+text); };
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"factor-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string vectorKey = Totp.Base32(Encoding.ASCII.GetBytes("12345678901234567890"));
            long[] seconds = {59,1111111109,1111111111,1234567890,2000000000,20000000000}; string[] expected = {"94287082","07081804","14050471","89005924","69279037","65353130"};
            for(int i=0;i<seconds.Length;i++)check(Totp.Code(vectorKey,seconds[i]/30,8)==expected[i],"RFC 6238 SHA-1 标准向量 "+seconds[i]);
            DateTime now = new DateTime(2026,10,13,8,0,0,DateTimeKind.Utc); string secret = Totp.NewSecret(); long step=Totp.Step(now);
            check(secret.Length==32 && Totp.Match(secret,Totp.Code(secret,step),now)==step,"随机绑定密钥及 6 位、30 秒动态码可用");
            check(Totp.Match(secret,Totp.Code(secret,step-1),now)==step-1 && Totp.Match(secret,Totp.Code(secret,step+1),now)==step+1,"容许手机与电脑前后一个周期误差");
            check(Totp.Match(secret,Totp.Code(secret,step-3),now)<0 && Totp.Match(secret,"12345X",now)<0,"过期及非数字动态码被拒绝");
            var codes=Totp.NewRecoveryCodes();check(codes.Length==8 && codes.Distinct().Count()==8 && codes.All(c=>c.Replace("-","").Length==20),"生成八个独立高熵备用恢复码");
            var data=Store.Initial();var account=Account.New("FAKE ACCOUNT","CN");account.Password="  FAKE_SECRET  ";data.Accounts.Add(account);
            var store=new Store(Path.Combine(root,"vault"));store.PasswordIterations=10000;store.Now=()=>now;store.Save(data);
            check(!store.IsProtected && !store.IsLocked && store.Load().Accounts[0].Password==account.Password,"免主密码模式仍使用 Windows 用户加密并直接加载");
            check(Reject(()=>store.EnableTwoFactor(Password,secret,Totp.Code(secret,step),codes)),"两步验证不能脱离主密码单独启用");
            store.EnableProtection(data,Password);byte[] before=File.ReadAllBytes(store.FilePath);
            check(Reject(()=>store.EnableTwoFactor(Password,secret,"BAD",codes)) && before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"绑定验证失败不更改保护设置或资料");
            data=store.EnableTwoFactor(Password,secret,Totp.Code(secret,step),codes);
            check(store.NeedsFactor && MasterKey.HasFactor(File.ReadAllBytes(store.FilePath)) && MasterKey.HasFactor(File.ReadAllBytes(store.FilePath+".bak")),"确认绑定后主库与当前备份使用两步验证专用格式");
            check(!Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Contains(secret) && data.Security.RecoveryHashes.All(h=>!codes.Contains(h)),"绑定密钥加密保存，恢复码仅存校验散列");
            var copy=data.Copy();copy.Accounts[0].Note="FAKE_EDIT";store.Save(copy);data=store.Load();
            check(data.Security.Secret==secret && data.Security.RecoveryHashes.Count==8,"普通编辑保存保留两步验证及备用码");
            var dropped=data.Copy();dropped.Security=null;check(Reject(()=>store.Save(dropped)),"普通保存入口拒绝意外移除两步验证");
            store.Lock();before=File.ReadAllBytes(store.FilePath);
            check(Reject(()=>store.Unlock(Password)) && store.IsLocked && before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"只有主密码时拒绝解锁且原文件不变");
            check(Reject(()=>store.Unlock(Password,Totp.Code(secret,step))) && store.IsLocked,"绑定时已使用的动态码不能再次解锁");
            now=now.AddSeconds(30);data=store.Unlock(Password,Totp.Code(secret,Totp.Step(now)));store.Lock();
            check(Reject(()=>store.Unlock(Password,Totp.Code(secret,Totp.Step(now)))) && store.IsLocked,"成功动态码消耗状态写入磁盘，锁定后不可重放");
            var reopened=new Store(store.Home);reopened.PasswordIterations=10000;reopened.Now=()=>now;
            check(Reject(()=>reopened.Unlock(Password,Totp.Code(secret,Totp.Step(now)))) && reopened.IsLocked,"重启后仍拒绝同一动态码");
            data=reopened.Unlock(Password,codes[0].ToLowerInvariant());check(data.Security.RecoveryHashes.Count==7 && data.Accounts[0].Password==account.Password,"主密码配合备用码可恢复访问且原密码空白完整保留");
            reopened.Lock();check(Reject(()=>reopened.Unlock(Password,codes[0])),"备用恢复码只能使用一次");
            data=reopened.Unlock(Password,codes[1]);now=now.AddSeconds(30);
            string backup=Path.Combine(root,"factor.bav");Store.WriteAtomic(backup,reopened.Export(data));
            check(Reject(()=>reopened.ReadBackup(backup,Password)),"两步验证备份不能只用主密码导入");
            check(reopened.ReadBackup(backup,Password,Totp.Code(secret,Totp.Step(now))).Accounts.Count==1,"备份可用原主密码和动态码导入");
            check(Reject(()=>reopened.ChangePassword(data,Password,Changed)),"修改主密码也须通过第二步验证");
            reopened.ChangePassword(data,Password,Changed,Totp.Code(secret,Totp.Step(now)));reopened.Lock();now=now.AddSeconds(30);
            check(Reject(()=>reopened.Unlock(Password,codes[2])) && reopened.Unlock(Changed,Totp.Code(secret,Totp.Step(now))).Security!=null,"修改主密码保留两步验证，新主密码才能访问");
            check(Reject(()=>reopened.RemoveProtection(Changed)),"已解锁时移除保护仍须再次验证动态码");
            now=now.AddSeconds(30);data=reopened.DisableTwoFactor(Changed,Totp.Code(secret,Totp.Step(now)));reopened.Lock();
            check(!reopened.NeedsFactor && reopened.Unlock(Changed).Security==null,"关闭两步验证后保留主密码且不再要求动态码");
            check(Reject(()=>reopened.RemoveProtection("WRONG")) && reopened.IsProtected,"移除主密码须验证原主密码");
            data=reopened.RemoveProtection(Changed);var optional=new Store(reopened.Home);
            check(!optional.IsProtected && !optional.IsLocked && optional.Load().Accounts[0].Id==account.Id && optional.Load().Accounts[0].Password==account.Password,"移除主密码后可直接使用且账户标识和密码保留");
            check(!MasterKey.Protected(File.ReadAllBytes(optional.FilePath)) && !MasterKey.Protected(File.ReadAllBytes(optional.FilePath+".bak")) && Store.Decode(File.ReadAllBytes(optional.FilePath)).Security==null && !Encoding.UTF8.GetString(File.ReadAllBytes(optional.FilePath)).Contains(account.Password),"移除后保留 Windows 用户加密格式");
            var protectedHistory=Directory.GetFiles(Path.Combine(optional.Home,"data","protected-history"),"*.bav");check(protectedHistory.All(p=>MasterKey.Protected(File.ReadAllBytes(p))),"历史受保护备份保持原保护，不自动降级");
            var throttle=new Store(Path.Combine(root,"throttle"));throttle.PasswordIterations=10000;throttle.Now=()=>now;throttle.Save(Store.Initial());throttle.EnableProtection(Store.Initial(),Password);throttle.Lock();
            for(int i=0;i<5;i++)Reject(()=>throttle.Unlock("BAD"));check(Reject(()=>throttle.Unlock(Password)) && throttle.IsLocked,"连续五次失败后暂停三十秒");now=now.AddSeconds(31);check(throttle.Unlock(Password).Accounts.Count==0,"等待后可以正常解锁");throttle.Lock();
            var window=new MainWindow(true,"CN",Path.Combine(root,"ui"));try{
                var uiStore=Field<Store>(window,"store");uiStore.PasswordIterations=10000;uiStore.Now=()=>now;var uiData=Field<Data>(window,"data");uiStore.Save(uiData);Call(window,"UpdateVaultAccess");
                check(Field<Border>(window,"vaultOverlay").Visibility==Visibility.Collapsed,"未设主密码时无强制设置遮罩");Call(window,"Require");
                uiStore.EnableProtection(uiData,Password);uiData=uiStore.EnableTwoFactor(Password,secret,Totp.Code(secret,Totp.Step(now)),Totp.NewRecoveryCodes());typeof(MainWindow).GetField("data",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,uiData);
                Call(window,"LockVault");check(Field<Data>(window,"data").Accounts.Count==0 && Reject(()=>Call(window,"Require")),"两步验证锁定后卸下账号并禁止查看复制");
                Entry.Render(window,Path.Combine(Path.GetDirectoryName(report),"preview-2fa-locked.png"),1100,920);
                now=now.AddSeconds(30);uiData=uiStore.Unlock(Password,Totp.Code(secret,Totp.Step(now)));typeof(MainWindow).GetField("data",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,uiData);Call(window,"Refresh",new object[]{null});Call(window,"UpdateVaultAccess");
                var clone=(Data)typeof(MainWindow).GetMethod("Clone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,null);check(clone.Security!=null && !object.ReferenceEquals(clone.Security,uiData.Security),"界面编辑快照完整复制独立的两步验证资料");
                using(var dialog=new DialogHolder(new VaultOptionsDialog(null,true,true)))Entry.Render(dialog.Window,Path.Combine(Path.GetDirectoryName(report),"preview-protection-settings.png"),470,400);
                using(var dialog=new DialogHolder(new FactorEnrollmentDialog(null,(p,s,c,r)=>System.Threading.Tasks.Task.FromResult(0))))Entry.Render(dialog.Window,Path.Combine(Path.GetDirectoryName(report),"preview-2fa-binding.png"),550,790);
                using(var dialog=new DialogHolder(new MasterDialog(null,"解锁密码库",(p,n,c)=>System.Threading.Tasks.Task.FromResult(0),true)))Entry.Render(dialog.Window,Path.Combine(Path.GetDirectoryName(report),"preview-2fa-unlock.png"),470,420);
            }finally{window.Close();}
            rows.Add("仅使用虚构密码、验证码和工作目录；没有读取、修改真实密码库或操作真实战网登录。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
        }
        sealed class DialogHolder:IDisposable{public Window Window;public DialogHolder(Window window){Window=window;}public void Dispose(){Window.Close();}}
    }
}
