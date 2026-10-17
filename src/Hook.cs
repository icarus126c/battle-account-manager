using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
namespace CnDirect {
    public class Target { public int Pid;public long Started;public string Title;public override string ToString(){return Title+"（"+Pid+"）";} }
    public class Installed {public ImportSlot Slot;public uint Code;}
    public class Hook : IDisposable {
        public IntPtr ProcessHandle {get;private set;} public IntPtr Shared {get;private set;}
        readonly List<Installed> installed=new List<Installed>();public int Count{get{return installed.Count;}}
        public static string ClientPath {get{return BrowserManager.OfficialClient.ClientPath;}}
        public static List<Target> Targets() {
            var targets=new List<Target>();
            foreach(var p in Process.GetProcessesByName("Battle.net"))using(p)try{
                if(p.MainWindowHandle==IntPtr.Zero||!LoginTitle(p.MainWindowTitle)||!string.Equals(p.MainModule.FileName,ClientPath,StringComparison.OrdinalIgnoreCase))continue;
                targets.Add(new Target{Pid=p.Id,Started=p.StartTime.ToUniversalTime().Ticks,Title=p.MainWindowTitle});
            }catch{}return targets;
        }
        static bool LoginTitle(string title){return title!=null&&(title.Contains("战网登录")||title.IndexOf("Battle.net Login",StringComparison.OrdinalIgnoreCase)>=0);}
        public static Hook Attach(Target t) {
            using(var p=Process.GetProcessById(t.Pid)){
                if(p.StartTime.ToUniversalTime().Ticks!=t.Started||!LoginTitle(p.MainWindowTitle)||!string.Equals(p.MainModule.FileName,ClientPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("所选登录窗口已变化。请刷新后重新选择。");
                var hook=new Hook(t.Pid);try{
                    string root=Path.GetFullPath(Path.GetDirectoryName(ClientPath)).TrimEnd('\\')+"\\";
                    foreach(ProcessModule m in p.Modules){
                        if(!Path.GetFullPath(m.FileName).StartsWith(root,StringComparison.OrdinalIgnoreCase)||!new[]{"qwindows.dll","Qt5Core.dll","battle.net.dll"}.Contains(m.ModuleName,StringComparer.OrdinalIgnoreCase))continue;
                        foreach(var slot in Imports.Find(hook.ProcessHandle,m.BaseAddress,m.ModuleName))hook.Install(slot,Stub.Prefix,Stub.Alternate);
                    }
                    if(hook.Count==0)throw new InvalidOperationException("没有找到可接管的网页入口。请保留登录窗口并告诉我这个提示。");return hook;
                }catch{hook.Dispose();throw;}
            }
        }
        // This constructor is also used only by the own-process test fixture.
        public Hook(int pid) {
            if(IntPtr.Size!=4)throw new InvalidOperationException("请使用 32 位验证程序。");
            ProcessHandle=Native.OpenProcess(0x438,false,pid);if(ProcessHandle==IntPtr.Zero)throw new InvalidOperationException("无法接管这个战网窗口；未更改任何安全设置。");
            try{Shared=Native.Alloc(ProcessHandle,65548);}catch{Native.CloseHandle(ProcessHandle);ProcessHandle=IntPtr.Zero;throw;}
        }
        public uint SeenCalls{get{return Native.UInt(ProcessHandle,Native.Add(Shared,65544));}}
        public void Install(ImportSlot slot,string prefix,string alternate) {
            IntPtr code=Native.Alloc(ProcessHandle,4096);byte[] bytes=Stub.Make(Native.Address(code),Native.Address(Shared),slot.Original,slot.Function=="ShellExecuteExW",prefix,alternate);
            if(bytes.Length>4096)throw new InvalidDataException();Native.Write(ProcessHandle,code,bytes);Native.Executable(ProcessHandle,code,bytes.Length);
            // Record before mutation so partial failures can still restore the slot.
            var item=new Installed{Slot=slot,Code=Native.Address(code)};installed.Add(item);
            Native.PointerWrite(ProcessHandle,slot.Address,slot.Original,item.Code);
        }
        public string Take() {
            if(Native.UInt(ProcessHandle,Shared)!=1)return null;
            uint count=Native.UInt(ProcessHandle,Native.Add(Shared,4));if(count==0||count>=32767)throw new InvalidDataException();
            byte[] bytes=Native.Read(ProcessHandle,Native.Add(Shared,8),checked((int)count*2));
            try{return Encoding.Unicode.GetString(bytes);}finally{Array.Clear(bytes,0,bytes.Length);Native.Write(ProcessHandle,Native.Add(Shared,8),new byte[checked(((int)count+1)*2)]);Native.Write(ProcessHandle,Shared,BitConverter.GetBytes(2));}
        }
        public bool RestorationFailed{get;private set;}
        public void Dispose() {
            if(ProcessHandle==IntPtr.Zero)return;
            try{Native.Write(ProcessHandle,Shared,BitConverter.GetBytes(3));}catch{}
            foreach(var item in installed.AsEnumerable().Reverse())try{
                uint current=Native.UInt(ProcessHandle,item.Slot.Address);
                if(current==item.Code)Native.PointerWrite(ProcessHandle,item.Slot.Address,item.Code,item.Slot.Original);
                else if(current!=item.Slot.Original)RestorationFailed=true;
            }catch{RestorationFailed=true;}
            // Never free code that a concurrent launcher thread might still execute.
            // The small allocations are reclaimed when this selected client exits.
            Native.CloseHandle(ProcessHandle);ProcessHandle=IntPtr.Zero;
        }
    }
}
