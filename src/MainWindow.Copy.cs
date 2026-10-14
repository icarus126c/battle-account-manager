using System;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public partial class MainWindow {
        Button quickLoginCopy,quickPasswordCopy,quickNicknameCopy;
        readonly TextBlock quickLoginValue=Ui.Text("",13),copyFeedback=Ui.Text("",12);
        // Test seam keeps verification away from the real system clipboard.
        Action<string> copyToClipboard;
        internal static string CopyValue(Account account,string field){
            if(account==null)throw new InvalidOperationException("请先选择账号。");
            switch(field){case "login":return account.LoginAccount;case "password":return account.Password;case "nickname":return account.Username;default:throw new ArgumentException("Unknown copy field");}
        }
        void BuildQuickCopy(Panel parent){
            parent.Children.Add(quickLoginValue);
            var row=new WrapPanel();parent.Children.Add(row);
            quickLoginCopy=ActionButton(row,"复制登录账号",()=>CopySelected("login"),true);
            quickPasswordCopy=ActionButton(row,"复制密码",()=>CopySelected("password"),false);
            quickNicknameCopy=ActionButton(row,"复制战网昵称",()=>CopySelected("nickname"),false);
            quickNicknameCopy.ToolTip="复制昵称 / BattleTag，登录邮箱请用左侧按钮。";
            copyFeedback.Visibility=Visibility.Collapsed;parent.Children.Add(copyFeedback);
        }
        void UpdateQuickCopy(Account account){
            if(quickLoginCopy==null)return;
            quickLoginCopy.Content=region=="GLOBAL"?"复制登录邮箱":"复制登录账号";
            quickLoginValue.Text=(region=="GLOBAL"?"登录邮箱：":"登录账号：")+(account==null?"—":Display(account.LoginAccount,"未填写，点“编辑账号”补充"));
            quickLoginCopy.IsEnabled=quickPasswordCopy.IsEnabled=quickNicknameCopy.IsEnabled=account!=null&&!busy&&!VaultDenied;
            copyFeedback.Text="";copyFeedback.Visibility=Visibility.Collapsed;
        }
        void CopySelected(string field){
            var account=Require();string label=field=="login"?(account.Region=="GLOBAL"?"登录邮箱":"登录账号"):field=="password"?"密码":"战网昵称";
            Copy(label,CopyValue(account,field));
        }
        void CopyFeedback(string message,bool error){
            status.Text=message;copyFeedback.Text=message;copyFeedback.Foreground=Ui.Brush(error?"#A43C32":"#365E9C");copyFeedback.Visibility=Visibility.Visible;
        }
    }
}
