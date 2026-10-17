using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    // Advisory only: saved configuration does not identify a live client window.
    internal static class RegionAdvice {
        internal static string Read(byte[] bytes,string requested){
            var doc=new ConfigDocument(bytes);var regions=new List<string>();
            Action<JsonPart> collect=parent=>{
                if(parent==null||parent.Fields==null)return;
                foreach(string section in new[]{"Services","Client"}){
                    var part=doc.Member(parent,section);if(part==null)continue;
                    string code=doc.ReadString(part,"LastLoginRegion");if(code!=null)regions.Add(code.ToUpperInvariant());
                }
            };
            collect(doc.Root);
            foreach(var field in doc.Root.Fields)if(field.Key!="Services"&&field.Key!="Client"&&field.Value.Fields!=null)collect(field.Value);
            if(regions.Count==0||regions.Any(r=>!new[]{"CN","US","EU","KR","TW"}.Contains(r)))return "无法确定战网记住的登录地区，请以目标窗口登录页为准。";
            bool cn=regions.Any(r=>r=="CN"),global=regions.Any(r=>r!="CN");
            if(cn&&global)return "战网保存了不同区服的地区标记，请在目标登录窗口核对。";
            string saved=cn?"国服":"外服";
            if(cn!=(requested=="CN"))return "区服可能不匹配：所选账号是"+(requested=="CN"?"国服":"外服")+"，战网记住的是"+saved+"。请在目标登录页选择对应地区；缺少选项可用“解锁外服客户端”。";
            return "战网记住的登录地区与所选账号区服一致（"+saved+"）。这不代表当前窗口已登录。";
        }
    }
    internal sealed class SettingsDialog:Window {
        internal string Choice;
        internal SettingsDialog(Window owner,string summary,Func<string> multipleStatus=null){Owner=owner;Title="设置与备份";Width=620;Height=Math.Min(700,SystemParameters.WorkArea.Height*.9);WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;Background=Ui.Brush("#F4F6FB");FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI");
            var root=new DockPanel();Content=new Border{Padding=new Thickness(24),Background=Background,Child=root};var close=Ui.Button("关闭",()=>Close(),false);DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});panel.Children.Add(Ui.Text("设置与备份",24));
            var privacy=Ui.Card(panel);privacy.Children.Add(Ui.Text("访问保护",18));Add(privacy,"密码库与可选二次验证","security");
            var storage=Ui.Card(panel);storage.Children.Add(Ui.Text("资料与备份",18));storage.Children.Add(Ui.Text(summary,12));var row=new WrapPanel();storage.Children.Add(row);Add(row,"备份资料","backup");Add(row,"导入备份","restore");Add(row,"仅备份账号备忘","memo");var folders=new WrapPanel();storage.Children.Add(folders);Add(folders,"打开资料目录","data");Add(folders,"打开备份目录","backups");Add(folders,"打开保留的原目录","previous");storage.Children.Add(Ui.Text("完整备份包含网页资料。备份／恢复前请关闭专用浏览器；备份受当前 Windows 用户保护。",12));
            var transfer=Ui.Card(panel);transfer.Children.Add(Ui.Text("换电脑 · 密码迁移包",18));transfer.Children.Add(Ui.Text("用单独的迁移密码携带账号、密码和备注。登录状态需在新电脑重新建立。",12));var transferRow=new WrapPanel();transfer.Children.Add(transferRow);Add(transferRow,"导出迁移包","portable-export");Add(transferRow,"导入迁移包","portable-import");
            var client=Ui.Card(panel);client.Children.Add(Ui.Text("客户端与登录环境",18));Add(client,"环境与战网位置","environment");var multiple=Ui.Text("多开状态：正在检查…",12);client.Children.Add(multiple);Add(client,"开启战网多开","multiple");
            Loaded+=async delegate{if(multipleStatus==null){multiple.Text="多开状态：未检查";return;}try{multiple.Text=await Task.Run(multipleStatus);}catch{multiple.Text="多开状态：暂时无法读取，请先打开一次战网后正常退出。";}};client.Children.Add(Ui.Text("首次开启前请正常退出战网，保留登录状态。此设置同时适用于国服和外服。",12));Add(client,"解锁外服客户端","region");
        }
        void Add(Panel parent,string text,string key){parent.Children.Add(Ui.Button(text,()=>{Choice=key;Close();},false));}
    }
    public partial class MainWindow {
        readonly WrapPanel recoveryActions=new WrapPanel{Visibility=Visibility.Collapsed};
        readonly TextBlock regionAdvice=Ui.Text("检查战网记住的区服，不修改配置。",12);
        Expander accountMemo,accountNotes,regionAdvicePanel;string workspaceAccount;int adviceRevision;bool allowRegionRead;Func<string,string> regionAdviceReader;
        UIElement BuildRecoveryStatus(){var panel=new StackPanel();panel.Children.Add(status);panel.Children.Add(recoveryActions);return panel;}
        void ClearRecovery(){recoveryActions.Children.Clear();recoveryActions.Visibility=Visibility.Collapsed;}
        void ShowRecovery(){
            ClearRecovery();var a=Selected;if(VaultDenied||a==null||a.Archived)return;
            ActionButton(recoveryActions,a.Region=="CN"?"改用网页登录":"重新认证",()=>{if(Require().Region=="CN")PrepareWebLogin();else PrepareGlobal();},true);
            ActionButton(recoveryActions,"检查区服",()=>{regionAdvicePanel.IsExpanded=true;regionAdvicePanel.BringIntoView();CheckRegionAdvice();},false);
            ActionButton(recoveryActions,"检查登录环境",ShowEnvironment,false);ActionButton(recoveryActions,"开启战网多开",EnableClientMultiple,false);
            if(a.Region=="CN")ActionButton(recoveryActions,"更换浏览器",()=>{options.IsExpanded=true;options.BringIntoView();},false);
            recoveryActions.Visibility=Visibility.Visible;
        }
        Expander Fold(Panel content,string header){var card=(FrameworkElement)content.Parent;details.Children.Remove(card);var expander=new Expander{Header=header,Content=card,Margin=new Thickness(0,12,0,0)};details.Children.Add(expander);return expander;}
        void ArrangeWorkspace(Panel memo,Panel notes){
            accountMemo=Fold(memo,"账号与密码");accountMemo.Collapsed+=delegate{HidePassword();};accountNotes=Fold(notes,"登录方式与备忘");
            details.Children.Remove(gettingStarted);details.Children.Add(gettingStarted);
            regionAdvicePanel=new Expander{Header="区服检查",Margin=new Thickness(0,12,0,0)};var panel=new StackPanel{Margin=new Thickness(12)};regionAdvicePanel.Content=panel;panel.Children.Add(regionAdvice);panel.Children.Add(Ui.Text("仅参考保存的地区标记；多开时请分别核对各窗口。",11));var actions=new WrapPanel();panel.Children.Add(actions);ActionButton(actions,"检查区服",CheckRegionAdvice,false);ActionButton(actions,"解锁外服客户端",ShowRegionEntries,false);details.Children.Insert(2,regionAdvicePanel);
            // Keep native and web launch actions above optional notes and help.
            details.Children.Remove(globalCard);details.Children.Insert(1,globalCard);
        }
        void UpdateWorkspace(Account account){
            string key=account==null?region+":empty":account.Id;
            if(key!=workspaceAccount){workspaceAccount=key;adviceRevision++;ClearRecovery();if(loginFeedback!=null)loginFeedback.IsExpanded=false;if(accountMemo!=null){accountMemo.IsExpanded=false;accountNotes.IsExpanded=false;}regionAdvice.Text="检查战网记住的区服，不修改配置。";if(regionAdvicePanel!=null){regionAdvicePanel.IsExpanded=false;if(account!=null&&!account.Archived)CheckRegionAdvice();}}
        }
        async void CheckRegionAdvice(){
            if(!IsLoaded)return;
            await RefreshRegionAdviceAsync();
        }
        string ReadRegionAdvice(string selectedRegion){byte[] bytes=new OfficialRegionBackend(store.Home).Read();try{return RegionAdvice.Read(bytes,selectedRegion);}finally{Array.Clear(bytes,0,bytes.Length);}}
        internal async Task RefreshRegionAdviceAsync(){
            Dispatcher.VerifyAccess();
            if(preview||!allowRegionRead||VaultDenied||closed)return;
            int revision=++adviceRevision;string selectedRegion=region;regionAdvice.Text="正在读取战网保存的地区标记…";
            string result;try{result=await Task.Run(()=>regionAdviceReader(selectedRegion)).ConfigureAwait(false);}catch{result="区服设置暂时无法读取。请检查官方战网的位置，并以目标登录窗口显示为准。";}
            // Construction can run before WPF installs a synchronization context. Never
            // rely on an await continuation returning to the window's thread.
            if(Dispatcher.HasShutdownStarted||Dispatcher.HasShutdownFinished)return;
            try{await Dispatcher.InvokeAsync(new Action(()=>{
                if(closed||VaultDenied||revision!=adviceRevision)return;
                regionAdvice.Text=result;if(result.Contains("可能不匹配"))regionAdvicePanel.IsExpanded=true;
            })).Task.ConfigureAwait(false);}catch(TaskCanceledException){/* Window dispatcher is shutting down. */}
        }
        void ShowSettings(){
            RequireVaultAccess();HidePassword();UpdateStorageInfo();var dialog=new SettingsDialog(this,storageSummary.Text,()=>{byte[] bytes=new OfficialNativeClient().ReadConfig();try{return MultiInstanceConfig.Enabled(bytes)?"多开状态：已开启":"多开状态：未开启";}finally{Array.Clear(bytes,0,bytes.Length);}});dialog.ShowDialog();RequireVaultAccess();
            switch(dialog.Choice){case "multiple":EnableClientMultiple();break;case "portable-export":ExportPortable();break;case "portable-import":ChoosePortable();break;case "security":ConfigureVault();break;case "backup":Backup();break;case "restore":Import();break;case "memo":BackupAccounts();break;case "data":OpenFolder(store.Home);break;case "backups":OpenFolder(Path.Combine(store.Home,"backups"));break;case "previous":OpenPreviousFolder();break;case "environment":ShowEnvironment();break;case "region":ShowRegionEntries();break;}
        }
        void OpenPreviousFolder(){string path=Path.Combine(store.Home,"directory-info.json");if(!File.Exists(path))throw new InvalidOperationException("本资料库尚无迁移或整套恢复记录。");var info=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DirectoryInfoRecord>(File.ReadAllText(path));if(info==null||string.IsNullOrEmpty(info.Source)||!Directory.Exists(info.Source))throw new InvalidOperationException("保留的原目录不在原位置，请查看完整备份。");OpenFolder(info.Source);}
    }
}
