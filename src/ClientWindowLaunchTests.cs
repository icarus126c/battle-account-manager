using CnDirect;using System.Linq;using System;using System.IO;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;
namespace BrowserManager {
    internal static class ClientWindowLaunchTests {
        sealed class Fake:ClientWindowBackend {
            public bool LoggedInWindow;public bool Visible;public int Starts,Waits,Presentations;public Action<Fake> Tick;
            public bool Present(){Presentations++;return Visible;}
            public void Start(){Starts++;}
            public Task Wait(CancellationToken cancel){cancel.ThrowIfCancellationRequested();Waits++;if(Tick!=null)Tick(this);return Task.FromResult(0);}
        }
        public static void Run(string report){
            var lines=new List<string>();Action<bool,string> check=(ok,s)=>{if(!ok)throw new Exception(s);lines.Add("通过："+s);};
            var open=new Fake{Visible=true};check(ClientWindowLaunch.Open(open,CancellationToken.None).Result&&open.Starts==0&&open.Waits==0,"已有待登录窗口直接使用，不重复启动");
            var logged=new Fake{LoggedInWindow=true,Tick=f=>{if(f.Waits==2)f.Visible=true;}};
            check(ClientWindowLaunch.Open(logged,CancellationToken.None).Result&&logged.Starts==1&&logged.LoggedInWindow,"只有已登录窗口时启动新登录窗口，保留原窗口");
            var onlyLogged=new Fake{LoggedInWindow=true};
            check(!ClientWindowLaunch.Open(onlyLogged,CancellationToken.None).Result&&onlyLogged.Starts==1,"一直只有已登录窗口时不误报成功");
            check(WindowsClientWindow.LoginPid(new Target[0])==0,"无登录目标时不能选中已登录进程");
            check(WindowsClientWindow.LoginPid(new[]{new Target{Pid=123}})==123,"唯一登录目标按进程选择");
            bool multipleRejected=false;try{WindowsClientWindow.LoginPid(new[]{new Target{Pid=123},new Target{Pid=456}});}catch(InvalidOperationException){multipleRejected=true;}
            check(multipleRejected,"多个登录目标时停止，不猜选窗口");
            foreach(string flag in new[]{"false","\"false\""}){
                byte[] config=Encoding.UTF8.GetBytes("{\"Client\":{\"SingleInstance\":"+flag+",\"SavedAccountNames\":\"FAKE\"}}");var before=(byte[])config.Clone();
                WindowsClientWindow.CheckMultiple(config);check(config.SequenceEqual(before),"多开设置兼容布尔和文本格式，只读保留账号配置");
            }
            foreach(string config in new[]{"{}","{\"Client\":{}}","{\"Client\":{\"SingleInstance\":true}}"}){
                bool rejected=false;try{WindowsClientWindow.CheckMultiple(Encoding.UTF8.GetBytes(config));}catch(InvalidOperationException){rejected=true;}
                check(rejected,"未启用多开时明确停止");
            }
            var delayed=new Fake{Tick=f=>{if(f.Waits==4)f.Visible=true;}};check(ClientWindowLaunch.Open(delayed,CancellationToken.None).Result&&delayed.Starts==1&&delayed.Waits==4,"等待实际窗口出现后才返回找到窗口");
            var missing=new Fake();check(!ClientWindowLaunch.Open(missing,CancellationToken.None).Result&&missing.Starts==1&&missing.Waits==30,"没有窗口时超时返回失败，只有一次启动请求");
            var edge=new Fake{Tick=f=>{if(f.Waits==30)f.Visible=true;}};check(ClientWindowLaunch.Open(edge,CancellationToken.None).Result,"最后一次等待出现窗口仍成功");
            using(var cancel=new CancellationTokenSource()){
                cancel.Cancel();var fake=new Fake();var task=ClientWindowLaunch.Open(fake,cancel.Token);check(task.IsCanceled&&fake.Starts==0,"预先取消不启动客户端");
            }
            using(var cancel=new CancellationTokenSource()){
                var fake=new Fake{Tick=f=>cancel.Cancel()};var task=ClientWindowLaunch.Open(fake,cancel.Token);check(task.IsCanceled&&fake.Starts==1&&fake.Waits==1,"等待中取消及时停止，不追加启动");
            }
            lines.Add("使用虚构窗口后端，未启动或操作实际战网。Windows 前台限制及真实客户端需实机核对。");
            File.WriteAllLines(report,lines,new UTF8Encoding(true));
        }
    }
}
