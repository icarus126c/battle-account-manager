using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public static class EditionTests {
        static IEnumerable<DependencyObject> Walk(DependencyObject obj) {
            yield return obj;
            foreach (var child in LogicalTreeHelper.GetChildren(obj).OfType<DependencyObject>())
                foreach (var item in Walk(child)) yield return item;
        }
        public static void Run(string report) {
            var rows = new List<string>();
            Action<bool,string> check = (ok, name) => { if (!ok) throw new Exception(name); rows.Add("通过："+name); };
            var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "edition-fixture-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                check(new[]{"自用版","友情分享版","赞助版"}.Contains(Edition.Name),"编译版本名称明确");
                check(Edition.Sponsorship == (Edition.Name == "赞助版"),"只有赞助版启用赞助展示");
                foreach(string url in new[]{"file:///C:/fake","javascript:alert(1)","http://example.invalid","https://user:pass@example.invalid/","https://127.0.0.1/","https://localhost/","https://example.invalid:8443/"})
                    check(!SponsorPolicy.ValidUrl(url),"拒绝不适合的赞助链接："+url.Split(':')[0]);
                check(SponsorPolicy.ValidUrl("https://example.invalid/offer"),"接受明确的 HTTPS 外部链接");
                check(SponsorPolicy.Parse("{broken")==null&&SponsorPolicy.Parse(new string('x',8193))==null,"损坏或超长配置隐藏卡片，不影响登录");
                string json="{\"Enabled\":true,\"Sponsor\":\"FAKE\",\"Title\":\"FAKE CARD\",\"Description\":\"FAKE DESCRIPTION\",\"Url\":\"\"}";
                check(SponsorPolicy.Parse(json)!=null&&SponsorPolicy.Parse(json.Replace("true","false"))==null,"支持无链接示例和关闭投放");
                File.WriteAllText(Path.Combine(root,"sponsor.json"),json);
                string home=Path.Combine(root,"home");Directory.CreateDirectory(Path.Combine(home,"data"));
                string vault=Path.Combine(home,"data","vault-v2.bin");File.WriteAllText(vault,"FAKE_VAULT_UNCHANGED");
                SponsorPolicy.SetHidden(home,true);check(SponsorPolicy.Hidden(home),"关闭状态保存到独立偏好文件");
                SponsorPolicy.SetHidden(home,false);check(!SponsorPolicy.Hidden(home),"可重新显示卡片");
                var shell=SponsorCard.Create(home,root,false);
                var buttons=Walk(shell).OfType<Button>().ToArray();
                if(Edition.Sponsorship) {
                    check(buttons.Length==2,"无链接示例只有关闭和重新显示，不含虚假合作跳转");
                    buttons.Single(b=>Convert.ToString(b.Content)=="关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(SponsorPolicy.Hidden(home),"实际关闭按钮持久保存关闭状态");
                    var reopened=SponsorCard.Create(home,root,false);
                    check(Walk(reopened).OfType<Border>().Single().Visibility==Visibility.Collapsed,"重新创建窗口仍保持关闭");
                    Walk(reopened).OfType<Button>().Single(b=>Convert.ToString(b.Content)=="显示赞助卡片").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(!SponsorPolicy.Hidden(home),"重新显示入口可恢复卡片");
                } else check(!buttons.Any()&&!Walk(shell).OfType<Border>().Any(),"无广告版本即使存在赞助配置也不显示卡片");
                check(File.ReadAllText(vault)=="FAKE_VAULT_UNCHANGED","赞助偏好不改写账号密码库");
                check(!Directory.GetFiles(Path.Combine(home,"data")).Any(p=>Path.GetFileName(p).Contains("track")),"没有创建跟踪数据文件");
                var window=new MainWindow(true,"CN");
                try {
                    check(window.Title.Contains(Edition.Name),"主窗口显示对应版本名称");
                    Entry.Render(window,Path.Combine(Path.GetDirectoryName(report),"preview-edition.png"),1100,920);
                } finally {window.Close();}
                rows.Add("版本="+Edition.Name+"；全部使用虚构资料；没有启动战网、打开赞助网址或读取真实账号。");
                File.WriteAllLines(report,rows,new UTF8Encoding(true));
            } finally {
                string parent=Path.GetFullPath(Path.GetDirectoryName(report)).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(Path.GetFullPath(root).StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("edition-fixture-",StringComparison.Ordinal))Directory.Delete(root,true);
            }
        }
    }
}
