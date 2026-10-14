using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public static class LoginGuide {
        public static string[] Steps(string region){return region=="GLOBAL"?new[]{
            "1  添加账号：填便于识别的名称和登录邮箱，密码可不填。",
            "2  正常退出战网，点“首次登录 / 重新认证”。手动登录这个邮箱，明确勾选“保持登录”。",
            "3  核对客户端昵称后，从菜单选“退出”，不要选“退出登录”。可点下方按钮检查保持登录设置。",
            "4  下次选择此账号，点“切换并启动”。如果需要密码或验证，重新完成第 2 步。"
        }:new[]{
            "1  添加账号：填写便于识别的名称，选择内置窗口或专用 Chrome / Edge。每个账号单独记忆。",
            "2  点管家“网页登录”，会自动打开战网并等待登录页。就绪后，在战网点“登录或注册”。",
            "3  首次在网页手动登录，完成将军令等必要确认。以战网客户端显示的昵称为准，网页显示账号不代表客户端已登录。",
            "4  想试一键切换：正常退出所有战网，展开“国服一键登录（实验）”，点“记住这个账号”。多开继续使用网页登录。"
        };}
        public static void CheckGlobal(byte[] bytes,Account account){var state=GlobalConfig.Read(bytes);state.RequireForeign();if(!GlobalIdentity.Same(GlobalIdentity.First(state.Names),GlobalIdentity.Key(account)))throw new InvalidOperationException("战网最后记住的账号与所选条目不同。请选择对应条目，或先完成此账号的官方登录。没有修改任何账号资料。");if(!state.AutoLogin)throw new InvalidOperationException("保持登录没有开启。请重新登录时明确勾选“保持登录”，进入后正常退出再检查。");}
    }
    public partial class MainWindow {
        Expander gettingStarted;TextBlock guideText;Button guideCheck;string guideAccount;
        void BuildGettingStarted(){
            gettingStarted=new Expander{Header="首次使用 / 登录帮助",Margin=new Thickness(0,12,0,0)};
            var body=new StackPanel{Margin=new Thickness(12)};gettingStarted.Content=body;guideText=Ui.Text("",13);guideText.LineHeight=24;body.Children.Add(guideText);
            var row=new WrapPanel();body.Children.Add(row);ActionButton(row,"环境与战网位置",ShowEnvironment,false);guideCheck=ActionButton(row,"检查退出后的保持登录",CheckGlobalRegistration,false);details.Children.Add(gettingStarted);
        }
        void UpdateGettingStarted(){if(gettingStarted==null)return;var account=Selected;gettingStarted.Visibility=account!=null&&account.Archived?Visibility.Collapsed:Visibility.Visible;guideText.Text=string.Join("\n\n",LoginGuide.Steps(region));guideCheck.Visibility=region=="GLOBAL"&&account!=null?Visibility.Visible:Visibility.Collapsed;
            string current=account==null?region+":empty":account.Id;if(current!=guideAccount){guideAccount=current;gettingStarted.IsExpanded=account==null;}
        }
        async void CheckGlobalRegistration(){
            Account account;try{account=Require().Copy();GlobalIdentity.Key(account);account.Password="";}catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"请选择外服账号。";return;}
            Stop();HidePassword();SetBusy(true);status.Text="正在核对退出后的账号选择和保持登录设置…";
            try {await Task.Run(()=>{var backend=new TcNoGlobalBackend(store.Home,AppDomain.CurrentDomain.BaseDirectory);backend.Idle();byte[] bytes=backend.Read();try{LoginGuide.CheckGlobal(bytes,account);backend.Idle();}finally{Array.Clear(bytes,0,bytes.Length);}});status.Text="所选账号与战网记住的账号一致，保持登录已开启。现在可点“切换并启动”测试；这项检查不代表服务器认证一定有效。";}
            catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"检查未完成。请正常退出战网后再检查；原资料未改动。";}finally{SetBusy(false);}
        }
    }
}
