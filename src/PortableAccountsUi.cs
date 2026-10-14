using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BrowserManager {
    public sealed class TransferPasswordDialog:Window {
        readonly PasswordBox password=new PasswordBox {Padding=new Thickness(9),MaxLength=256};
        readonly PasswordBox confirm=new PasswordBox {Padding=new Thickness(9),MaxLength=256};
        readonly TextBlock message=Ui.Text("",12);
        readonly Button submit,cancel;readonly Func<string,Task> action;readonly bool exporting;bool working;
        public TransferPasswordDialog(Window owner,bool export,Func<string,Task> callback){
            if(owner!=null)Owner=owner;exporting=export;action=callback;
            Title=export?"创建跨电脑迁移包":"打开跨电脑迁移包";
            Width=520;Height=Math.Min(590,SystemParameters.WorkArea.Height*.9);WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI");Background=Ui.Brush("#F4F6FB");
            var dock=new DockPanel();Content=new Border {Padding=new Thickness(24),Background=Background,Child=dock};
            var row=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(row,Dock.Bottom);dock.Children.Add(row);
            cancel=Ui.Button("取消",()=>{if(!working)DialogResult=false;},false);cancel.IsCancel=true;row.Children.Add(cancel);
            submit=Ui.Button(export?"加密并导出":"解密并预览",Run,true);submit.IsDefault=true;row.Children.Add(submit);
            var panel=new StackPanel();dock.Children.Add(new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panel.Children.Add(Ui.Text(Title,23));
            panel.Children.Add(Ui.Text(export?"导出所有国服、外服账号，包括密码、备注和隐藏条目。新电脑只需迁移密码即可打开，不绑定原 Windows 用户。":"请输入导出时设置的迁移密码。解密后先预览账号，由你确认后才导入。",13));
            panel.Children.Add(Ui.Text("迁移密码",13));panel.Children.Add(password);
            if(export){panel.Children.Add(Ui.Text("再次输入迁移密码",13));panel.Children.Add(confirm);panel.Children.Add(Ui.Text("至少 10 个字符。可以与本机主密码不同；遗忘后无法找回，请与迁移包分开保管。",12));}
            panel.Children.Add(Ui.Text("包含：账号、密码、昵称、登录方式、备注、收藏。\n不包含：浏览器登录状态、战网凭据、本机主密码和管家验证器密钥。",12));
            panel.Children.Add(Ui.Text("迁移包每次打开都需要密码。导入后沿用新电脑的保护设置；想让管家每次打开也需要密码，请在“保护设置”启用主密码。",12));
            message.Foreground=Ui.Brush("#B84438");panel.Children.Add(message);
            Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(working)e.Cancel=true;};
            Closed+=delegate{password.Clear();confirm.Clear();};Loaded+=delegate{password.Focus();};
        }
        async void Run(){
            if(working)return;string entered=password.Password;
            try{PortableAccounts.CheckPassword(entered);}catch(InvalidOperationException e){message.Text=e.Message;return;}
            if(exporting&&entered!=confirm.Password){message.Text="两次迁移密码不一致。";return;}
            working=true;submit.IsEnabled=cancel.IsEnabled=password.IsEnabled=confirm.IsEnabled=false;message.Text=exporting?"正在加密，请稍候…":"正在解密，请稍候…";
            try{await action(entered);working=false;DialogResult=true;}
            catch(Exception e){message.Text=e is InvalidOperationException?e.Message:"操作未完成，请检查文件位置和访问权限。原资料保留。";}
            finally{entered=null;working=false;submit.IsEnabled=cancel.IsEnabled=password.IsEnabled=confirm.IsEnabled=true;}
        }
    }
    public partial class MainWindow {
        void RequireTransferAccess(){RequireVaultAccess();if(sessionLockPending)throw new InvalidOperationException("系统曾锁屏，请关闭此窗口并重新解锁管家后操作。");}
        void ExportPortable(){
            Stop();HidePassword();RequireVaultAccess();
            var save=new SaveFileDialog {Title="导出跨电脑迁移包",Filter="密码加密迁移包 (*.bammove)|*.bammove",DefaultExt=".bammove",AddExtension=true,FileName="账号迁移-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".bammove",OverwritePrompt=true};
            if(save.ShowDialog(this)!=true)return;RequireVaultAccess();
            if(!save.FileName.EndsWith(".bammove",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请使用 .bammove 扩展名保存迁移包。");
            vaultDialogOpen=true;UpdateVaultAccess();
            try{
                var dialog=new TransferPasswordDialog(this,true,async password=>{
                    RequireTransferAccess();var snapshot=data.Copy();byte[] packet=null;
                    try{packet=await Task.Run(()=>PortableAccounts.Encode(snapshot,password));RequireTransferAccess();Store.WriteAtomic(save.FileName,packet);}
                    finally{PortableAccounts.Clear(snapshot);if(packet!=null)Array.Clear(packet,0,packet.Length);}
                });
                if(dialog.ShowDialog()==true)status.Text="跨电脑迁移包已加密保存。请保管迁移密码，在新电脑用“导入迁移包”打开。";
            }finally{vaultDialogOpen=false;lastVaultActivity=DateTime.UtcNow;if(sessionLockPending)LockVault();UpdateVaultAccess();}
        }
        void ChoosePortable(){
            Stop();HidePassword();RequireVaultAccess();
            var open=new OpenFileDialog {Title="导入跨电脑迁移包",Filter="密码加密迁移包 (*.bammove)|*.bammove"};
            if(open.ShowDialog(this)==true)ImportPortable(open.FileName);
        }
        void ImportPortable(string path){
            Stop();HidePassword();RequireVaultAccess();PortableEnvelope incoming=null;Data updated=null;
            vaultDialogOpen=true;UpdateVaultAccess();
            try{
                var dialog=new TransferPasswordDialog(this,false,async password=>{
                    RequireTransferAccess();PortableEnvelope candidate=null;
                    try{candidate=await Task.Run(()=>PortableAccounts.Read(path,password));RequireTransferAccess();incoming=candidate;candidate=null;}
                    finally{if(candidate!=null)PortableAccounts.Clear(candidate.Accounts);}
                });
                if(dialog.ShowDialog()!=true)return;RequireTransferAccess();
                var previewDialog=new RestorePreviewDialog(this,data,incoming.Accounts,false,incoming.CreatedUtc);
                if(previewDialog.ShowDialog()!=true)return;RequireTransferAccess();
                updated=Clone();int added=Accounts.Merge(updated,incoming.Accounts);
                if(added>0){Commit(updated,null);updated=null;}
                status.Text="已导入 "+added+" 个账号，相同条目未覆盖。迁入账号需重新认证；本机"+(store.IsProtected?"主密码保护保持不变。":"当前免主密码使用，可在“保护设置”启用主密码。");
            }finally{if(updated!=null&&!object.ReferenceEquals(updated,data))PortableAccounts.Clear(updated);if(incoming!=null)PortableAccounts.Clear(incoming.Accounts);vaultDialogOpen=false;lastVaultActivity=DateTime.UtcNow;if(sessionLockPending)LockVault();UpdateVaultAccess();}
        }
    }
}
