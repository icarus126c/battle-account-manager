using System;using System.Windows;using System.Windows.Controls;using System.Windows.Media;
namespace BrowserManager {
    public static class Ui {
        public static SolidColorBrush Brush(string color){return (SolidColorBrush)new BrushConverter().ConvertFromString(color);}
        public static TextBlock Text(string value,int size){return new TextBlock{Text=value,FontSize=size,Foreground=Brush("#253455"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,7)};}
        public static Button Button(string text,Action action,bool primary){var b=new Button{Content=text,Padding=new Thickness(13,8,13,8),Margin=new Thickness(0,5,10,5),Background=primary?Brush("#3464DF"):Brushes.White,Foreground=primary?Brushes.White:Brush("#253455"),BorderBrush=Brush("#CCD5E7"),Cursor=System.Windows.Input.Cursors.Hand};b.Click+=delegate{action();};return b;}
        public static StackPanel Card(Panel parent){var p=new StackPanel();parent.Children.Add(new Border{Background=Brushes.White,CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Margin=new Thickness(0,12,0,0),Child=p});return p;}
    }
    public class Editor:Window {
        public Account Result;readonly Account source;readonly Data existing;
        readonly TextBox name,user,login,auth,note,visiblePassword;
        readonly PasswordBox password;
        readonly ComboBox region,method;
        readonly CheckBox show;
        readonly TextBlock error;
        public Editor(Window owner,Account account,Data existingData=null){
            source=account.Copy();existing=existingData;Owner=owner;Title="编辑账号备忘";Width=640;Height=780;MinWidth=600;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=Ui.Brush("#F4F6FB");
            var shell=new DockPanel{Background=Background};Content=shell;var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(24,8,24,18)};var cancel=Ui.Button("取消",delegate{DialogResult=false;},false);cancel.IsCancel=true;buttons.Children.Add(cancel);var save=Ui.Button("保存备忘",Save,true);save.IsDefault=true;buttons.Children.Add(save);DockPanel.SetDock(buttons,Dock.Bottom);shell.Children.Add(buttons);
            var p=new StackPanel{Margin=new Thickness(24,14,24,12)};shell.Children.Add(new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});p.Children.Add(Ui.Text("账号资料都可以选填",22));p.Children.Add(Ui.Text("登录账号填邮箱或手机号；战网昵称单独备注，不能用作登录邮箱。",12));
            var top=new Grid();top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(160)});p.Children.Add(top);var left=new StackPanel{Margin=new Thickness(0,0,14,0)};top.Children.Add(left);left.Children.Add(Ui.Text("账号名称 / 备注名",13));name=Box(source.Name,80);left.Children.Add(name);var right=new StackPanel();Grid.SetColumn(right,1);top.Children.Add(right);right.Children.Add(Ui.Text("所属区服",13));region=new ComboBox{Padding=new Thickness(8),ItemsSource=new[]{"国服","外服"},SelectedIndex=source.Region=="CN"?0:1};right.Children.Add(region);
            p.Children.Add(Ui.Text("战网昵称 / BattleTag · 选填",13));user=Box(source.Username,120);p.Children.Add(user);
            p.Children.Add(Ui.Text("登录账号 · 手机号 / 邮箱（选填）",13));login=Box(source.LoginAccount,254);p.Children.Add(login);
            p.Children.Add(Ui.Text("外服免密码切换需要填写实际登录邮箱。密码仅作备忘，可不填；首次认证在官方战网中完成。",12));
            p.Children.Add(Ui.Text("密码（选填）",13));var secretRow=new DockPanel();p.Children.Add(secretRow);show=new CheckBox{Content="显示",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};DockPanel.SetDock(show,Dock.Right);secretRow.Children.Add(show);var passwordGrid=new Grid();secretRow.Children.Add(passwordGrid);password=new PasswordBox{Password=source.Password??"",MaxLength=1024,Padding=new Thickness(8)};visiblePassword=Box("",1024);visiblePassword.Visibility=Visibility.Collapsed;passwordGrid.Children.Add(password);passwordGrid.Children.Add(visiblePassword);
            show.Checked+=delegate{visiblePassword.Text=password.Password;password.Visibility=Visibility.Collapsed;visiblePassword.Visibility=Visibility.Visible;};show.Unchecked+=delegate{password.Password=visiblePassword.Text;visiblePassword.Visibility=Visibility.Collapsed;password.Visibility=Visibility.Visible;visiblePassword.Clear();};
            p.Children.Add(Ui.Text("登录方式（可选，也可以直接输入）",13));method=new ComboBox{IsEditable=true,Padding=new Thickness(7)};p.Children.Add(method);SetMethods();method.Text=source.LoginMethod??"";region.SelectionChanged+=delegate{string previous=method.Text;SetMethods();method.Text=previous;};
            p.Children.Add(Ui.Text("将军令 / 验证器备忘（选填）",13));auth=Box(source.AuthenticatorMemo,2000);auth.Height=70;auth.AcceptsReturn=true;auth.TextWrapping=TextWrapping.Wrap;auth.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;p.Children.Add(auth);
            p.Children.Add(Ui.Text("其他备注（选填）",13));note=Box(source.Note,4000);note.Height=95;note.AcceptsReturn=true;note.TextWrapping=TextWrapping.Wrap;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;p.Children.Add(note);
            error=Ui.Text("",12);error.Foreground=Ui.Brush("#B84438");p.Children.Add(error);Deactivated+=delegate{if(show.IsChecked==true)show.IsChecked=false;};Closed+=delegate{password.Clear();visiblePassword.Clear();};
        }
        static TextBox Box(string text,int max){return new TextBox{Text=text??"",MaxLength=max,Padding=new Thickness(8),Margin=new Thickness(0,0,0,5)};}
        void SetMethods(){method.ItemsSource=region.SelectedIndex==0?new[]{"手机号 + 将军令","邮箱 + 将军令","手机号 / 邮箱 + 密码","将军令 / 扫码","浏览器保留登录","其他"}:new[]{"邮箱 + 密码","邮箱 + 验证器","手机号 + 密码","通行密钥","其他"};}
        void Save(){try{var a=source.Copy();a.Region=region.SelectedIndex==0?"CN":"GLOBAL";a.Username=user.Text.Trim();a.LoginAccount=login.Text.Trim();a.Name=name.Text.Trim();if(string.IsNullOrWhiteSpace(a.Name)){a.Name=!string.IsNullOrWhiteSpace(a.Username)?a.Username:!string.IsNullOrWhiteSpace(a.LoginAccount)?a.LoginAccount:a.Region=="CN"?"国服备忘":"外服备忘";if(a.Name.Length>80)a.Name=a.Name.Substring(0,80);}a.Password=show.IsChecked==true?visiblePassword.Text:password.Password;a.LoginMethod=method.Text.Trim();a.AuthenticatorMemo=auth.Text.Trim();a.Note=note.Text.Trim();Store.Validate(new Data{Version=2,Accounts=new System.Collections.Generic.List<Account>{a}});AccountChecks.RequireUniqueForeign(existing,a);Result=a;DialogResult=true;}catch(InvalidOperationException e){error.Text=e.Message;}catch{error.Text="资料超出长度限制，请缩短后再保存。";}}
    }
}
