using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace BrowserManager {
    public sealed class EnvironmentReport {public string Client,WebView,Chrome,Edge,Engine;public bool ClientReady,WebReady,EngineReady;}
    public static class EnvironmentCheck {
        public const string WebViewDownload="https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section";
        public static string WebViewVersion(){try{return CoreWebView2Environment.GetAvailableBrowserVersionString();}catch{return null;}}
        public static void RequireWebView(){if(string.IsNullOrEmpty(WebViewVersion()))throw new InvalidOperationException("内置浏览器运行环境不可用。请打开“环境与战网位置”安装 WebView2，或在“登录选项”改选已安装的 Chrome / Edge。");}
        static string External(string mode){try{return Browser.Executable(mode);}catch{return "未找到（可选，不影响其他登录方式）";}}
        public static EnvironmentReport Read(string home,string appRoot){
            var result=new EnvironmentReport();
            try{result.Client=OfficialClient.Resolve(OfficialClient.ReadPreference(home),OfficialClient.Candidates(),OfficialClient.IsClient);result.ClientReady=true;}catch(InvalidOperationException e){result.Client=e.Message;}
            string version=WebViewVersion();result.WebReady=!string.IsNullOrEmpty(version);result.WebView=result.WebReady?"可用 · "+version:"不可用：请安装／修复 WebView2，或使用 Chrome / Edge";
            result.Chrome=External("chrome");result.Edge=External("edge");
            try{TcNoGlobalBackend.CheckEngine(Path.Combine(appRoot,"Engine","TcNo-Acc-Switcher.exe"));result.EngineReady=true;result.Engine="完整 · 版本校验通过";}catch{result.Engine="组件缺失或版本不同，请重新解压完整软件包。网页登录仍可单独使用。";}
            return result;
        }
    }
    public sealed class EnvironmentDialog:Window {
        readonly string home,root;readonly Func<bool> allowed;readonly TextBlock client=Ui.Text("正在检查…",12),web=Ui.Text("",12),chrome=Ui.Text("",12),edge=Ui.Text("",12),engine=Ui.Text("",12),notice=Ui.Text("",12);readonly StackPanel actions=new StackPanel();bool reading;
        public EnvironmentDialog(Window owner,string dataHome,string appRoot,Func<bool> canChange){
            Owner=owner;home=dataHome;root=appRoot;allowed=canChange;Title="环境与战网位置";Width=700;Height=Math.Min(730,SystemParameters.WorkArea.Height*0.9);WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=Ui.Brush("#F4F6FB");
            var shell=new DockPanel{Background=Background};Content=shell;var footer=new StackPanel{Margin=new Thickness(24,8,24,16)};DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);footer.Children.Add(notice);footer.Children.Add(actions);
            var panel=new StackPanel{Margin=new Thickness(24,24,24,0)};shell.Children.Add(new ScrollViewer{Content=panel,Background=Background,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});panel.Children.Add(Ui.Text("准备这台电脑",25));panel.Children.Add(Ui.Text("这里只检查安装情况，不登录账号，也不改变战网的登录状态。",12));
            var c=Ui.Card(panel);c.Children.Add(Ui.Text("战网客户端位置",18));c.Children.Add(client);var row=new WrapPanel();c.Children.Add(row);row.Children.Add(Ui.Button("选择 Battle.net.exe",Choose,true));row.Children.Add(Ui.Button("恢复自动查找",Reset,false));
            c.Children.Add(Ui.Text("同一个位置用于国服、外服和网页登录。可以选择 D 盘等自定义安装位置。",12));
            var browser=Ui.Card(panel);browser.Children.Add(Ui.Text("网页登录环境",18));browser.Children.Add(Ui.Text("内置窗口 · WebView2",13));browser.Children.Add(web);browser.Children.Add(Ui.Button("打开微软官方下载页",()=>{try{Process.Start(new ProcessStartInfo(EnvironmentCheck.WebViewDownload){UseShellExecute=true});}catch{notice.Text="浏览器未能打开，请在使用说明中查看微软官方下载地址。";}},false));browser.Children.Add(Ui.Text("Chrome（可选）",13));browser.Children.Add(chrome);browser.Children.Add(Ui.Text("Edge（可选）",13));browser.Children.Add(edge);
            var e=Ui.Card(panel);e.Children.Add(Ui.Text("一键切换组件",18));e.Children.Add(engine);actions.Orientation=Orientation.Horizontal;actions.Children.Add(Ui.Button("重新检查",Refresh,false));actions.Children.Add(Ui.Button("完成",()=>Close(),true));Loaded+=delegate{Refresh();};
        }
        void Choose(){if(reading||!allowed())return;var dialog=new OpenFileDialog{Title="选择战网安装目录里的 Battle.net.exe",Filter="战网主程序 (Battle.net.exe)|Battle.net.exe",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;try{if(!allowed())return;OfficialClient.SavePreference(home,dialog.FileName);notice.Text="位置已保存。下次启动国服、外服都使用此位置。";Refresh();}catch(Exception e){notice.Text=e is InvalidOperationException?e.Message:"位置未能保存，原设置保留。";}}
        void Reset(){if(reading||!allowed())return;try{OfficialClient.ClearPreference(home);notice.Text="已恢复自动查找。";Refresh();}catch{notice.Text="设置未能更新，请稍后重试。";}}
        async void Refresh(){if(reading)return;reading=true;actions.IsEnabled=false;try{Apply(await Task.Run(()=>EnvironmentCheck.Read(home,root)));}catch{notice.Text="检查未完成，请稍后重新检查。";}finally{reading=false;actions.IsEnabled=true;}}
        internal void Apply(EnvironmentReport report){client.Text=report.Client;web.Text=report.WebView;chrome.Text=report.Chrome;edge.Text=report.Edge;engine.Text=report.Engine;client.Foreground=Ui.Brush(report.ClientReady?"#233658":"#B84438");web.Foreground=Ui.Brush(report.WebReady?"#233658":"#B84438");engine.Foreground=Ui.Brush(report.EngineReady?"#233658":"#B84438");}
    }
    public partial class MainWindow {
        void ShowEnvironment(){RequireVaultAccess();Stop();HidePassword();var dialog=new EnvironmentDialog(this,store.Home,AppDomain.CurrentDomain.BaseDirectory,()=>!VaultDenied&&!busy&&!closed);dialog.ShowDialog();}
    }
}
