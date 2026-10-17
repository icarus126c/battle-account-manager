using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace BrowserManager {
    public sealed class VaultOptionsDialog : Window {
        public string Choice;
        public VaultOptionsDialog(Window owner, bool protectedVault, bool factor) {
            Owner = owner; Title = "密码库保护设置"; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI"); Background = Ui.Brush("#F4F6FB");
            var panel = new StackPanel(); Content = new Border { Padding = new Thickness(26), Background = Background, Child = panel };
            panel.Children.Add(Ui.Text("保护设置", 23)); panel.Children.Add(Ui.Text(protectedVault ? factor ? "当前：主密码 + 验证器两步验证" : "当前：主密码保护" : "当前：免主密码使用", 14));
            panel.Children.Add(Ui.Text("主密码和两步验证都可选；账号资料仍按当前 Windows 用户加密保存在本机。",12));
            Add(panel, protectedVault ? "修改主密码" : "设置主密码");
            if (protectedVault) { Add(panel, factor ? "关闭两步验证" : "启用两步验证"); Add(panel, "移除主密码"); }
            else panel.Children.Add(Ui.Text("两步验证作为第二步，需要先设置主密码。保持当前状态即可免主密码使用。",12));
            panel.Children.Add(Ui.Button("完成", delegate { DialogResult = false; }, false));
        }
        void Add(Panel panel, string name) { panel.Children.Add(Ui.Button(name, delegate { Choice = name; DialogResult = true; }, false)); }
    }
    public sealed class FactorEnrollmentDialog : Window {
        readonly PasswordBox password = new PasswordBox { Padding = new Thickness(9), MaxLength = 256 }, code = new PasswordBox { Padding = new Thickness(9), MaxLength = 6 };
        readonly TextBlock message = Ui.Text("",12); readonly Button submit;
        readonly TextBlock secretLabel, recoveryLabel;
        readonly CheckBox saved = new CheckBox { Content = "已将备用恢复码保管在手机之外", Margin = new Thickness(0,10,0,10) };
        readonly SecretClipboard clipboard = new SecretClipboard();
        readonly Func<string,string,string,string[],Task> operation;
        string secret; string[] recovery; bool working;
        public FactorEnrollmentDialog(Window owner, Func<string,string,string,string[],Task> callback) {
            Owner = owner; operation = callback; Title = "绑定验证器"; Width = 550; Height = Math.Min(790, SystemParameters.WorkArea.Height * .9); ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI"); Background = Ui.Brush("#F4F6FB");
            secret = Totp.NewSecret(); recovery = Totp.NewRecoveryCodes();
            var panel = new StackPanel(); Content = new Border { Padding = new Thickness(26), Background = Background, Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            panel.Children.Add(Ui.Text("绑定验证器 · 可选",23));
            panel.Children.Add(Ui.Text("1. 在手机验证器中选择手动添加密钥。名称填“战网账号管家”，类型选基于时间。",13));
            secretLabel = Ui.Text(secret,18); secretLabel.FontFamily = new FontFamily("Consolas"); panel.Children.Add(secretLabel);
            panel.Children.Add(Ui.Button("复制绑定密钥", delegate { if (!working) { try { clipboard.Copy(secret); message.Text = "密钥已复制，30 秒后清除本工具的复制内容。"; } catch { message.Text = "暂时无法复制，请手动输入密钥。"; } } },false));
            panel.Children.Add(Ui.Text("采用 SHA-1、6 位、30 秒；验证器动态码与战网账号的验证码独立。",11));
            panel.Children.Add(Ui.Text("2. 保管这 8 个一次性恢复码。手机丢失时，可用主密码加一个恢复码解锁。恢复码不能找回主密码。",13));
            recoveryLabel = Ui.Text(string.Join("\n",recovery),14); recoveryLabel.FontFamily = new FontFamily("Consolas"); panel.Children.Add(recoveryLabel);
            panel.Children.Add(Ui.Button("复制备用恢复码", delegate { if (!working) { try { clipboard.Copy(string.Join("\n",recovery)); message.Text = "恢复码已复制，请妥善保管。"; } catch { message.Text = "暂时无法复制，请手动记录恢复码。"; } } },false));
            panel.Children.Add(saved);
            panel.Children.Add(Ui.Text("3. 输入主密码和手机当前的 6 位动态码，确认绑定。",13)); panel.Children.Add(Ui.Text("主密码",12)); panel.Children.Add(password); panel.Children.Add(Ui.Text("手机验证器的 6 位动态码",12)); panel.Children.Add(code);
            message.Foreground = Ui.Brush("#B84438"); panel.Children.Add(message);
            submit = Ui.Button("验证并开启",Run,true); panel.Children.Add(submit); panel.Children.Add(Ui.Button("取消",delegate { if(!working) DialogResult=false; },false));
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if(working)e.Cancel=true; };
            Closed += delegate { password.Clear(); code.Clear(); secretLabel.Text = recoveryLabel.Text = ""; secret = null; if(recovery != null)Array.Clear(recovery,0,recovery.Length); clipboard.Dispose(); };
        }
        async void Run() {
            if(working)return; if(saved.IsChecked!=true){message.Text="请先保管备用恢复码，并勾选确认。";return;} if(password.Password.Length==0){message.Text="请输入主密码。";return;}
            string enteredPassword=password.Password, enteredCode=code.Password; working=true; submit.IsEnabled=password.IsEnabled=code.IsEnabled=saved.IsEnabled=false;
            try { await operation(enteredPassword,secret,enteredCode,recovery); working=false; DialogResult=true; }
            catch(Exception e){message.Text=e is InvalidOperationException?e.Message:"绑定未完成，原保护设置保留。";code.Clear();}
            finally{working=false;submit.IsEnabled=password.IsEnabled=code.IsEnabled=saved.IsEnabled=true;}
        }
    }
}
