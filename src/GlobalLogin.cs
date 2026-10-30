using System;using System.IO;using System.Linq;using System.Text;using System.Diagnostics;using System.Collections.Generic;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Text.RegularExpressions;using System.Threading;
namespace BrowserManager {
 public static class AccountChecks {
  public static void RequireUniqueForeign(Data data,Account account){if(data==null||account.Region!="GLOBAL"||string.IsNullOrWhiteSpace(account.LoginAccount))return;if(data.Accounts.Any(a=>a.Id!=account.Id&&a.Region=="GLOBAL"&&GlobalIdentity.Same((a.LoginAccount??"").Trim(),account.LoginAccount.Trim())))throw new InvalidOperationException("这个外服邮箱已在列表中（包括已归档账号）。请编辑已有条目，或从“已归档”中恢复，原资料不会覆盖。");}
 }
 public static class GlobalIdentity {
  public static string Key(Account account){if(account==null||account.Region!="GLOBAL"||account.Archived)throw new InvalidOperationException("请选择未归档的外服账号。");string key=(account.LoginAccount??"").Trim();if(!Valid(key))throw new InvalidOperationException("请在“编辑备忘”填写这个外服账号的登录邮箱，再切换。手机号等其他方式仍可作为备忘保存。");return key;}
  public static bool Valid(string key){return !string.IsNullOrWhiteSpace(key)&&key.Length<=320&&key.Contains("@")&&!key.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||c==','||c=='"'||c=='\\')&&!key.Contains("://");}
  public static string First(string names){return (names??"").Split(',')[0].Trim();}
  public static bool Same(string a,string b){return string.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
 }
 public class GlobalConfig {
  public string Names;public bool AutoLogin;public List<string> Regions=new List<string>();
  static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}
  static void Collect(Dictionary<string,object> node,List<string> regions,int depth){if(depth>12)throw new InvalidDataException();foreach(var pair in node){if(pair.Key=="LastLoginRegion")regions.Add(Convert.ToString(pair.Value));var child=pair.Value as Dictionary<string,object>;if(child!=null)Collect(child,regions,depth+1);}}
  public static GlobalConfig Read(byte[] bytes){var root=Json().DeserializeObject(new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF')) as Dictionary<string,object>;object clientObject,names,auto;if(root==null||!root.TryGetValue("Client",out clientObject))throw new InvalidDataException();var client=clientObject as Dictionary<string,object>;if(client==null||!client.TryGetValue("SavedAccountNames",out names)||!(names is string))throw new InvalidDataException();bool enabled=client.TryGetValue("AutoLogin",out auto)&&string.Equals(Convert.ToString(auto),"true",StringComparison.OrdinalIgnoreCase);var state=new GlobalConfig{Names=(string)names,AutoLogin=enabled};Collect(root,state.Regions,0);return state;}
  public void RequireForeign(){if(Regions.Count==0||Regions.Any(r=>string.IsNullOrEmpty(r)||r.Equals("CN",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("请先在官方战网选择外服地区，再使用外服切换。国服继续使用网页登录。");}
  public static byte[] Patch(byte[] bytes,string selected){string text=new UTF8Encoding(false,true).GetString(bytes);var parsed=Read(bytes);var matches=Regex.Matches(text,@"""SavedAccountNames""\s*:\s*(?<value>""(?:[^""\\]|\\.)*"")");if(matches.Count!=1)throw new InvalidDataException();var span=matches[0].Groups["value"];if(Json().Deserialize<string>(span.Value)!=parsed.Names)throw new InvalidDataException();return new UTF8Encoding(false,true).GetBytes(text.Substring(0,span.Index)+Json().Serialize(selected)+text.Substring(span.Index+span.Length));}
 }
 public interface GlobalBackend {void Idle();byte[] Read();void Prepare(Data data,Account account);void Backup(byte[] data);void Swap(string key);void ClearChoice();void Launch();bool Revert(byte[] before,string key);}
 public static class GlobalLaunch {
  public static void Start(GlobalBackend backend,Data data,Account account,Action<string> progress=null){Run(backend,data,account,false,progress);}
  public static void PrepareLogin(GlobalBackend backend,Data data,Account account,Action<string> progress=null){Run(backend,data,account,true,progress);}

  // Both paths hold the same lock and check for configuration drift before changing selection.
  static void Run(GlobalBackend backend,Data data,Account account,bool firstLogin,Action<string> progress){
   string key=GlobalIdentity.Key(account);
   byte[] before=null,fresh=null;
   bool called=false;
   Action<string> stage=value=>{if(progress!=null)progress(value);};
   using(var gate=new Mutex(false,@"Local\BattleAccountManager.NativeConfig")){
    bool held=false;
    try{
     try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}
     if(!held)throw new InvalidOperationException("另一项账号切换仍在进行，请稍后再试。");
     stage("检查客户端退出状态");
     backend.Idle();
     before=backend.Read();
     var state=GlobalConfig.Read(before);
     state.RequireForeign();
     stage(state.AutoLogin?"保持登录：开启":"保持登录：关闭或未确认");
     if(!firstLogin&&!state.AutoLogin)throw new InvalidOperationException("战网的“保持登录”未开启。请点“账号密码登录”，手动登录时勾选“保持登录”，再正常退出战网。未修改账号选择。");
     stage("准备已验证的外服切换引擎");
     backend.Prepare(data,account);
     backend.Backup(before);
     backend.Idle();
     fresh=backend.Read();
     if(!fresh.SequenceEqual(before))throw new InvalidOperationException("战网配置刚发生变化，本次未切换，请稍后再试。");
     if(!firstLogin&&GlobalIdentity.Same(GlobalIdentity.First(state.Names),key)){
      stage("目标已是当前选择，原样启动");
      backend.Launch();
      return;
     }
     Array.Clear(fresh,0,fresh.Length);fresh=null;
     called=true;
     if(firstLogin){
      stage("清空账号选择，保留其他账号的登录资料");
      backend.ClearChoice();
      fresh=backend.Read();
      if(GlobalConfig.Read(fresh).Names!="")throw new InvalidOperationException("新登录窗口准备未完成，账号选择未清空。修改前备份已保留。");
      stage("打开官方登录窗口，等待用户手动认证");
      backend.Launch();
     }else{
      stage("按原版流程切换账号并启动");
      backend.Swap(key);
      fresh=backend.Read();
      if(!GlobalIdentity.Same(GlobalIdentity.First(GlobalConfig.Read(fresh).Names),key))throw new InvalidOperationException("原版切换返回后，账号选择未能核对。请核对官方客户端，修改前备份已保留。");
     }
     if(fresh!=null)stage(GlobalConfig.Read(fresh).AutoLogin?"启动后保持登录：开启（不代表认证有效）":"启动后保持登录：关闭或未确认，需要在官方登录页重新确认");
     stage("启动请求已发出，客户端登录结果待用户核对");
    }catch(CnEngine.PendingEngineException){stage("切换组件仍在运行，未回写配置，修改前备份已保留");throw;}catch(Exception error){
     if(called&&before!=null){
      bool restored=false;
      try{restored=backend.Revert(before,key);}catch{}
      stage(restored?"已撤回本次账号选择":"无法安全撤回，修改前备份已保留");
      if(!restored)throw new InvalidOperationException("外服启动未完成。修改前的加密备份已保留；请核对官方战网，不要反复点击。",error);
     }
     throw;
    }finally{
     if(before!=null)Array.Clear(before,0,before.Length);
     if(fresh!=null)Array.Clear(fresh,0,fresh.Length);
     if(held)gate.ReleaseMutex();
    }
   }
  }
 }
 public class TcNoGlobalBackend:GlobalBackend {
  public const string EngineHash="c22ddad5eb76d2b72a3fe527f831198688bfac33e3f4e5f676d42ab5ab6c5c1a";
  readonly string home,engineFolder,engineData;readonly bool skipRunningCheck;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
  public TcNoGlobalBackend(string storeHome,string appRoot,bool skipRunningCheck=false){home=storeHome;engineFolder=Path.Combine(appRoot,"Engine");engineData=Path.Combine(storeHome,"data","tcno");this.skipRunningCheck=skipRunningCheck;}
  static string ConfigPath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Battle.net\Battle.net.config");}}
  protected virtual bool ClientRunning(){foreach(string name in new[]{"Battle.net","Battle.net Helper","Battle.net Launcher"}){var processes=Process.GetProcessesByName(name);try{if(processes.Length!=0)return true;}finally{foreach(var p in processes)p.Dispose();}}return false;}
  void RequireIdle(){if(ClientRunning())throw new InvalidOperationException("切换前请从战网菜单正常“退出”，不要“退出登录”。游戏和更新程序无需退出，管家不会结束程序。若要保留原账号并手动登录另一份，请用“单独打开战网”。");}
  static void RequireEngineIdle(){var processes=Process.GetProcessesByName("TcNo-Acc-Switcher");try{if(processes.Length!=0)throw new CnEngine.PendingEngineException();}finally{foreach(var p in processes)p.Dispose();}}
  public void Idle(){RequireEngineIdle();if(!skipRunningCheck)RequireIdle();}
  public byte[] Read(){if(!File.Exists(ConfigPath)||new FileInfo(ConfigPath).Length>4*1024*1024)throw new InvalidOperationException("请先正常打开官方战网并选择外服地区。");return File.ReadAllBytes(ConfigPath);}
  static void CheckHash(string path,string expected){if(!File.Exists(path))throw new InvalidOperationException("缺少外服切换组件，请解压完整软件包。");using(var stream=File.OpenRead(path))using(var sha=SHA256.Create()){if(!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("外服切换组件与已验证版本不同，未执行切换。请使用完整软件包。");}}
  public static void CheckEngine(string path){CheckHash(path,EngineHash);CheckHash(Path.Combine(Path.GetDirectoryName(path),"Platforms-v4.0.8.json"),"e0a8814336fc58ae90ffa8417a626828316456031c095a4310d98382e4667404");}
  void Save(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(path));Store.WriteAtomic(path,new UTF8Encoding(false).GetBytes(json.Serialize(value)));}
  public void Prepare(Data data,Account selected){string key=GlobalIdentity.Key(selected);CheckEngine(Path.Combine(engineFolder,"TcNo-Acc-Switcher.exe"));var others=Process.GetProcessesByName("TcNo-Acc-Switcher");try{if(others.Length!=0)throw new InvalidOperationException("请先正常关闭其他 TcNo 窗口，再用账号管家切换。");}finally{foreach(var p in others)p.Dispose();}
   Directory.CreateDirectory(engineData);var ids=new Dictionary<string,string>();foreach(var account in data.Accounts.Where(a=>a.Region=="GLOBAL"&&!a.Archived)){string candidate=(account.LoginAccount??"").Trim();if(GlobalIdentity.Valid(candidate)){ids[candidate]=account.Id;Save(Path.Combine(engineData,"LoginCache","BattleNet",account.Id,"email.json"),candidate);}}ids[key]=selected.Id;Save(Path.Combine(engineData,"LoginCache","BattleNet",selected.Id,"email.json"),key);Save(Path.Combine(engineData,"LoginCache","BattleNet","ids.json"),new{ids=ids,lastused=new Dictionary<string,string>()});
   var settings=new Dictionary<string,object>{{"version",1},{"language","zh-CN"},{"userDataPath",engineData},{"offlineMode",true},{"statsEnabled",false},{"statsShare",false},{"crashReportAutoSubmit",false},{"discordRpc",false},{"prereleaseUpdates",false},{"protocolEnabled",false},{"alwaysRunAsAdmin",false},{"startTrayWithWindows",false},{"exitToTray",false},{"platformExePaths",new Dictionary<string,string>{{"BattleNet",OfficialClient.ClientPath}}}};
   Save(Path.Combine(engineFolder,"TcNo-Acc-Switcher.settings.json"),settings);
   var catalog=json.DeserializeObject(File.ReadAllText(Path.Combine(engineFolder,"Platforms-v4.0.8.json"),Encoding.UTF8)) as Dictionary<string,object>;var platforms=catalog["Platforms"] as Dictionary<string,object>;var descriptor=platforms["BattleNet"] as Dictionary<string,object>;descriptor["ExesToEnd"]=new string[0];Save(Path.Combine(engineData,"Platforms.custom.json"),new{Platforms=new Dictionary<string,object>{{"BattleNet",descriptor}}});Save(Path.Combine(engineData,"Settings","BattleNetSettings.json"),new{RunAsAdmin=false,AutoStart=true,ClosingMethod="TaskKill",ShowLastUsed=true,PullAccountImagesOnSwitch=false,AccountNotes=new Dictionary<string,string>()});
  }
  public void Backup(byte[] data){byte[] encoded=ProtectedData.Protect(data,Encoding.UTF8.GetBytes("BattleAccountManager.GlobalConfigBackup.v1"),DataProtectionScope.CurrentUser);try{Store.WriteAtomic(Path.Combine(home,"data","global-backups",DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".bin"),encoded);}finally{Array.Clear(encoded,0,encoded.Length);}}
  public void Swap(string key){if(!GlobalIdentity.Valid(key))throw new InvalidDataException();RunEngine("\"+b:"+key+"\"");}
  // This command only clears SavedAccountNames; it does not sign out at the service.
  public void ClearChoice(){RunEngine("logout:BattleNet");}
  void RunEngine(string command){Idle();CheckEngine(Path.Combine(engineFolder,"TcNo-Acc-Switcher.exe"));var start=new ProcessStartInfo(Path.Combine(engineFolder,"TcNo-Acc-Switcher.exe"),command+" --log-level=error"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=engineFolder};using(var process=Process.Start(start)){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(30000))throw new CnEngine.PendingEngineException();if(process.ExitCode!=0)throw new InvalidOperationException("外服切换组件返回错误（"+process.ExitCode+"），未将日志中的账号内容显示出来。");}}
  public void Launch(){Idle();using(var p=Process.Start(new ProcessStartInfo(OfficialClient.ClientPath){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(OfficialClient.ClientPath)})){} }
  // Automatic rollback must not overwrite a running client's configuration even when launch checks are skipped.
  void Write(byte[] data){RequireEngineIdle();RequireIdle();string temp=ConfigPath+".global-manager-"+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data,0,data.Length);stream.Flush(true);}RequireEngineIdle();RequireIdle();File.Replace(temp,ConfigPath,null);}finally{if(File.Exists(temp))File.Delete(temp);}}
  public bool Revert(byte[] before,string key){RequireEngineIdle();RequireIdle();byte[] current=Read(),next=GlobalConfig.Patch(before,key),clear=GlobalConfig.Patch(before,"");try{if(current.SequenceEqual(before))return true;if(!current.SequenceEqual(next)&&!current.SequenceEqual(clear))return false;Write(before);byte[] checkedData=Read();try{return checkedData.SequenceEqual(before);}finally{Array.Clear(checkedData,0,checkedData.Length);}}finally{foreach(var bytes in new[]{current,next,clear})Array.Clear(bytes,0,bytes.Length);}}
 }
 public static class GlobalImport {
  public static string DecodeKey(byte[] encoded,string slot){if(encoded==null||encoded.Length>128*1024||encoded.Length<9||Encoding.ASCII.GetString(encoded,0,8)!="GBRO0001")throw new InvalidDataException();byte[] raw=ProtectedData.Unprotect(encoded.Skip(8).ToArray(),Encoding.UTF8.GetBytes("GlobalLoginCheck.ReadOnly.v1"),DataProtectionScope.CurrentUser);try{var root=new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(raw)) as Dictionary<string,object>;if(root==null||Convert.ToInt32(root["Format"])!=1||Convert.ToString(root["Slot"])!=slot)throw new InvalidDataException();var regions=root["Regions"] as Dictionary<string,object>;if(regions==null||!regions.Values.Any(r=>Convert.ToString(r)!="CN"))throw new InvalidDataException();string key=GlobalIdentity.First(Convert.ToString(root["SavedNames"]));if(!GlobalIdentity.Valid(key))throw new InvalidDataException();return key;}finally{Array.Clear(raw,0,raw.Length);}}
  public static int Merge(Data data,IEnumerable<KeyValuePair<string,string>> keys){int added=0;foreach(var pair in keys){if(!GlobalIdentity.Valid(pair.Value))throw new InvalidDataException();if(data.Accounts.Any(a=>a.Region=="GLOBAL"&&GlobalIdentity.Same((a.LoginAccount??"").Trim(),pair.Value)))continue;var account=Account.New("外服 "+pair.Key,"GLOBAL");account.LoginAccount=pair.Value;account.LoginMethod="官方客户端保持登录";data.Accounts.Add(account);added++;}Store.Validate(data);return added;}
  public static int FromProbe(string appRoot,Data data){string sibling=Path.Combine(Directory.GetParent(Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar)).FullName,"外服登录只读核对-v0.1","private-data");var keys=new List<KeyValuePair<string,string>>();foreach(string slot in new[]{"A","B"}){string path=Path.Combine(sibling,"foreign-"+slot+".bin");if(File.Exists(path))keys.Add(new KeyValuePair<string,string>(slot,DecodeKey(File.ReadAllBytes(path),slot)));}return Merge(data,keys);}
 }
}
