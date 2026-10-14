using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrowserManager {
    public sealed class RegionDialog:Window {
        readonly string home;readonly Func<bool> allowed;readonly TextBlock summary=Ui.Text("点击“检查当前设置”查看是否需要补齐入口。",13),status=Ui.Text("",13);readonly WrapPanel buttons=new WrapPanel();bool working;
        public RegionDialog(Window owner,string dataHome,Func<bool> canChange){Owner=owner;home=dataHome;allowed=canChange;Title="解锁外服客户端 · 实验";Width=700;Height=Math.Min(630,SystemParameters.WorkArea.Height*0.9);WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=Ui.Brush("#F4F6FB");
            var panel=new StackPanel{Margin=new Thickness(26)};Content=new ScrollViewer{Background=Background,Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(Ui.Text("解锁外服客户端",24));panel.Children.Add(Ui.Text("补齐国服与外服的登录入口 · 实验功能，实际结果需在官方客户端核对。",12));
            var body=Ui.Card(panel);body.Children.Add(Ui.Text("国服与外服入口",18));body.Children.Add(summary);body.Children.Add(Ui.Text("中国 CN · 美洲 US · 欧洲 EU · 亚洲 KR",14));body.Children.Add(Ui.Text("同时保留原有地区与语言选项，补充简体中文、英语和繁体中文选项；不改变当前界面语言。",12));
            var guide=Ui.Card(panel);guide.Children.Add(Ui.Text("操作步骤",18));guide.Children.Add(Ui.Text("1. 从菜单正常退出全部战网窗口，保留登录状态。\n2. 点击下方“解锁国服与外服入口”。\n3. 重新打开战网，在登录页的地区选项中选择目标区服，再登录该区服的账号。",13));guide.Children.Add(Ui.Text("如果重开后自动进入原账号，本功能不会替你退出登录。国服和外服仍使用各自账号，游戏进度与购买内容不因入口修改而合并。",12));
            panel.Children.Add(buttons);buttons.Children.Add(Ui.Button("检查当前设置",Check,false));buttons.Children.Add(Ui.Button("解锁国服与外服入口",()=>Change(false),true));buttons.Children.Add(Ui.Button("撤销最近一次修改",()=>Change(true),false));status.Foreground=Ui.Brush("#233658");panel.Children.Add(status);panel.Children.Add(Ui.Button("关闭",()=>{if(!working)Close();},false));
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(working)e.Cancel=true;};
        }
        internal void SetPreview(string text){summary.Text=text;}
        async void Check(){if(working||!allowed())return;working=true;buttons.IsEnabled=false;try{summary.Text=await Task.Run(()=>{byte[] bytes=new OfficialRegionBackend(home).Read();try{return RegionConfig.Describe(bytes);}finally{Array.Clear(bytes,0,bytes.Length);}});status.Text="只读检查完成。";}catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"检查未完成，未改动战网配置。";}finally{working=false;buttons.IsEnabled=true;}}
        async void Change(bool undo){if(working||!allowed())return;working=true;buttons.IsEnabled=false;status.Text=undo?"正在核对并撤销入口修改…":"正在备份入口设置并核对战网退出状态…";try{status.Text=await Task.Run(()=>RegionService.Change(new OfficialRegionBackend(home),undo,allowed));summary.Text="请以官方战网的登录页实际显示结果为准。";}catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"入口修改未能完成。请保留当前状态和提示，原入口备份会保留。";}finally{working=false;buttons.IsEnabled=true;}}
    }
    public partial class MainWindow {
        void ShowRegionEntries(){RequireVaultAccess();Stop();HidePassword();new RegionDialog(this,store.Home,()=>!VaultDenied&&!closed).ShowDialog();}
    }
}
