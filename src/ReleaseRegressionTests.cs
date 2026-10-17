using System;using System.IO;using System.Linq;using System.Text;using System.Reflection;using System.Collections.Generic;using System.Windows.Controls;using CnDirect;
namespace BrowserManager {
    internal static class ReleaseRegressionTests {
        static bool Reject(Action run){try{run();return false;}catch{return true;}}
        internal static void Run(string report){
            var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};
            var old=new Target{Pid=123,Started=1};var added=new Target{Pid=456,Started=2};
            var previous=new HashSet<string>{WindowsClientWindow.ProcessKey(old)};
            check(WindowsClientWindow.NewWindowPid(new[]{old},previous)==0,"原账号窗口不被当成本次新开窗口");
            check(WindowsClientWindow.NewWindowPid(new[]{old,added},previous)==456,"独立启动识别新窗口，无需判断是否登录");
            check(WindowsClientWindow.NewWindowPid(new[]{new Target{Pid=123,Started=3}},previous)==123,"进程编号重用以启动时间区分");
            check(WindowsClientWindow.NewWindowPid(new[]{added,new Target{Pid=789,Started=3}},previous)==0,"多个新增窗口不猜选目标");
            var accept=typeof(WindowsClientWindow).GetField("acceptStartedWindow",BindingFlags.NonPublic|BindingFlags.Instance);
            check(!(bool)accept.GetValue(new WindowsClientWindow()),"网页登录后端默认仍只接受登录页");
            check((bool)accept.GetValue(new WindowsClientWindow(true,true)),"只有独立启动显式接受本次新窗口");
            var window=new MainWindow(true,"CN");
            try{
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                var buttons=(List<Button>)typeof(MainWindow).GetField("actionButtons",flags).GetValue(window);
                var before=buttons.ToDictionary(b=>b,b=>b.IsEnabled);
                var busy=typeof(MainWindow).GetMethod("SetBusy",flags);busy.Invoke(window,new object[]{true});
                check(buttons.All(b=>!b.IsEnabled),"忙碌时操作按钮全部置灰");
                check(((Button)typeof(MainWindow).GetField("cancelWeb",flags).GetValue(window)).IsEnabled,"手动取消等待不被禁用");
                busy.Invoke(window,new object[]{false});check(before.All(p=>p.Key.IsEnabled==p.Value),"结束等待恢复按钮原有启用条件");
            }finally{window.Close();}
            string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"release-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var store=new Store(Path.Combine(root,"home"));var data=Store.Initial();data.Accounts.Add(Account.New("FAKE","CN"));store.Save(data);
            for(int i=0;i<55;i++)File.WriteAllText(Path.Combine(store.Home,"data","fixture-"+i),"FAKE BROWSER CONTENT "+i);
            string operations=Path.Combine(store.Home,"operations");Directory.CreateDirectory(operations);
            string legacy=Path.Combine(operations,Guid.NewGuid().ToString("N")+".zip.tmp"),keep=Path.Combine(operations,"keep.zip.tmp");File.WriteAllText(legacy,"FAKE LEGACY");File.WriteAllText(keep,"KEEP");
            string locked=Path.Combine(operations,Guid.NewGuid().ToString("N")+".zip.tmp");File.WriteAllText(locked,"FAKE LOCKED");
            using(var handle=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                check(BackupTemporary.CleanupLegacy(store.Home)==1&&!File.Exists(legacy)&&File.Exists(locked)&&File.Exists(keep),"清理仅限旧版命名的临时ZIP，不删除占用文件或其他文件");
            }
            BackupTemporary.CleanupLegacy(store.Home);
            string pack=Path.Combine(root,"full.bampack");bool observed=false;
            FullBackup.Create(store,pack,s=>{observed=true;check(Directory.GetFiles(operations,"*.zip.tmp").SequenceEqual(new[]{keep}),"备份过程中不生成未加密ZIP");},h=>{});
            check(observed,"执行真实压缩加密写入流程");
            byte[] packet=FullBackup.ReadKeyPacket(pack);
            using(var stage=FullBackup.Prepare(store.Home,pack,store.OpenBundleKeys(packet))){
                check(File.ReadAllText(Path.Combine(stage.Path,"data","fixture-1"))=="FAKE BROWSER CONTENT 1","流式加密备份可完整恢复");
                check(!File.Exists(stage.Path+".zip.tmp"),"恢复预览前解密临时ZIP已由句柄关闭删除");
            }
            string failed=Path.Combine(root,"failed.bampack");
            check(Reject(()=>FullBackup.Create(store,failed,s=>{throw new IOException("FAKE_ABORT");},h=>{}))&&!File.Exists(failed)&&!Directory.GetFiles(root,"failed.bampack.*.tmp").Any(),"中断备份不发布成品并清理加密暂存");
            string oldPack=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"legacy.bampack");
            if(File.Exists(oldPack)){
                using(var stage=FullBackup.Prepare(store.Home,oldPack,store.OpenBundleKeys(FullBackup.ReadKeyPacket(oldPack),"FAKE master password 123"))){check(stage.Manifest.Files.Count>0,"读取上一版实际生成的加密备份");}
            }else rows.Add("未提供旧版本虚构备份，本次跳过跨版本兼容检查。");
            store.Lock();rows.Add("仅使用虚构资料；未关闭、登录或修改真实战网及账号。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
        }
    }
}
