using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Security.Cryptography;using System.Web.Script.Serialization;
namespace BrowserManager {
 public class NativeSelection {
  public int Format {get;set;} public string AccountId {get;set;} public string Region {get;set;}
  public string ClientVersion {get;set;} public string SavedNames {get;set;} public DateTime RegisteredUtc {get;set;}
 }
 public static class NativeSelections {
  static readonly byte[] Header=Encoding.ASCII.GetBytes("BNCSEL01");
  static readonly byte[] Entropy=Encoding.UTF8.GetBytes("BattleAccountManager.NativeSelection.v1");
  public static string PathFor(string home,string id){if(!Browser.ValidId(id))throw new InvalidDataException();return Path.Combine(Path.GetFullPath(home),"data","native-cn",id+".bin");}
  public static bool Exists(string home,string id){return File.Exists(PathFor(home,id));}
  public static void Validate(NativeSelection s,string id){if(s==null||s.Format!=1||s.Region!="CN"||s.AccountId!=id||!Browser.ValidId(id)||string.IsNullOrWhiteSpace(s.ClientVersion)||s.ClientVersion.Length>4096||string.IsNullOrWhiteSpace(s.SavedNames)||s.SavedNames.Length>16384)throw new InvalidDataException();}
  public static byte[] Encode(NativeSelection s){Validate(s,s.AccountId);byte[] plain=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(s));try{return Header.Concat(ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser)).ToArray();}finally{Array.Clear(plain,0,plain.Length);}}
  public static NativeSelection Decode(byte[] bytes,string id){if(bytes==null||bytes.Length<9||bytes.Length>128*1024||!bytes.Take(8).SequenceEqual(Header))throw new InvalidDataException();byte[] plain=ProtectedData.Unprotect(bytes.Skip(8).ToArray(),Entropy,DataProtectionScope.CurrentUser);try{var s=new JavaScriptSerializer().Deserialize<NativeSelection>(Encoding.UTF8.GetString(plain));Validate(s,id);return s;}finally{Array.Clear(plain,0,plain.Length);}}
  public static NativeSelection Load(string home,string id){string path=PathFor(home,id);if(!File.Exists(path))throw new InvalidOperationException("请先完成此账号的国服登录，再点击“登记当前客户端账号”。");if(new FileInfo(path).Length>128*1024)throw new InvalidDataException();return Decode(File.ReadAllBytes(path),id);}
  public static void Save(string home,NativeSelection s){byte[] encoded=Encode(s);var verify=Decode(encoded,s.AccountId);if(verify.SavedNames!=s.SavedNames||verify.ClientVersion!=s.ClientVersion)throw new InvalidDataException();Store.WriteAtomic(PathFor(home,s.AccountId),encoded);}
 }
 public interface NativeClientBackend {
  void Idle();byte[] ReadConfig();void WriteConfig(byte[] bytes);string Version {get;}void Launch();
 }
 public static class NativeLogin {
  static JavaScriptSerializer Parser(){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}
  public static Dictionary<string,object> Parse(byte[] bytes){var root=Parser().DeserializeObject(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')) as Dictionary<string,object>;if(root==null)throw new InvalidOperationException("战网配置无法识别，请先正常打开官方战网。");return root;}
  static Dictionary<string,object> Client(Dictionary<string,object> root){object value;var client=root.TryGetValue("Client",out value)?value as Dictionary<string,object>:null;if(client==null)throw new InvalidOperationException("战网配置结构已变化，请先保留此提示。");return client;}
  public static string SavedNames(byte[] bytes){object names;var c=Client(Parse(bytes));if(!c.TryGetValue("SavedAccountNames",out names)||!(names is string)||string.IsNullOrWhiteSpace((string)names))throw new InvalidOperationException("尚未发现记住的账号，请先完成一次官方国服登录。");return (string)names;}
  static IEnumerable<string> Regions(Dictionary<string,object> root,int depth){if(depth>10)throw new InvalidDataException();foreach(var pair in root){if(pair.Key=="LastLoginRegion")yield return Convert.ToString(pair.Value);var child=pair.Value as Dictionary<string,object>;if(child!=null)foreach(var region in Regions(child,depth+1))yield return region;}}
  public static void CheckCN(byte[] bytes) {
   var root=Parse(bytes);
   var client=Client(root);
   var regions=Regions(root,0).ToArray();
   if(regions.Length==0||regions.Any(r=>!string.Equals(r,"CN",StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("请先将官方战网切到国服，并正常退出后再使用国服客户端登录。");
   object auto;
   if(client.TryGetValue("AutoLoginCN",out auto)) {
    // An explicit disabled (or unrecognized) value must not be overridden.
    if(!string.Equals(Convert.ToString(auto),"true",StringComparison.OrdinalIgnoreCase))
     throw new InvalidOperationException("战网配置明确未开启国服保持登录。请在官方客户端完成保持登录后再试；原登记已保留。");
   } else if(!client.TryGetValue("AutoLogin",out auto)||!string.Equals(Convert.ToString(auto),"true",StringComparison.OrdinalIgnoreCase)) {
    // Missing optional CN flag is not evidence that an existing session is invalid.
    throw new InvalidOperationException("尚无法确认战网已开启保持登录。请在官方客户端确认保持登录后再试；无需先覆盖原登记。");
   }
   SavedNames(bytes);
  }
  public static byte[] StableRead(NativeClientBackend backend){byte[] last=null;try{for(int i=0;i<6;i++){byte[] now=backend.ReadConfig();if(last!=null&&last.SequenceEqual(now)){Array.Clear(last,0,last.Length);last=null;return now;}if(last!=null)Array.Clear(last,0,last.Length);last=now;System.Threading.Thread.Sleep(150);}throw new InvalidOperationException("战网配置仍在变化，请稍后重试。");}finally{if(last!=null)Array.Clear(last,0,last.Length);}}
  public static void SaveBackup(string home,byte[] bytes){byte[] plain=(byte[])bytes.Clone();try{byte[] encrypted=ProtectedData.Protect(plain,Encoding.UTF8.GetBytes("BattleAccountManager.NativeConfigBackup.v1"),DataProtectionScope.CurrentUser);Store.WriteAtomic(Path.Combine(home,"data","native-cn","last-config-before.bin"),Encoding.ASCII.GetBytes("BNCBACK1").Concat(encrypted).ToArray());}finally{Array.Clear(plain,0,plain.Length);}}
 }
 public class OfficialNativeClient {
  public static string ClientPath {get{return OfficialClient.ClientPath;}}
  static string ConfigPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Battle.net\\Battle.net.config");}}
  public string Version {get{string path=ClientPath;return FileVersionInfo.GetVersionInfo(path).FileVersion+"|"+string.Join(";",Directory.GetDirectories(Path.GetDirectoryName(path),"Battle.net.*").Select(Path.GetFileName).OrderBy(x=>x).ToArray());}}
  public byte[] ReadConfig(){if(!File.Exists(ConfigPath)||new FileInfo(ConfigPath).Length>4*1024*1024)throw new InvalidOperationException("没有可用的战网配置，请先完成一次官方国服登录。");return File.ReadAllBytes(ConfigPath);}
 }
}
