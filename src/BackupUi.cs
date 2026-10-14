using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace BrowserManager {
    public sealed class RestorePreviewDialog:Window {
        public RestorePreviewDialog(Window owner,Data current,Data incoming,bool full,DateTime created){Owner=owner;Title="恢复前预览";Width=580;Height=Math.Min(630,SystemParameters.WorkArea.Height*.85);ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var panel=new DockPanel();Content=new Border{Padding=new Thickness(26),Child=panel,Background=Ui.Brush("#F4F6FB")};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);top.Children.Add(Ui.Text(full?"恢复完整备份":"导入账号备份",24));
            int added=incoming.Accounts.Count(a=>!current.Accounts.Any(b=>b.Id==a.Id)),older=current.Accounts.Count(a=>!incoming.Accounts.Any(b=>b.Id==a.Id));
            top.Children.Add(Ui.Text("当前 "+current.Accounts.Count+" 个账号；备份 "+incoming.Accounts.Count+" 个（国服 "+incoming.Accounts.Count(a=>a.Region=="CN")+"，外服 "+incoming.Accounts.Count(a=>a.Region=="GLOBAL")+"）。",13));
            if(created!=DateTime.MinValue)top.Children.Add(Ui.Text("备份时间："+created.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),12));
            top.Children.Add(Ui.Text(full?"将恢复备份时的账号、密码库保护、网页登录资料及登记记录。原整套资料会另存，可撤回；当前有 "+older+" 个条目不在备份中。恢复后关闭管家，再打开新版。":"仅添加 "+added+" 个缺少的账号；相同账号不覆盖。当前保护设置和网页状态保留。",13));
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);actions.Children.Add(Ui.Button("取消",delegate{DialogResult=false;},false));actions.Children.Add(Ui.Button(full?"恢复整套资料并保留原件":"确认导入",delegate{DialogResult=true;},true));
            panel.Children.Add(new ScrollViewer{Margin=new Thickness(0,14,0,14),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=Ui.Text(string.Join("\n",incoming.Accounts.Select(a=>(a.Region=="CN"?"国服 · ":"外服 · ")+a.Name)),14)});
        }
    }
    public partial class MainWindow {
        readonly TextBlock storageSummary=Ui.Text("",11);
        void BuildStorageInfo(Panel sidebar){var expander=new Expander{Header="资料位置与备份",Margin=new Thickness(0,10,0,0)};var panel=new StackPanel();expander.Content=panel;sidebar.Children.Add(expander);panel.Children.Add(storageSummary);
            ActionButton(panel,"打开资料目录",()=>OpenFolder(store.Home),false);ActionButton(panel,"打开备份目录",()=>OpenFolder(Path.Combine(store.Home,"backups")),false);
            ActionButton(panel,"仅备份账号备忘",BackupAccounts,false);
            ActionButton(panel,"打开保留的原目录",()=>{string path=Path.Combine(store.Home,"directory-info.json");if(!File.Exists(path))throw new InvalidOperationException("本资料库尚无迁移或整套恢复记录。");var info=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DirectoryInfoRecord>(File.ReadAllText(path));if(info==null||string.IsNullOrEmpty(info.Source)||!Directory.Exists(info.Source))throw new InvalidOperationException("保留的原目录不在原位置，请查看完整备份。");OpenFolder(info.Source);},false);
            panel.Children.Add(Ui.Text("每日首次保存自动备份账号；“备份资料”可包含网页状态。完整备份／恢复前请关闭专用浏览器。",11));UpdateStorageInfo();
        }
        void OpenFolder(string folder){Directory.CreateDirectory(folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});}
        void UpdateStorageInfo(){string folder=Path.Combine(store.Home,"backups");DateTime latest=DateTime.MinValue;if(Directory.Exists(folder))foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))if(file.EndsWith(".bav",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".bampack",StringComparison.OrdinalIgnoreCase)){var time=File.GetLastWriteTime(file);if(time>latest)latest=time;}
            storageSummary.Text="资料："+store.Home+"\n最近备份："+(latest==DateTime.MinValue?"暂无":latest.ToString("yyyy-MM-dd HH:mm"));
        }
        bool PreviewRestore(Data incoming,bool full,DateTime created){vaultDialogOpen=true;try{return new RestorePreviewDialog(this,data,incoming,full,created).ShowDialog()==true;}finally{vaultDialogOpen=false;lastVaultActivity=DateTime.UtcNow;if(sessionLockPending)LockVault();}}
        BundleEnvelope OpenBundleEnvelope(byte[] packet){try{return store.OpenBundleKeys(packet);}catch(VaultLockedException){BundleEnvelope result=null;vaultDialogOpen=true;try{var dialog=new MasterDialog(this,"解锁完整备份",(old,password,code)=>Task.Run(()=>{result=store.OpenBundleKeys(packet,password,code);}),MasterKey.HasFactor(packet));return dialog.ShowDialog()==true?result:null;}finally{vaultDialogOpen=false;if(sessionLockPending)LockVault();}}}
        void BackupAccounts(){Stop();HidePassword();RequireVaultAccess();string folder=Path.Combine(store.Home,"backups","accounts");Directory.CreateDirectory(folder);var save=new SaveFileDialog{Title="仅备份账号备忘",Filter="账号加密备份 (*.bav)|*.bav",FileName="账号备忘-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".bav",InitialDirectory=folder};if(save.ShowDialog(this)!=true)return;RequireVaultAccess();Store.WriteAtomic(save.FileName,store.Export(data));status.Text="账号备份已保存；网页登录资料不包含在此备份内。";UpdateStorageInfo();}
        async void Backup(){Stop();HidePassword();RequireVaultAccess();string suggested=FullBackup.DefaultPath(store.Home);Directory.CreateDirectory(Path.GetDirectoryName(suggested));var save=new SaveFileDialog{Title="备份资料",Filter="完整加密备份（含网页资料） (*.bampack)|*.bampack",FileName=Path.GetFileName(suggested),InitialDirectory=Path.GetDirectoryName(suggested)};if(save.ShowDialog(this)!=true)return;
            try{RequireVaultAccess();SetBusy(true);status.Text="正在备份并校验资料…";
                await Task.Run(()=>FullBackup.Create(store,save.FileName,value=>Dispatcher.BeginInvoke(new Action(()=>status.Text=value))));status.Text="完整加密备份已保存并校验，包含账号、网页资料和登记记录。";
            }catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"备份未完成，原资料保持原样。请关闭专用浏览器后重试。";}finally{SetBusy(false);UpdateStorageInfo();}
        }
        async void Import(){Stop();HidePassword();var open=new OpenFileDialog{Title="导入或恢复备份",Filter="账号／完整备份 (*.bav;*.bin;*.bampack)|*.bav;*.bin;*.bampack"};if(open.ShowDialog(this)!=true)return;
            RestoreStage stage=null;byte[] packet=null;
            try{RequireVaultAccess();if(!open.FileName.EndsWith(".bampack",StringComparison.OrdinalIgnoreCase)){var incoming=ImportVaultBackup(open.FileName);if(incoming==null||!PreviewRestore(incoming,false,DateTime.MinValue))return;var updated=Clone();int added=Accounts.Merge(updated,incoming);if(added>0)Commit(updated,null);status.Text="已添加 "+added+" 个账号，相同账号及当前网页状态未覆盖。";return;}
                packet=FullBackup.ReadKeyPacket(open.FileName);var envelope=OpenBundleEnvelope(packet);if(envelope==null)return;RequireVaultAccess();SetBusy(true);status.Text="正在读取并校验完整备份…";
                stage=await Task.Run(()=>FullBackup.Prepare(store.Home,open.FileName,envelope,value=>Dispatcher.BeginInvoke(new Action(()=>status.Text=value))));RequireVaultAccess();if(!PreviewRestore(envelope.Preview,true,envelope.CreatedUtc))return;RequireVaultAccess();
                string previous=await Task.Run(()=>FullBackup.Publish(stage,()=>!VaultDenied));store.Lock();ClearVaultData();SetBusy(false);MessageBox.Show(this,"完整备份已恢复。原资料保留在：\n"+previous+"\n\n请重新打开新版管家；使用备份当时的保护方式解锁。官方战网登录缓存没有被改写。","恢复完成");Close();
            }catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"备份无法读取或校验失败，当前资料未被覆盖。请确认备份来自当前 Windows 用户。";}
            finally{if(stage!=null)stage.Dispose();if(packet!=null)Array.Clear(packet,0,packet.Length);if(!closed){SetBusy(false);UpdateStorageInfo();}}
        }
    }
}
