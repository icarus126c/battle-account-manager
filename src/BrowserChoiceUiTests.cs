using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Text;using System.Windows.Controls;
namespace BrowserManager {
    public static class BrowserChoiceUiTests {
        static T Field<T>(MainWindow window,string name){return (T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);}
        public static void Run(string report,string image){
            var results=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new Exception(text);results.Add("通过："+text);};
            string parent=Path.GetDirectoryName(Path.GetFullPath(report)),home=Path.Combine(parent,"browser-ui-fixture-"+Guid.NewGuid().ToString("N"));MainWindow window=null;
            try{
                var store=new Store(home);var data=MainWindow.PreviewData();store.Save(data);string a=data.Accounts[0].Id,b=data.Accounts[1].Id;window=new MainWindow(false,"CN",home);
                var list=Field<ListBox>(window,"list");Action<string> select=id=>{list.SelectedItem=list.Items.Cast<Account>().Single(x=>x.Id==id);};select(a);
                var builtin=Field<RadioButton>(window,"builtInChoice");var external=Field<RadioButton>(window,"otherChoice");var choice=Field<ComboBox>(window,"externalChoice");
                check(builtin.IsChecked==true&&external.IsChecked!=true&&choice.SelectedIndex==0,"新账号界面默认内置登录窗口，外部列表包含 Chrome 和 Edge");
                external.IsChecked=true;check(store.Load().Accounts.Single(x=>x.Id==a).LoginBrowser=="chrome","选择使用其他浏览器后持久保存 Chrome");
                choice.SelectedIndex=1;var saved=store.Load().Accounts.Single(x=>x.Id==a);check(saved.LoginBrowser=="edge"&&saved.ExternalBrowser=="edge","选择 Edge 后同时保存登录路由和外部浏览器偏好");
                select(b);check(builtin.IsChecked==true&&choice.SelectedIndex==0&&store.Load().Accounts.Single(x=>x.Id==a).LoginBrowser=="edge","切换账号加载独立偏好且不覆盖上个账号");
                select(a);check(external.IsChecked==true&&choice.SelectedIndex==1,"切回账号恢复 Edge 选择");
                builtin.IsChecked=true;check(store.Load().Accounts.Single(x=>x.Id==a).LoginBrowser=="webview2"&&choice.SelectedIndex==1,"切换内置模式后仍记住外部 Edge 选择");
                external.IsChecked=true;Entry.Render(window,image,1100,920);window.Close();window=null;
                window=new MainWindow(false,"CN",home);selectWindow(window,a);check(Field<RadioButton>(window,"otherChoice").IsChecked==true&&Field<ComboBox>(window,"externalChoice").SelectedIndex==1,"关闭重开虚构界面后仍记住 Edge");
                saved=store.Load().Accounts.Single(x=>x.Id==a);check(saved.Password==data.Accounts[0].Password&&saved.AuthenticatorMemo==data.Accounts[0].AuthenticatorMemo&&saved.Id==a,"实际界面选择保存保留虚构密码、将军令备忘和标识");
                results.Add("合计 "+results.Count+" 项通过。仅操作本工具的虚构窗口和临时资料，不显示真实认证窗口，不访问真实账号资料，不启动任何浏览器。");File.WriteAllLines(report,results,new UTF8Encoding(true));
            }finally{if(window!=null)window.Close();string full=Path.GetFullPath(home);if(full.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("browser-ui-fixture-",StringComparison.Ordinal))Directory.Delete(full,true);}
        }
        static void selectWindow(MainWindow window,string id){var list=Field<ListBox>(window,"list");list.SelectedItem=list.Items.Cast<Account>().Single(a=>a.Id==id);}
    }
}
