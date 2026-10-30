using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
namespace BrowserManager {
    public sealed class LoginStatusInfo {public string Text,Next;public bool Attention;}
    public static class LoginStatus {
        public static LoginStatusInfo Read(string home,Account account){
            if(account==null)return new LoginStatusInfo{Text="选择账号后查看登录状态。",Next=""};
            bool registered=false,broken=false;
            if(account.Region=="CN"){string root=Path.Combine(home,"cn-exp");if(NativeSelections.Exists(root,account.Id))try{var selection=NativeSelections.Load(root,account.Id);registered=CnEngine.Flow.Valid(CnEngine.Flow.First(selection.SavedNames));broken=!registered;}catch{broken=true;}}
            else registered=File.Exists(Path.Combine(home,"data","tcno","LoginCache","BattleNet",account.Id,"email.json"));
            string registration=broken?"需要重新记住账号":registered?account.Region=="CN"?"已记住一键登录账号":"已保存账号切换信息":account.Region=="CN"?"可使用网页登录":"首次使用请先登录一次";
            string state=account.LoginState=="needs-auth"?"需要重新登录":account.LoginState=="pending"?"已发起登录 · 等待你核对":account.LoginState=="confirmed"?"上次已确认进入此账号":"登录结果尚未确认";
            string time=account.LoginCheckedUtc==DateTime.MinValue?"":"\n上次确认："+account.LoginCheckedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return new LoginStatusInfo{Text=state+"\n"+registration+time,Attention=broken||account.LoginState=="needs-auth",Next=account.Region=="CN"?account.LoginState=="needs-auth"||!registered?"点下方“网页登录”，自动打开战网并准备浏览器。":"先退出所有战网窗口，再点“一键登录（实验）”。":account.LoginState=="needs-auth"||!registered?"首次使用：点“账号密码登录”，登录时勾选“保持登录”。":"先退出战网，再点“切换并启动”。"};
        }
    }
    public partial class MainWindow {
        Expander loginFeedback;readonly TextBlock loginStatusText=Ui.Text("",13),loginNextText=Ui.Text("",12);
        void BuildLoginStatus(Panel panel){panel.Children.Add(loginStatusText);panel.Children.Add(loginNextText);loginFeedback=new Expander{Header="登录结果反馈（可选）",Margin=new System.Windows.Thickness(0,8,0,0)};panel.Children.Add(loginFeedback);var body=new StackPanel();loginFeedback.Content=body;body.Children.Add(Ui.Text("只记录你看到的结果，不会替你登录。请先核对战网客户端里的账号。",12));var row=new WrapPanel();body.Children.Add(row);
            ActionButton(row,"登录成功，账号正确",()=>ConfirmLogin(true),false);ActionButton(row,"没登进去，查看帮助",()=>ConfirmLogin(false),false);
        }
        void UpdateLoginStatus(){var info=LoginStatus.Read(store.Home,Selected);loginStatusText.Text=info.Text;loginStatusText.Foreground=Ui.Brush(info.Attention?"#B84438":"#233658");loginNextText.Text=info.Next;}
        void LoginRequested(string id,string action){if(VaultDenied)return;var updated=Clone();var account=updated.Accounts.SingleOrDefault(a=>a.Id==id);if(account==null)return;account.LoginState="pending";account.LastLoginAction=action;try{Commit(updated,id);}catch{status.Text+=" 登录结果状态未保存，原资料保留。";}}
        void ConfirmLogin(bool success){var selected=Require();if(selected.Archived)throw new InvalidOperationException("请先恢复此账号到列表。");var updated=Clone();var account=updated.Accounts.Single(a=>a.Id==selected.Id);account.LoginState=success?"confirmed":"needs-auth";if(success)account.LoginCheckedUtc=DateTime.UtcNow;Commit(updated,account.Id);
            if(success)ClearRecovery();else ShowRecovery();status.Text=success?"已记录你的确认：官方客户端进入了这个账号。这是上次结果，不保证登录状态永久有效。":"已标记需要重新登录；账号和旧登记资料仍保留。";
        }
    }
}
