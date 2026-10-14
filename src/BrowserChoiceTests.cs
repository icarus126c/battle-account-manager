using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;
namespace BrowserManager {
    public static class BrowserChoiceTests {
        static bool Reject(Action action){try{action();return false;}catch{return true;}}
        public static void Run(string report){
            var results=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);results.Add("通过："+label);};
            string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"browser-choice-fixture");var a=Account.New("虚构 A","CN");var b=Account.New("虚构 B","CN");var data=new Data{Version=2,Accounts=new List<Account>{a,b}};
            Store.Validate(data);check(a.LoginBrowser=="webview2"&&a.ExternalBrowser=="chrome","旧条目和新条目默认使用内置登录窗口，外部浏览器默认 Chrome");
            foreach(string mode in new[]{"chrome","edge"}){
                string pa=Browser.Profile(root,a.Id,mode),pb=Browser.Profile(root,b.Id,mode);check(pa!=pb&&pa.EndsWith(Path.Combine(mode,a.Id)),mode+" 按账号标识分开保存资料");
                string url=CnDirect.Tests.CurrentDummy+"&opaque=FAKE%2B%2F%3D";string exe=Path.Combine(root,"虚构安装目录",mode+".exe");var start=Browser.BuildStartInfo(root,a.Id,url,mode,exe);
                check(start.FileName==exe&&!start.UseShellExecute&&start.Arguments.StartsWith("--user-data-dir="+SessionProbe.BrowserProfiles.Quote(pa)+" ")&&start.Arguments.EndsWith(SessionProbe.BrowserProfiles.Quote(url)),mode+" 使用指定程序及指定账号目录，临时请求原文不变");
                check(Reject(()=>Browser.BuildStartInfo(root,a.Id,"https://evil.invalid/",mode,exe)),mode+" 拒绝非国服登录入口");
                check(Reject(()=>Browser.Profile(root,"../shared",mode)),mode+" 拒绝越界账号标识");
            }
            check(Browser.Profile(root,a.Id)==Browser.Profile(root,a.Id,"chrome"),"旧 Chrome 路径及接口保持兼容");
            check(Browser.Profile(root,a.Id,"edge")!=Browser.Profile(root,a.Id,"chrome")&&Browser.Profile(root,a.Id,"edge")!=EmbeddedPolicy.DataRoot(root),"Edge、Chrome、WebView2 三种资料分开保存");
            check(Reject(()=>Browser.Profile(root,a.Id,"unknown"))&&Reject(()=>Browser.Executable("webview2")),"无效选择报错，不自动切换到其他浏览器");
            foreach(string mode in new[]{"webview2","chrome","edge"}){
                a.LoginBrowser=mode;a.ExternalBrowser=mode=="edge"?"edge":"chrome";a.Password="FAKE_ONLY_SECRET";a.AuthenticatorMemo="虚构将军令所在手机";
                var decoded=Store.Decode(Store.Encode(data)).Accounts[0];check(decoded.LoginBrowser==mode&&decoded.ExternalBrowser==a.ExternalBrowser&&decoded.Password==a.Password&&decoded.AuthenticatorMemo==a.AuthenticatorMemo&&decoded.Id==a.Id,"加密保存 "+mode+" 偏好并保留账号、密码和将军令备忘");
                var route=new LoginRoute(a);a.LoginBrowser=mode=="edge"?"chrome":"edge";a.Name="后改的名称";
                check(route.Mode==mode&&route.IsEmbedded==(mode=="webview2")&&route.Name!="后改的名称"&&route.Id==a.Id,"准备 "+mode+" 请求时绑定当时账号和方式，后续修改不串号");a.Name="虚构 A";
            }
            a.LoginBrowser="edge";a.ExternalBrowser="edge";var edited=a.Copy();edited.Note="编辑后的虚构备注";check(edited.LoginBrowser=="edge"&&edited.ExternalBrowser=="edge","编辑备忘所用的副本保留浏览器偏好");
            a.LoginBrowser="invalid";check(Reject(()=>Store.Encode(data)),"非法登录方式不能写入账号资料");a.LoginBrowser="webview2";a.ExternalBrowser="invalid";check(Reject(()=>Store.Encode(data)),"非法外部浏览器不能写入账号资料");
            string raw="{\"Version\":2,\"Accounts\":[{\"Id\":\""+a.Id+"\",\"Name\":\"旧版虚构账号\",\"Region\":\"CN\",\"Password\":\"FAKE_OLD_ONLY\"}]}";
            byte[] encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(raw),Encoding.UTF8.GetBytes("BattleAccountManager.Vault.v2"),DataProtectionScope.CurrentUser);var old=Store.Decode(Encoding.ASCII.GetBytes("BAMV0200").Concat(encrypted).ToArray()).Accounts[0];check(old.LoginBrowser=="webview2"&&old.ExternalBrowser=="chrome"&&old.Password=="FAKE_OLD_ONLY"&&old.Id==a.Id,"缺少新增字段的旧 v2 加密备忘可读取并保留原标识与密码");
            b.Region="GLOBAL";check(Reject(()=>new LoginRoute(b)),"外服备忘不误用国服网页登录接管");b.Region="CN";b.Archived=true;check(Reject(()=>new LoginRoute(b)),"归档账号不能准备登录请求");
            results.Add("合计 "+results.Count+" 项通过。仅使用虚构资料和虚构安装路径，没有打开真实登录页或写入真实账号资料。");File.WriteAllLines(report,results,new UTF8Encoding(true));
        }
    }
}
