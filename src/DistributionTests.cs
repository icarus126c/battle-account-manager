using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public static class DistributionTests {
        static bool Reject(Action action){try{action();return false;}catch{return true;}}
        static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var d in Walk(child))yield return d;}
        static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
        public static void Run(string report){
            string parent=Path.GetDirectoryName(Path.GetFullPath(report));Directory.CreateDirectory(parent);string root=Path.Combine(parent,"distribution-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};string originalHome=OfficialClient.SettingsHome;
            try {
                string home=Path.Combine(root,"home"),a=Path.Combine(root,"中文 自定义位置","Battle.net.exe"),b=Path.Combine(root,"another","Battle.net.exe");
                check(OfficialClient.ReadPreference(home)==null,"首次使用没有指定位置时允许自动查找");
                check(OfficialClient.Resolve(a,new[]{b},p=>p==a)==a,"手动选择优先于自动发现，支持中文空格路径");
                check(Reject(()=>OfficialClient.Resolve(a,new[]{b},p=>p==b)),"手动位置失效时提示重新选择，不悄悄启动另一份客户端");
                check(OfficialClient.Resolve(null,new[]{a,b},p=>p==b)==b,"自动发现跳过无效候选");
                check(Reject(()=>OfficialClient.Resolve(null,new string[0],p=>true)),"未安装战网时给出可操作提示");
                OfficialClient.SavePreference(home,a,p=>p==a);
                check(OfficialClient.ReadPreference(home)==a,"自定义位置写入资料目录并可重新读取");
                check(Reject(()=>OfficialClient.SavePreference(home,b,p=>false))&&OfficialClient.ReadPreference(home)==a,"选错文件不覆盖原位置");
                check(!OfficialClient.IsClient(AppDomain.CurrentDomain.BaseDirectory+"BattleAccountManager.exe"),"不把管家自身识别为战网客户端");
                Directory.CreateDirectory(Path.GetDirectoryName(a));File.WriteAllText(a,"not a PE executable");check(!OfficialClient.IsClient(a),"同名文本文件不被识别为官方战网");
                check(OfficialClient.Candidate("\"D:\\游戏\\Battle.net\"")==@"D:\游戏\Battle.net\Battle.net.exe","注册表中的目录转换为主程序候选");
                OfficialClient.ClearPreference(home);check(OfficialClient.ReadPreference(home)==null,"恢复自动查找只删除位置设置");
                File.WriteAllText(OfficialClient.PreferencePath(home),"{broken");check(Reject(()=>OfficialClient.ReadPreference(home)),"损坏的位置设置给出恢复入口");OfficialClient.ClearPreference(home);
                string installed=OfficialClient.Candidates().FirstOrDefault(OfficialClient.IsClient);
                if(installed!=null){
                    File.Copy(installed,a,true);OfficialClient.SavePreference(home,a);OfficialClient.SettingsHome=home;
                    check(OfficialClient.ClientPath==a&&OfficialNativeClient.ClientPath==a&&CnDirect.Hook.ClientPath==a,"真实主程序副本位于自定义目录时，国服／外服／网页登录统一识别");
                    string engineRoot=Path.Combine(root,"fake-package");Directory.CreateDirectory(Path.Combine(engineRoot,"Engine"));foreach(string name in new[]{"TcNo-Acc-Switcher.exe","Platforms-v4.0.8.json"})File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Engine",name),Path.Combine(engineRoot,"Engine",name));
                    var fake=Store.Initial();var foreign=Account.New("FAKE","GLOBAL");foreign.LoginAccount="test@example.invalid";fake.Accounts.Add(foreign);new TcNoGlobalBackend(home,engineRoot).Prepare(fake,foreign);
                    Func<bool> settingMatches=()=>{var settings=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(engineRoot,"Engine","TcNo-Acc-Switcher.settings.json"))) as Dictionary<string,object>;return Convert.ToString(((Dictionary<string,object>)settings["platformExePaths"])["BattleNet"])==a;};
                    check(settingMatches(),"外服引擎接收到用户选择的客户端位置（不执行切换）");
                    new CnEngine.OriginalEngine(engineRoot,Path.Combine(home,"cn-exp")).Prepare(new[]{new NativeSelection{Format=1,Region="CN",AccountId=Guid.NewGuid().ToString("N"),SavedNames="12345",ClientVersion="FAKE",RegisteredUtc=DateTime.UtcNow}});
                    check(settingMatches(),"国服引擎接收到同一客户端位置（不执行切换）");
                    OfficialClient.ClearPreference(home);OfficialClient.SettingsHome=originalHome;
                }else rows.Add("跳过：未安装真实战网，无法核对主程序版本资料；解析和保存的虚构路径测试已执行。");
                string deep=Path.Combine(root,new string('a',80),new string('b',80),"vault");var deepStore=new Store(deep);deepStore.Save(Store.Initial());check(deepStore.FilePath.Length>260&&deepStore.Load().Accounts.Count==0,"超过 260 字符的资料路径仍可创建及读取账号库");
                var account=Account.New("虚构外服","GLOBAL");account.LoginAccount="fixture@example.invalid";
                Func<string,bool,string,byte[]> config=(key,auto,region)=>Encoding.UTF8.GetBytes("{\"Client\":{\"SavedAccountNames\":\""+key+"\",\"AutoLogin\":"+(auto?"true":"false")+",\"LastLoginRegion\":\""+region+"\"}}");
                check(!Reject(()=>LoginGuide.CheckGlobal(config(account.LoginAccount,true,"US"),account)),"首次流程核对正确邮箱和保持登录设置");
                check(Reject(()=>LoginGuide.CheckGlobal(config(account.LoginAccount,false,"US"),account)),"未勾选保持登录时提示重新认证");
                check(Reject(()=>LoginGuide.CheckGlobal(config("wrong@example.invalid",true,"US"),account)),"刚退出其他账号时阻止误认登记成功");
                check(Reject(()=>LoginGuide.CheckGlobal(config(account.LoginAccount,true,"CN"),account)),"外服设置检查拒绝国服配置");
                check(account.LoginState==null&&account.LoginCheckedUtc==DateTime.MinValue,"本地设置检查不自动标记服务器登录成功");
                check(LoginGuide.Steps("GLOBAL").Any(s=>s.Contains("保持登录"))&&LoginGuide.Steps("CN").Any(s=>s.Contains("网页登录")),"引导按国服和外服分别说明");
                string install=Path.Combine(root,"fresh-app"),freshHome=Path.Combine(root,"fresh-home");Directory.CreateDirectory(install);DataDirectory.Ensure(install,freshHome,null,p=>{});var fresh=new Store(freshHome);check(fresh.Load().Accounts.Count==0&&DataDirectory.Ready(freshHome),"全新安装创建空账号库，不依赖旧版本或测试账号");
                var w=new MainWindow(true,"CN");try{var labels=Walk((DependencyObject)w.Content).OfType<Button>().Select(x=>Convert.ToString(x.Content)).ToArray();check(labels.Contains("环境与战网位置")&&!labels.Any(s=>s.Contains("导入已验证")),"界面有环境入口且不包含私人试验记录入口");check(!Field<Expander>(w,"gettingStarted").IsExpanded,"已有账号的首次引导默认收起，主操作优先");check(Field<Expander>(w,"cnExperiment").IsExpanded,"国服实验入口默认展开");Entry.Render(w,Path.Combine(parent,"preview-cn.png"),1100,920);}finally{w.Close();}
                var g=new MainWindow(true,"GLOBAL");try{check(Field<Button>(g,"guideCheck").Visibility==Visibility.Visible&&Field<TextBlock>(g,"guideText").Text.Contains("保持登录"),"外服引导显示只读保持登录检查入口");Entry.Render(g,Path.Combine(parent,"preview-global.png"),1100,920);}finally{g.Close();}
                var dialog=new EnvironmentDialog(null,home,install,()=>false);try{dialog.Apply(new EnvironmentReport{Client="未找到官方战网，请选择安装目录里的 Battle.net.exe。",WebView="不可用：安装 WebView2，或改用 Chrome / Edge",Chrome="未找到（可选）",Edge="C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",Engine="组件缺失，请重新解压完整软件包。"});Entry.Render(dialog,Path.Combine(parent,"preview-environment.png"),700,730);check(!File.Exists(OfficialClient.PreferencePath(home)),"环境页面和静态预览不写入客户端位置");}finally{dialog.Close();}
                var system=EnvironmentCheck.Read(home,install);check(!system.EngineReady&&system.Engine.Contains("重新解压"),"缺少切换组件时提供解压指引，不当作认证失败");
                rows.Add("合计 "+rows.Count+" 项通过。全程使用虚构账号；没有启动真实战网或写入真实账号资料。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
            }finally{OfficialClient.SettingsHome=originalHome;string expected=Path.GetFullPath(parent)+Path.DirectorySeparatorChar;if(Path.GetFullPath(root).StartsWith(expected,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("distribution-fixture-",StringComparison.Ordinal))Directory.Delete(root,true);}
        }
    }
}
