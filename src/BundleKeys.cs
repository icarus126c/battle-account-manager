using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
namespace BrowserManager {
    public sealed class BundleEnvelope { public int Format {get;set;} public string Keys {get;set;} public string VaultHash {get;set;} public Data Preview {get;set;} public DateTime CreatedUtc {get;set;} }
    public partial class Store {
        static readonly byte[] bundleEntropy=Encoding.UTF8.GetBytes("BattleAccountManager.BundleKeys.v1");
        public byte[] SealBundleKeys(byte[] keys,string vaultHash) {
            lock(protectionSync){if(IsLocked)throw new VaultLockedException();var preview=Load().Copy();foreach(var a in preview.Accounts){a.Password="";a.LoginAccount="";a.Username="";a.Note="";a.AuthenticatorMemo="";a.LoginMethod="";}
                var envelope=new BundleEnvelope{Format=1,Keys=Convert.ToBase64String(keys),VaultHash=vaultHash,Preview=preview,CreatedUtc=DateTime.UtcNow};byte[] raw=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(envelope)),inner=null;
                try{inner=ProtectedData.Protect(raw,bundleEntropy,DataProtectionScope.CurrentUser);return masterKey==null?(byte[])inner.Clone():masterKey.Seal(inner,preview.Security!=null);}finally{Array.Clear(raw,0,raw.Length);if(inner!=null)Array.Clear(inner,0,inner.Length);}
            }
        }
        public BundleEnvelope OpenBundleKeys(byte[] packet,string password=null,string code=null) {
            lock(protectionSync){if(IsLocked)throw new VaultLockedException();byte[] inner=null,raw=null;MasterKey key=null;
                try{if(MasterKey.Protected(packet)){if(password!=null)key=MasterKey.Derive(password,packet);try{inner=(key??masterKey).Open(packet);}catch{if(password==null)throw new VaultLockedException();throw;}}else inner=(byte[])packet.Clone();
                    raw=ProtectedData.Unprotect(inner,bundleEntropy,DataProtectionScope.CurrentUser);var result=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<BundleEnvelope>(Encoding.UTF8.GetString(raw));
                    if(result==null||result.Format!=1||Convert.FromBase64String(result.Keys).Length!=64||result.VaultHash==null||result.VaultHash.Length!=64)throw new InvalidDataException();Validate(result.Preview);
                    if(MasterKey.HasFactor(packet)!=(result.Preview.Security!=null))throw new InvalidDataException();
                    if(result.Preview.Security!=null){bool current=password==null&&masterKey!=null&&Load().Security!=null&&Load().Security.Secret==result.Preview.Security.Secret;if(!current){if(code==null)throw new VaultLockedException();Totp.Verify(result.Preview.Security,code,Now());}}
                    result.Preview.Security=null;return result;
                }finally{if(key!=null)key.Dispose();foreach(var bytes in new[]{inner,raw})if(bytes!=null)Array.Clear(bytes,0,bytes.Length);}
            }
        }
        void DailyBackup() {
            if(!File.Exists(FilePath))return;string file=Path.Combine(Home,"backups","accounts",DateTime.Now.ToString("yyyy-MM-dd")+".bav");if(File.Exists(file))return;
            WriteWithoutHistory(file,ReadProtected(FilePath));
        }
    }
}
