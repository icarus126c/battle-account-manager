using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using CnDirect;
namespace BrowserManager {
 public static class AutoWebTests {
  sealed class Handle:IDisposable {public bool Closed;public void Dispose(){Closed=true;}}
  sealed class Backend:AutoWebBackend {
   public Target[] Windows=new Target[0];public bool Running,Visible,FailBrowser,FailAttach;public int Launches,Waits,Prepared,Attached;public Action<Backend> OnWait;public Action OnPrepare,OnAttach;public Handle Handle=new Handle();public Target Selected;public Task BrowserWork;public Task<IDisposable> AttachWork;
   public Target[] Targets(){return Windows;}public bool ClientRunning(){return Running;}public bool HasClientWindow(){return Visible;}public void LaunchClient(){Launches++;Running=true;}
   public Task Wait(CancellationToken cancel){cancel.ThrowIfCancellationRequested();Waits++;if(OnWait!=null)OnWait(this);return Task.FromResult(0);}
   public Task PrepareBrowser(LoginRoute choice,System.Threading.CancellationToken cancel){Prepared++;if(OnPrepare!=null)OnPrepare();if(FailBrowser)throw new InvalidOperationException("FAKE_BROWSER");return BrowserWork??Task.FromResult(0);}
   public Task<IDisposable> Attach(Target target,System.Threading.CancellationToken cancel){Attached++;Selected=target;if(FailAttach)throw new InvalidOperationException("FAKE_CHANGED_WINDOW");if(OnAttach!=null)OnAttach();return AttachWork??Task.FromResult((IDisposable)Handle);}
  }
  static bool WaitForCompletion(this Task work,int milliseconds){return ((IAsyncResult)work).AsyncWaitHandle.WaitOne(milliseconds);}
  static bool Reject(Backend b,LoginRoute choice,CancellationToken token){try{using(var p=WebSessionStarter.Automatic(b,choice,token,s=>{}).GetAwaiter().GetResult()){}return false;}catch{return true;}}
  public static void Run(string report){var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};var a=Account.New("FAKE CN","CN");var t=new Target{Pid=123,Started=456,Title="FAKE LOGIN"};var choice=new LoginRoute(a);
   foreach(string mode in new[]{"webview2","chrome","edge"}){a.LoginBrowser=mode;var b=new Backend{OnWait=x=>{if(x.Waits==3)x.Windows=new[]{t};}};using(var result=WebSessionStarter.Automatic(b,new LoginRoute(a),CancellationToken.None,s=>{}).GetAwaiter().GetResult()){check(b.Launches==1&&b.Waits==3&&b.Prepared==1&&b.Attached==1&&result.Choice.Id==a.Id&&result.Choice.Mode==mode,"未运行时只启动一次战网，等待后自动准备 "+mode);}check(b.Handle.Closed,mode+" 取消接管释放句柄");}
   var existing=new Backend{Running=true,Windows=new[]{t}};check(!Reject(existing,choice,CancellationToken.None)&&existing.Launches==0&&existing.Attached==1,"已有唯一登录窗口直接准备，不重复启动");
   var booting=new Backend{Running=true,OnWait=x=>{if(x.Waits==2)x.Windows=new[]{t};}};check(!Reject(booting,choice,CancellationToken.None)&&booting.Launches==0,"已有后台启动过程只等待，不重复打开");
   var ambiguous=new Backend{Windows=new[]{t,new Target{Pid=124,Started=457}}};check(Reject(ambiguous,choice,CancellationToken.None)&&ambiguous.Attached==0&&ambiguous.Launches==0,"多个登录窗口不猜测，保留手动选择");
   var logged=new Backend{Running=true,Visible=true};check(Reject(logged,choice,CancellationToken.None)&&logged.Launches==0&&logged.Prepared==0,"已有普通战网窗口不自动登出或另开");
   var timed=new Backend();check(Reject(timed,choice,CancellationToken.None)&&timed.Waits==90&&timed.Launches==1&&timed.Attached==0,"等待超时停止，不反复启动客户端");
   using(var cancel=new CancellationTokenSource()){cancel.Cancel();var b=new Backend();check(Reject(b,choice,cancel.Token)&&b.Launches==0&&b.Prepared==0,"开始前取消不启动战网");}
   using(var cancel=new CancellationTokenSource()){var b=new Backend{OnWait=x=>cancel.Cancel()};check(Reject(b,choice,cancel.Token)&&b.Waits==1&&b.Prepared==0,"等待时取消，不继续准备浏览器");}
   using(var cancel=new CancellationTokenSource()){var b=new Backend{Windows=new[]{t},OnPrepare=()=>cancel.Cancel()};check(Reject(b,choice,cancel.Token)&&b.Attached==0,"浏览器准备后取消不安装接管");}
   using(var cancel=new CancellationTokenSource()){var b=new Backend{Windows=new[]{t},OnAttach=()=>cancel.Cancel()};check(Reject(b,choice,cancel.Token)&&b.Handle.Closed,"接管完成瞬间取消也释放句柄");}
   var unavailable=new Backend{Windows=new[]{t},FailBrowser=true};check(Reject(unavailable,choice,CancellationToken.None)&&unavailable.Attached==0,"浏览器不可用不继续接管");
   var gone=new Backend{Windows=new[]{t},FailAttach=true};check(Reject(gone,choice,CancellationToken.None)&&gone.Attached==1&&gone.Launches==0,"窗口变化时停止，不改选或重开");
   var slow=new Backend{Visible=true,OnWait=x=>{if(x.Waits==12)x.Windows=new[]{t};}};check(!Reject(slow,choice,CancellationToken.None)&&slow.Attached==1,"启动初期的临时窗口不立即误报");
   var boundary=new Backend{Visible=true,OnWait=x=>{if(x.Waits==90)x.Windows=new[]{t};}};
   check(!Reject(boundary,choice,CancellationToken.None)&&boundary.Attached==1,"最后一次等待完成时出现登录窗口仍可继续");
   using(var cancel=new CancellationTokenSource()){
    var pending=new TaskCompletionSource<bool>();var b=new Backend{Windows=new[]{t},BrowserWork=pending.Task};
    var operation=WebSessionStarter.Automatic(b,choice,cancel.Token,s=>{});cancel.Cancel();
    check(operation.WaitForCompletion(2000)&&operation.IsCanceled&&b.Attached==0,"浏览器初始化一直未完成时取消可及时返回，不继续接管");
    pending.SetResult(true);check(b.Attached==0,"取消后浏览器晚到的完成结果不触发接管");
   }
   {
    var pending=new TaskCompletionSource<bool>();var b=new Backend{Windows=new[]{t},BrowserWork=pending.Task};
    var operation=WebSessionStarter.Selected(b,choice,t,CancellationToken.None,s=>{},30);
    check(operation.WaitForCompletion(2000)&&operation.IsFaulted&&operation.Exception.InnerException is InvalidOperationException&&b.Attached==0,"手动选择窗口的浏览器初始化也有超时边界");
    pending.SetException(new InvalidOperationException("FAKE_LATE_FAILURE"));
   }
   using(var cancel=new CancellationTokenSource()){
    var pending=new TaskCompletionSource<IDisposable>();var b=new Backend{Windows=new[]{t},AttachWork=pending.Task};
    var operation=WebSessionStarter.Selected(b,choice,t,cancel.Token,s=>{});cancel.Cancel();
    check(operation.WaitForCompletion(2000)&&operation.IsCanceled,"接管仍在准备时取消及时返回");
    pending.SetResult(b.Handle);check(SpinWait.SpinUntil(()=>b.Handle.Closed,2000),"取消后晚到的接管句柄被释放");
   }
   {
    var pending=new TaskCompletionSource<IDisposable>();var b=new Backend{Windows=new[]{t},AttachWork=pending.Task};
    var operation=WebSessionStarter.Selected(b,choice,t,CancellationToken.None,s=>{},30);
    check(operation.WaitForCompletion(2000)&&operation.IsFaulted,"接管准备一直未完成时超时返回");
    pending.SetResult(b.Handle);check(SpinWait.SpinUntil(()=>b.Handle.Closed,2000),"超时后晚到的接管句柄也被释放");
   }
   using(var cancel=new CancellationTokenSource()){
    int factories=0;var pending=new TaskCompletionSource<Microsoft.Web.WebView2.Core.CoreWebView2Environment>();
    using(var sessions=new EmbeddedSessions(Path.GetTempPath(),null,null,()=>{factories++;return pending.Task;})){
     var operation=sessions.Prepare(a.Id,a.Name,cancel.Token);cancel.Cancel();
     check(operation.WaitForCompletion(2000)&&operation.IsCanceled&&factories==1,"真实内置会话类取消等待环境，不创建窗口");
     pending.SetResult(null);check(operation.IsCanceled,"环境晚到完成后仍维持取消结果，不继续创建窗口");
    }
   }
   {
    var pending=new TaskCompletionSource<Microsoft.Web.WebView2.Core.CoreWebView2Environment>();
    var sessions=new EmbeddedSessions(Path.GetTempPath(),null,null,()=>pending.Task);
    var operation=sessions.Prepare(a.Id,a.Name);sessions.Dispose();
    check(operation.WaitForCompletion(2000)&&operation.IsCanceled,"关闭会话可取消未完成的浏览器环境初始化");
    pending.SetResult(null);var retry=sessions.Prepare(a.Id,a.Name);
    check(retry.IsFaulted&&retry.Exception.InnerException is ObjectDisposedException,"已关闭会话拒绝重新打开浏览器");
   }
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));rows.Add("合计 "+rows.Count+" 项通过；使用虚构后端，没有启动真实战网或登录浏览器。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
  }
 }
}
