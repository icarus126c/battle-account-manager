using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;using System.Web.Script.Serialization;
namespace BrowserManager {
    public class Account {
        public string Id {get;set;} public string Name {get;set;} public string Region {get;set;}
        public string Username {get;set;} public string LoginAccount {get;set;} public string Password {get;set;}
        public string LoginMethod {get;set;} public string AuthenticatorMemo {get;set;} public string Note {get;set;}
        public string LoginBrowser {get;set;} public string ExternalBrowser {get;set;}
        public string LoginState {get;set;} public string LastLoginAction {get;set;} public DateTime LoginCheckedUtc {get;set;}
        public bool Favorite {get;set;} public bool Archived {get;set;} public DateTime LastUsed {get;set;}
        public override string ToString(){return (Favorite?"★  ":"")+Name+(string.IsNullOrWhiteSpace(LoginAccount)?"":"\n"+LoginAccount);}
        public Account Copy(){return (Account)MemberwiseClone();}
        public static Account New(string name,string region){return new Account{Id=Guid.NewGuid().ToString("N"),Name=name,Region=region,Username="",LoginAccount="",Password="",LoginMethod="",AuthenticatorMemo="",Note=""};}
    }
    public class Data {public int Version {get;set;} public List<Account> Accounts {get;set;} public FactorSettings Security {get;set;}
        public Data Copy(){return new Data{Version=Version,Accounts=Accounts.Select(a=>a.Copy()).ToList(),Security=Security==null?null:Security.Copy()};}
    }
    public class LegacyAccount {public string Id {get;set;} public string Name {get;set;} public string Label {get;set;} public string Username {get;set;} public string Password {get;set;} public string Region {get;set;} public string Note {get;set;} public bool Favorite {get;set;} public DateTime LastUsed {get;set;} }
    public class LegacyData {public int Version {get;set;} public List<LegacyAccount> Accounts {get;set;} }
    public static class Accounts {
        public static List<Account> Find(Data data,string region,string query,bool favorites,bool archived){
            string q=(query??"").Trim();
            return data.Accounts.Where(a=>a.Region==region&&a.Archived==archived&&(!favorites||a.Favorite)).Where(a=>(a.Name+" "+a.Username+" "+a.LoginAccount+" "+a.LoginMethod+" "+a.AuthenticatorMemo+" "+a.Note).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(a=>a.Favorite).ThenBy(a=>a.Name).ToList();
        }
        public static int Merge(Data target,Data incoming){Store.Validate(target);Store.Validate(incoming);int count=0;var ids=new HashSet<string>(target.Accounts.Select(a=>a.Id),StringComparer.OrdinalIgnoreCase);foreach(var a in incoming.Accounts)if(ids.Add(a.Id)){target.Accounts.Add(a.Copy());count++;}Store.Validate(target);return count;}
    }
    public partial class Store {
        public readonly string Home;public readonly string FilePath;
        static readonly byte[] entropy=Encoding.UTF8.GetBytes("BattleAccountManager.Vault.v2");
        static readonly byte[] legacyEntropy=Encoding.UTF8.GetBytes("BattleBrowserManager.Metadata.v1");
        static readonly byte[] passwordLegacyEntropy=Encoding.UTF8.GetBytes("BattleLogin.LocalVault.v1");
        static readonly byte[] header=Encoding.ASCII.GetBytes("BAMV0200");
        static readonly byte[] oldPasswordHeader=Encoding.ASCII.GetBytes("BLOGIN01");
        public Store(string home){Home=Path.GetFullPath(home);FilePath=Path.Combine(Home,"data","vault-v2.bin");}
        public static string ResolveHome(string appRoot){return DataDirectory.Ready(DataDirectory.FixedHome)?DataDirectory.FixedHome:ResolveLegacyHome(appRoot);}
        public static string ResolveLegacyHome(string appRoot){
            string root=Path.GetFullPath(appRoot);
            if(File.Exists(Path.Combine(root,"data","vault-v2.bin"))||File.Exists(Path.Combine(root,"data","accounts.bin")))return root;
            string prior160=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.6.0-可选保护版"));if(DataDirectory.HasVault(prior160))return prior160;
            string prior150=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.5.0-密码库保护版"));if(File.Exists(Path.Combine(prior150,"data","vault-v2.bin"))||File.Exists(Path.Combine(prior150,"data","accounts.bin")))return prior150;
            string prior140=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.4.0-国服实验版"));if(File.Exists(Path.Combine(prior140,"data","vault-v2.bin"))||File.Exists(Path.Combine(prior140,"data","accounts.bin")))return prior140;
            string prior131=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.3.1"));if(File.Exists(Path.Combine(prior131,"data","vault-v2.bin"))||File.Exists(Path.Combine(prior131,"data","accounts.bin")))return prior131;
            string prior130=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.3.0"));if(File.Exists(Path.Combine(prior130,"data","vault-v2.bin"))||File.Exists(Path.Combine(prior130,"data","accounts.bin")))return prior130;
            string prior120=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.2.0"));if(File.Exists(Path.Combine(prior120,"data","vault-v2.bin"))||File.Exists(Path.Combine(prior120,"data","accounts.bin")))return prior120;
            string priorFixed=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-外服填写修正版-v1.1.1"));if(File.Exists(Path.Combine(priorFixed,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorFixed,"data","accounts.bin")))return priorFixed;
            string priorFill=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-外服填写验证-v1.1.0"));if(File.Exists(Path.Combine(priorFill,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorFill,"data","accounts.bin")))return priorFill;
            string priorFormal=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v1.0.0"));if(File.Exists(Path.Combine(priorFormal,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorFormal,"data","accounts.bin")))return priorFormal;
            string priorWeb=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v0.13.0"));
            if(File.Exists(Path.Combine(priorWeb,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorWeb,"data","accounts.bin")))return priorWeb;
            string priorStartup=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-启动修正版-v0.12.0"));
            if(File.Exists(Path.Combine(priorStartup,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorStartup,"data","accounts.bin")))return priorStartup;
            string priorFix=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-多开试验版-v0.11.1"));
            if(File.Exists(Path.Combine(priorFix,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorFix,"data","accounts.bin")))return priorFix;
            string priorExperiment=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-多开试验版-v0.11.0"));
            if(File.Exists(Path.Combine(priorExperiment,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorExperiment,"data","accounts.bin")))return priorExperiment;
            string priorManager=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v0.10.0"));
            if(File.Exists(Path.Combine(priorManager,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorManager,"data","accounts.bin")))return priorManager;
            string latestWebview=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-WebView2试验版-v0.9.1"));
            if(File.Exists(Path.Combine(latestWebview,"data","vault-v2.bin"))||File.Exists(Path.Combine(latestWebview,"data","accounts.bin")))return latestWebview;
            string priorWebview=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-WebView2试验版-v0.9.0"));
            if(File.Exists(Path.Combine(priorWebview,"data","vault-v2.bin"))||File.Exists(Path.Combine(priorWebview,"data","accounts.bin")))return priorWebview;
            string nativeManager=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v0.8.0"));
            if(File.Exists(Path.Combine(nativeManager,"data","vault-v2.bin"))||File.Exists(Path.Combine(nativeManager,"data","accounts.bin")))return nativeManager;
            string manager=Path.GetFullPath(Path.Combine(root,@"..\战网账号管家-v0.7.0"));
            if(File.Exists(Path.Combine(manager,"data","vault-v2.bin"))||File.Exists(Path.Combine(manager,"data","accounts.bin")))return manager;
            string prior=Path.GetFullPath(Path.Combine(root,@"..\战网浏览器账号管家-v0.6.0"));
            return File.Exists(Path.Combine(prior,"data","accounts.bin"))||File.Exists(Path.Combine(prior,"data","vault-v2.bin"))?prior:root;
        }
        public static Data Initial(){return new Data{Version=2,Accounts=new List<Account>()};}
        static bool TooLong(string value,int limit){return (value??"").Length>limit;}
        public static void Validate(Data data){
            if(data==null||data.Version!=2||data.Accounts==null||data.Accounts.Count>1000)throw new InvalidDataException("账号资料版本或数量不符合要求。");
            if(data.Security!=null)data.Security.Validate();
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var a in data.Accounts){
                if(a==null||!Browser.ValidId(a.Id)||!ids.Add(a.Id)||(a.Region!="CN"&&a.Region!="GLOBAL")||string.IsNullOrWhiteSpace(a.Name)||TooLong(a.Name,80)||TooLong(a.Username,120)||TooLong(a.LoginAccount,254)||TooLong(a.Password,1024)||TooLong(a.LoginMethod,200)||TooLong(a.AuthenticatorMemo,2000)||TooLong(a.Note,4000))throw new InvalidDataException("账号资料有重复标识、无效地区或超长字段。");
                if(!string.IsNullOrEmpty(a.LoginState)&&a.LoginState!="pending"&&a.LoginState!="confirmed"&&a.LoginState!="needs-auth")throw new InvalidDataException("登录状态无效。");if(!string.IsNullOrEmpty(a.LastLoginAction)&&a.LastLoginAction!="web"&&a.LastLoginAction!="native"&&a.LastLoginAction!="first-login")throw new InvalidDataException("登录操作类型无效。");
                a.Username=a.Username??"";a.LoginAccount=a.LoginAccount??"";a.Password=a.Password??"";a.LoginMethod=a.LoginMethod??"";a.AuthenticatorMemo=a.AuthenticatorMemo??"";a.Note=a.Note??"";
                a.LoginBrowser=string.IsNullOrEmpty(a.LoginBrowser)?"webview2":a.LoginBrowser;a.ExternalBrowser=string.IsNullOrEmpty(a.ExternalBrowser)?"chrome":a.ExternalBrowser;
                if((a.LoginBrowser!="webview2"&&!Browser.External(a.LoginBrowser))||!Browser.External(a.ExternalBrowser))throw new InvalidDataException("浏览器选择无效。");
            }
        }
        static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024};}
        public static byte[] Encode(Data data){
            Validate(data);byte[] raw=Encoding.UTF8.GetBytes(Serializer().Serialize(data));
            try{if(raw.Length>15*1024*1024)throw new InvalidDataException("账号资料总大小超出限制，未保存。");byte[] encrypted=ProtectedData.Protect(raw,entropy,DataProtectionScope.CurrentUser);byte[] result=new byte[header.Length+encrypted.Length];Buffer.BlockCopy(header,0,result,0,header.Length);Buffer.BlockCopy(encrypted,0,result,header.Length,encrypted.Length);Array.Clear(encrypted,0,encrypted.Length);return result;}finally{Array.Clear(raw,0,raw.Length);}
        }
        public static Data Decode(byte[] bytes){
            if(bytes==null||bytes.Length<=header.Length||bytes.Length>16*1024*1024||!bytes.Take(header.Length).SequenceEqual(header))throw new InvalidDataException("不是有效的加密账号备份。");
            byte[] raw=ProtectedData.Unprotect(bytes.Skip(header.Length).ToArray(),entropy,DataProtectionScope.CurrentUser);
            try{var data=Serializer().Deserialize<Data>(Encoding.UTF8.GetString(raw));Validate(data);return data;}finally{Array.Clear(raw,0,raw.Length);}
        }
        public static Data DecodeLegacy(byte[] bytes,bool passwordVault){
            if(bytes==null||bytes.Length>16*1024*1024)throw new InvalidDataException();
            if(passwordVault&&(bytes.Length<=8||!bytes.Take(8).SequenceEqual(oldPasswordHeader)))throw new InvalidDataException();
            byte[] encrypted=passwordVault?bytes.Skip(8).ToArray():bytes;
            byte[] raw=ProtectedData.Unprotect(encrypted,passwordVault?passwordLegacyEntropy:legacyEntropy,DataProtectionScope.CurrentUser);
            try{
                var old=Serializer().Deserialize<LegacyData>(Encoding.UTF8.GetString(raw));if(old==null||old.Version!=1||old.Accounts==null)throw new InvalidDataException();
                var data=Initial();foreach(var a in old.Accounts){if(a==null)throw new InvalidDataException();data.Accounts.Add(new Account{Id=a.Id,Name=a.Name,Region=passwordVault?a.Region:"CN",Username="",LoginAccount=passwordVault?a.Username:a.Label,Password=passwordVault?a.Password:"",LoginMethod=passwordVault?"账号 + 密码":"",AuthenticatorMemo="",Note=a.Note,Favorite=a.Favorite,LastUsed=a.LastUsed});}Validate(data);return data;
            }finally{Array.Clear(raw,0,raw.Length);}
        }
        static byte[] Read(string path){if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException();return File.ReadAllBytes(path);}
        public Data Load(){
            try{if(File.Exists(FilePath)){lock(protectionSync){byte[] bytes=ReadProtected(FilePath);try{return DecodeActive(bytes);}finally{Array.Clear(bytes,0,bytes.Length);}}}string legacy=Path.Combine(Home,"data","accounts.bin");if(File.Exists(legacy)){byte[] bytes=Read(legacy);return DecodeLegacy(bytes,bytes.Take(8).SequenceEqual(oldPasswordHeader));}return Initial();}
            catch(VaultLockedException){throw;}catch{throw new InvalidOperationException("账号资料无法读取，原文件和备份已保留。请保留整个资料文件夹；不会用空列表覆盖原文件。");}
        }
        public void Save(Data data){lock(protectionSync){if(IsLocked)throw new VaultLockedException();byte[] next=Export(data);try{DailyBackup();WriteAtomic(FilePath,next);}finally{Array.Clear(next,0,next.Length);}}}
        public static void WriteAtomic(string path,byte[] encrypted){
            string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full));string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted,0,encrypted.Length);stream.Flush(true);}if(File.Exists(full))ReplaceFile(temp,full,full+".bak");else File.Move(temp,full);}
            finally{Array.Clear(encrypted,0,encrypted.Length);if(File.Exists(temp))File.Delete(temp);}
        }
        public static Data Import(string path){byte[] bytes=Read(path);if(bytes.Take(8).SequenceEqual(header))return Decode(bytes);if(bytes.Take(8).SequenceEqual(oldPasswordHeader))return DecodeLegacy(bytes,true);return DecodeLegacy(bytes,false);}
    }
}
