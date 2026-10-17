using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using CnDirect;
using System.Threading;
using System.Threading.Tasks;

namespace BrowserManager {
    internal interface ClientWindowBackend {
        bool Present();
        void Start();
        Task Wait(CancellationToken cancel);
    }
    internal static class ClientWindowLaunch {
        internal static bool Request(ClientWindowBackend backend) {
            if(backend.Present())return true;
            backend.Start();return false;
        }
        internal static async Task<bool> Open(ClientWindowBackend backend,CancellationToken cancel,int attempts=30) {
            cancel.ThrowIfCancellationRequested();
            if(Request(backend))return true;
            for(int i=0;i<attempts;i++){
                await backend.Wait(cancel);cancel.ThrowIfCancellationRequested();
                if(backend.Present())return true;
            }
            return false;
        }
    }
    internal sealed class WindowsClientWindow : ClientWindowBackend {
        readonly bool loginOnly,acceptStartedWindow;
        HashSet<string> beforeStart;
        internal WindowsClientWindow(bool loginOnly=true,bool acceptStartedWindow=false){this.loginOnly=loginOnly;this.acceptStartedWindow=acceptStartedWindow;}
        internal static string ProcessKey(Target target){return target.Pid+":"+target.Started;}
        internal static int NewWindowPid(IEnumerable<Target> windows,HashSet<string> before){
            var added=windows.Where(t=>!before.Contains(ProcessKey(t))).ToArray();
            return added.Length==1?added[0].Pid:0;
        }
        static Target[] Processes(bool windowsOnly){
            string path=OfficialClient.ClientPath;var result=new List<Target>();
            foreach(var process in Process.GetProcessesByName("Battle.net"))using(process)try{
                if(!string.Equals(process.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))continue;
                if(windowsOnly&&(process.MainWindowHandle==IntPtr.Zero||string.IsNullOrEmpty(process.MainWindowTitle)))continue;
                result.Add(new Target{Pid=process.Id,Started=process.StartTime.ToUniversalTime().Ticks,Title=process.MainWindowTitle});
            }catch{}
            return result.ToArray();
        }
        internal static int LoginPid(Target[] targets){
            if(targets.Length>1)throw new InvalidOperationException("有多个待登录窗口，请展开“选择登录窗口”指定目标。");
            return targets.Length==1?targets[0].Pid:0;
        }
        internal static void CheckMultiple(byte[] bytes){
            if(!MultiInstanceConfig.Enabled(bytes))
                throw new InvalidOperationException("战网尚未开启多开。可在管家的“设置与备份”或下方操作区点击“开启战网多开”；首次开启前请正常退出全部战网，保留登录状态。");
        }

        static bool ClientRunning(){
            var processes=Process.GetProcessesByName("Battle.net");
            try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}
        }
        delegate bool EnumCallback(IntPtr window,IntPtr state);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr state);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        internal static bool PresentTarget(int preferredPid=0) {
            IntPtr found=IntPtr.Zero;bool foundVisible=false;
            EnumWindows((window,state)=>{
                uint pid;GetWindowThreadProcessId(window,out pid);
                if(preferredPid!=0&&pid!=preferredPid)return true;
                if(GetWindow(window,4)!=IntPtr.Zero)return true;
                var title=new StringBuilder(512);GetWindowText(window,title,title.Capacity);
                string caption=title.ToString();
                if(caption.IndexOf("Battle.net",StringComparison.OrdinalIgnoreCase)<0&&caption.IndexOf("战网",StringComparison.Ordinal)<0)return true;
                try{using(var process=Process.GetProcessById((int)pid)){
                    if(!process.ProcessName.Equals("Battle.net",StringComparison.OrdinalIgnoreCase))return true;
                }}catch{return true;}
                bool visible=IsWindowVisible(window);
                if(found==IntPtr.Zero||(!foundVisible&&visible)){found=window;foundVisible=visible;}
                return true;
            },IntPtr.Zero);
            if(found==IntPtr.Zero||!IsWindow(found))return false;
            ShowWindowAsync(found,IsIconic(found)?9:5);
            SetForegroundWindow(found);
            // Windows may decline foreground focus; finding a window is not proof of login.
            return true;
        }
        public bool Present(){
            if(!loginOnly)return PresentTarget();
            int pid=LoginPid(Hook.Targets().ToArray());
            if(pid!=0)return PresentTarget(pid);
            // Standalone launch may finish at a game list, error or loading window.
            // This only acknowledges a NEW client window, never authentication.
            if(acceptStartedWindow&&beforeStart!=null){pid=NewWindowPid(Processes(true),beforeStart);return pid!=0&&PresentTarget(pid);}
            return false;
        }
        public void Start(){
            if(loginOnly&&ClientRunning()){
                byte[] config=new OfficialNativeClient().ReadConfig();
                try{CheckMultiple(config);}finally{Array.Clear(config,0,config.Length);}
            }
            beforeStart=new HashSet<string>(Processes(false).Select(ProcessKey));
            string path=OfficialClient.ClientPath;
            using(var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)})){}
        }
        public Task Wait(CancellationToken cancel){return Task.Delay(500,cancel);}
    }
}
