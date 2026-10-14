using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace BrowserManager {
    public sealed class PortableEnvelope {
        public int Format { get; set; }
        public string Purpose { get; set; }
        public DateTime CreatedUtc { get; set; }
        public Data Accounts { get; set; }
    }

    // Deliberately does not call Store.Encode or DPAPI: only the transfer password
    // protects this portable file. Store.Save re-encrypts imports on the target PC.
    public static class PortableAccounts {
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("BAMPORT1");
        const int MaxPacket=17*1024*1024+8, MaxPlain=15*1024*1024;
        const string Purpose="BattleAccountManager.PortableAccounts.v1";
        public static void CheckPassword(string password) {
            if(string.IsNullOrWhiteSpace(password)||password.Length<10||password.Length>256)
                throw new InvalidOperationException("迁移密码需要 10–256 个字符，不能全为空白。建议使用较长且独有的短句。");
        }
        static Data PortableCopy(Data data) {
            if(data==null||data.Version!=2||data.Accounts==null||data.Accounts.Any(a=>a==null))throw new InvalidDataException();
            var copy=new Data { Version=2, Accounts=data.Accounts.Select(a=>a.Copy()).ToList(), Security=null };
            Store.Validate(copy);
            foreach(var a in copy.Accounts){a.LoginState="pending";a.LastLoginAction="";a.LoginCheckedUtc=DateTime.MinValue;a.LastUsed=DateTime.MinValue;}
            return copy;
        }
        public static void Clear(Data data) {
            if(data==null)return;
            if(data.Accounts!=null){foreach(var a in data.Accounts)if(a!=null){a.Password=a.LoginAccount=a.Username=a.Name=a.Note=a.AuthenticatorMemo=a.LoginMethod="";}data.Accounts.Clear();}
            data.Security=null;
        }
        public static byte[] Encode(Data data,string password) { return Encode(data,password,MasterKey.DefaultIterations); }
        internal static byte[] Encode(Data data,string password,int rounds) {
            CheckPassword(password);Data copy=PortableCopy(data);byte[] raw=null,encrypted=null;
            try {
                var envelope=new PortableEnvelope { Format=1,Purpose=Purpose,CreatedUtc=DateTime.UtcNow,Accounts=copy };
                raw=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=MaxPlain}.Serialize(envelope));
                if(raw.Length>MaxPlain)throw new InvalidOperationException("账号资料过大，未创建迁移包。");
                using(var key=MasterKey.Create(password,rounds))encrypted=key.Seal(raw);
                var result=new byte[Magic.Length+encrypted.Length];
                Buffer.BlockCopy(Magic,0,result,0,Magic.Length);Buffer.BlockCopy(encrypted,0,result,Magic.Length,encrypted.Length);
                return result;
            } finally {Clear(copy);if(raw!=null)Array.Clear(raw,0,raw.Length);if(encrypted!=null)Array.Clear(encrypted,0,encrypted.Length);}
        }
        public static PortableEnvelope Decode(byte[] packet,string password) {
            CheckPassword(password);byte[] encrypted=null,raw=null;PortableEnvelope result=null;
            try {
                if(packet==null||packet.Length<120||packet.Length>MaxPacket||!packet.Take(8).SequenceEqual(Magic))throw new InvalidDataException();
                encrypted=packet.Skip(8).ToArray();
                if(!MasterKey.Protected(encrypted)||MasterKey.HasFactor(encrypted))throw new InvalidDataException();
                using(var key=MasterKey.Derive(password,encrypted))raw=key.Open(encrypted);
                if(raw.Length>MaxPlain)throw new InvalidDataException();
                result=new JavaScriptSerializer{MaxJsonLength=MaxPlain,RecursionLimit=32}.Deserialize<PortableEnvelope>(new UTF8Encoding(false,true).GetString(raw));
                if(result==null||result.Format!=1||result.Purpose!=Purpose||result.Accounts==null||result.Accounts.Security!=null)throw new InvalidDataException();
                Store.Validate(result.Accounts);
                var safe=PortableCopy(result.Accounts);Clear(result.Accounts);result.Accounts=safe;
                return result;
            } catch {
                if(result!=null&&result.Accounts!=null&&result.Accounts.Accounts!=null)Clear(result.Accounts);
                throw new InvalidOperationException("无法打开迁移包：密码不正确、文件损坏或格式不受支持。原资料未改动。");
            } finally {if(raw!=null)Array.Clear(raw,0,raw.Length);if(encrypted!=null)Array.Clear(encrypted,0,encrypted.Length);}
        }
        public static PortableEnvelope Read(string path,string password) {
            byte[] packet=null;
            try {
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(file.Length<120||file.Length>MaxPacket)throw new InvalidOperationException("文件大小不符合迁移包要求。");
                    packet=new byte[(int)file.Length];int read=0;
                    while(read<packet.Length){int n=file.Read(packet,read,packet.Length-read);if(n==0)throw new EndOfStreamException();read+=n;}
                }
                return Decode(packet,password);
            } finally {if(packet!=null)Array.Clear(packet,0,packet.Length);}
        }
    }
}
