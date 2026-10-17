using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BrowserManager {
 public static class StartupUiTests {
  static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
  static void Pump(Task task){
   var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;bool timeout=false;
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};timer.Tick+=delegate{timeout=true;frame.Continue=false;};timer.Start();
   task.ContinueWith(t=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
   Dispatcher.PushFrame(frame);timer.Stop();if(timeout)throw new Exception("UI operation timeout");task.GetAwaiter().GetResult();
  }
  public static void Run(string report){
   string parent=Path.GetDirectoryName(Path.GetFullPath(report)),root=Path.Combine(parent,"startup-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string originalHome=OfficialClient.SettingsHome;var originalContext=SynchronizationContext.Current;
   Func<string,string> home=name=>{string path=Path.Combine(root,name);var data=Store.Initial();data.Accounts.Add(Account.New("FAKE A","CN"));data.Accounts.Add(Account.New("FAKE B","CN"));new Store(path).Save(data);return path;};
   try {
    int reads=0;SynchronizationContext.SetSynchronizationContext(null);
    var w=new MainWindow(false,"CN",home("before-run"),null,r=>{Interlocked.Increment(ref reads);return "FAKE REGION RESULT";});
    try {
     check(reads==0,"构造窗口时不启动区服后台检查");
     SynchronizationContext.SetSynchronizationContext(null);var task=w.RefreshRegionAdviceAsync();
     Pump(task);check(Field<TextBlock>(w,"regionAdvice").Text=="FAKE REGION RESULT","没有同步上下文时，后台结果仍通过窗口线程更新");
    }finally{w.Close();}
    var fail=new MainWindow(false,"CN",home("read-error"),null,r=>{throw new IOException("FAKE READ ERROR");});
    try{Pump(fail.RefreshRegionAdviceAsync());check(Field<TextBlock>(fail,"regionAdvice").Text.Contains("暂时无法读取"),"读取失败显示提示，不导致窗口退出");}finally{fail.Close();}
    using(var release=new ManualResetEventSlim(false)){
     int count=0;var stale=new MainWindow(false,"CN",home("selection"),null,r=>{if(Interlocked.Increment(ref count)==1){if(!release.Wait(8000))throw new TimeoutException();return "OLD RESULT";}return "NEW RESULT";});
     try{var old=stale.RefreshRegionAdviceAsync();SpinWait.SpinUntil(()=>Volatile.Read(ref count)==1,3000);Field<ListBox>(stale,"list").SelectedIndex=1;Pump(stale.RefreshRegionAdviceAsync());release.Set();Pump(old);check(Field<TextBlock>(stale,"regionAdvice").Text=="NEW RESULT","切换账号后迟到的旧检查结果不会覆盖新结果");}finally{release.Set();stale.Close();}
    }
    using(var release=new ManualResetEventSlim(false)){
     var closing=new MainWindow(false,"CN",home("closing"),null,r=>{release.Wait(8000);return "LATE RESULT";});
     var pending=closing.RefreshRegionAdviceAsync();closing.Close();release.Set();Pump(pending);check(Field<TextBlock>(closing,"regionAdvice").Text!="LATE RESULT","关闭窗口后到达的后台结果不再更新界面");
    }
    string lockedHome=home("locked");var vault=new Store(lockedHome);vault.PasswordIterations=10000;vault.EnableProtection(vault.Load(),"FAKE startup password");vault.Lock();int protectedReads=0;
    var locked=new MainWindow(false,"CN",lockedHome,null,r=>{protectedReads++;return "LOCKED RESULT";});
    try{Pump(locked.RefreshRegionAdviceAsync());check(protectedReads==0&&Field<Border>(locked,"vaultOverlay").Visibility==Visibility.Visible,"受保护资料从锁定页启动，不运行区服检查");}finally{locked.Close();}
    int liveReads=0;var real=new MainWindow(false,"CN",home("application-run"),null,r=>{Interlocked.Increment(ref liveReads);return "FAKE LOADED RESULT";});
    real.ShowActivated=false;real.ShowInTaskbar=false;real.Opacity=0;
    bool loaded=false,updated=false,timedOut=false;DateTime deadline=DateTime.UtcNow.AddSeconds(8);
    real.Loaded+=delegate{loaded=true;};var watchdog=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
    watchdog.Tick+=delegate{updated=Field<TextBlock>(real,"regionAdvice").Text=="FAKE LOADED RESULT";if(updated||DateTime.UtcNow>deadline){timedOut=!updated;watchdog.Stop();real.Close();app.Shutdown();}};
    SynchronizationContext.SetSynchronizationContext(null);watchdog.Start();app.Run(real);
    check(loaded&&!timedOut&&updated&&liveReads>0,"真实 Application.Run 启动经过 Loaded 并完成后台区服检查，正常退出");
    rows.Add("合计 "+rows.Count+" 项通过。使用隔离的虚构密码库与地区读取器，不读取真实账号或修改战网。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
   } finally {OfficialClient.SettingsHome=originalHome;SynchronizationContext.SetSynchronizationContext(originalContext);if(!app.Dispatcher.HasShutdownStarted)app.Shutdown();}
  }
 }
}
