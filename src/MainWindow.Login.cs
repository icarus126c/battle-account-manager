using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Windows;using CnDirect;
namespace BrowserManager {
    public partial class MainWindow {
        internal static WindowsClientWindow StandaloneWindow(){return new WindowsClientWindow(true,true);}
        async void OpenClient(){
            if(busy||closed||VaultDenied)return;
            Stop();HidePassword();SetBusy(true);
            var cancel=new CancellationTokenSource();webPreparation=cancel;
            cancelStandalone.Visibility=Visibility.Visible;
            var trace=new StartTrace(store.Home,region=="GLOBAL");
            status.Text="正在查找登录窗口；没有待登录窗口时将启动战网…";trace.Stage("单独打开战网：查找待登录窗口或发送启动请求");
            try{
                bool found=await ClientWindowLaunch.Open(StandaloneWindow(),cancel.Token);
                if(closed||VaultDenied)return;
                status.Text=found?"战网窗口已打开，请在客户端继续操作并核对账号。这不代表登录已成功。":"已发出启动请求，暂未识别到目标窗口。请检查任务栏或更新提示；已打开的窗口保持运行。";
                trace.Stage(found?"已找到客户端窗口（不代表登录成功）":"等待窗口超时；未重复启动");
                if(!found)ShowRecovery();
            }catch(OperationCanceledException){status.Text="已停止等待战网窗口。已打开的战网保持运行。";trace.Stage("用户停止等待窗口");}
            catch(Exception e){trace.Failed(e);status.Text=e is InvalidOperationException?e.Message:"战网启动未完成，请检查安装位置、系统权限或更新提示。";ShowRecovery();}
            finally{webPreparation=null;cancel.Dispose();cancelStandalone.Visibility=Visibility.Collapsed;SetBusy(false);}
        }
        void RefreshTargets(){Stop();targets.ItemsSource=Hook.Targets().OrderByDescending(t=>t.Started).ToList();targets.SelectedIndex=targets.Items.Count>0?0:-1;status.Text=targets.Items.Count>0?"选择国服登录窗口，然后接管下一次新请求。":"未找到国服登录窗口，请先让战网停在国服登录页。";}
        bool ClearHook(){hookTimer.Stop();if(cancelWeb!=null)cancelWeb.Visibility=Visibility.Collapsed;if(hook==null)return false;var old=hook;hook=null;armedRoute=null;old.Dispose();return old.RestorationFailed;}
        void StartGlobal(){RunGlobal(false);}
        void PrepareGlobal(){RunGlobal(true);}
        async void RunGlobal(bool firstLogin){
            Account selected;Data snapshot;bool skipRunning=skipClientCheck.IsChecked==true;
            try{selected=Require().Copy();GlobalIdentity.Key(selected);snapshot=Clone();snapshot.Security=null;foreach(var memo in snapshot.Accounts)memo.Password="";selected.Password="";}catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"请先确认外服登录邮箱。";return;}
            Stop();HidePassword();SetBusy(true);
            var trace=new StartTrace(store.Home,true);startStage=firstLogin?"准备首次登录":"准备外服切换";trace.Stage(startStage);trace.Stage(skipRunning?"运行检测：用户已跳过":"运行检测：开启");startWatch.Start();
            status.Text=startStage+"，请勿重复点击。";
            Action<string> progress=stage=>{trace.Stage(stage);startStage=stage;Dispatcher.BeginInvoke(new Action(()=>{if(!closed&&busy)status.Text=stage+"…";}));};
            try{
                await Task.Run(()=>{var backend=new TcNoGlobalBackend(store.Home,AppDomain.CurrentDomain.BaseDirectory,skipRunning);if(firstLogin)GlobalLaunch.PrepareLogin(backend,snapshot,selected,progress);else GlobalLaunch.Start(backend,snapshot,selected,progress);});
                Used(selected.Id);LoginRequested(selected.Id,firstLogin?"first-login":"native");
                status.Text=firstLogin?"已打开官方登录窗口。请手动登录“"+selected.Name+"”，勾选保持登录，核对昵称后正常退出；以后点“切换并启动”。":"已为“"+selected.Name+"”发起启动，请核对客户端账号。若要求密码，请使用“账号密码登录”；启动完成不等于登录已成功。";
            }catch(Exception e){trace.Failed(e);ShowRecovery();status.Text=e is InvalidOperationException?e.Message:"外服准备未完成，请在“帮助与记录”查看切换记录。修改前备份已保留。";}
            finally{startWatch.Stop();SetBusy(false);}
        }
        void Stop(){if(webPreparation!=null)webPreparation.Cancel();if(hook!=null)status.Text=ClearHook()?"接管移除尚未核对，请正常退出并重开所选战网窗口。":"已取消接管。";}
        void Tick(object sender,EventArgs e){if(hook==null)return;try{string url=hook.Take();if(url!=null){var a=armedRoute;bool failed=ClearHook();try{if(!Flow.Allowed(url))status.Text="国服请求未通过校验，请从战网重新发起；不要发送完整网址。";else{if(a.IsEmbedded)embedded.OpenReady(a.Id,url);else Browser.Open(store.Home,a.Id,url,a.Mode);status.Text="新请求已交给“"+a.Name+"”的 "+Browser.Label(a.Mode)+"，请手动完成网页确认，并核对客户端账号。";Used(a.Id);LoginRequested(a.Id,"web");}}finally{url=null;if(failed)status.Text+=" 接管移除尚未核对，请登录完成后正常退出并重开战网。";}}else if(DateTime.UtcNow-armedAt>TimeSpan.FromMinutes(5)){bool failed=ClearHook();status.Text=failed?"接管超时且移除尚未核对，请正常退出并重开该战网窗口。":"接管已过期，请重新选择窗口并发起登录。";}}catch{bool failed=ClearHook();status.Text="窗口已关闭或所选浏览器未能打开，请重新准备并从战网发起登录。";ShowRecovery();if(failed)status.Text+=" 请正常退出并重开该战网窗口。";}}
    }
}
