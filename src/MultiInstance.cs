using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BrowserManager {
    internal static class MultiInstanceConfig {
        static string Value(ConfigDocument doc,JsonPart client){
            if(client==null||client.Fields==null)throw new InvalidOperationException("战网配置结构无法识别，未修改多开设置。请先正常打开一次官方战网，再退出后重试。");
            var part=doc.Member(client,"SingleInstance");
            if(part==null)return null;
            string raw=doc.Text.Substring(part.Start,part.End-part.Start);
            if(raw!="true"&&raw!="false"&&raw!="\"true\""&&raw!="\"false\"")throw new InvalidOperationException("战网多开设置格式无法识别，本次未修改。");
            return raw;
        }
        internal static bool Enabled(byte[] bytes){var doc=new ConfigDocument(bytes);string value=Value(doc,doc.Client(null));return value=="false"||value=="\"false\"";}
        internal static byte[] Enable(byte[] bytes){
            var doc=new ConfigDocument(bytes);var client=doc.Client(null);string value=Value(doc,client);
            if(value=="false"||value=="\"false\"")return (byte[])bytes.Clone();
            // Change only this value, preserving all other bytes, including credential strings.
            return doc.Set(client,"SingleInstance",value=="\"true\""?"\"false\"":"false");
        }
    }
    internal static class MultiInstanceService {
        internal static string Enable(RegionBackend backend,Func<bool> allowed){
            using(var gate=new Mutex(false,@"Local\BattleAccountManager.NativeConfig")){
                bool held=false;byte[] before=null,next=null;
                try{
                    try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}
                    if(!held)throw new InvalidOperationException("另一项战网设置操作仍在进行，请稍后重试。");
                    if(!allowed())throw new InvalidOperationException("密码库已锁定，本次未修改。");
                    before=backend.Read();
                    if(MultiInstanceConfig.Enabled(before))return "战网多开已经开启，无需重复修改。可以继续使用“单独打开战网”或“网页登录”。";
                    backend.Idle();next=MultiInstanceConfig.Enable(before);
                    if(!allowed())throw new InvalidOperationException("密码库已锁定，本次未修改。");
                    backend.Idle();backend.Write(before,next);
                    return "已开启战网多开。以后无需再去官方设置勾选；可继续打开战网。账号和保持登录设置已保留。";
                }finally{if(before!=null)Array.Clear(before,0,before.Length);if(next!=null)Array.Clear(next,0,next.Length);if(held)gate.ReleaseMutex();}
            }
        }
        internal static void RequireIdle(){
            foreach(string name in new[]{"Battle.net","Battle.net Helper","Battle.net Launcher","TcNo-Acc-Switcher"}){
                var processes=Process.GetProcessesByName(name);
                try{if(processes.Length>0)throw new InvalidOperationException("首次开启多开，请先从菜单正常退出全部战网和切换工具，再点“开启战网多开”。不要退出登录；游戏和更新服务无需关闭。开启后无需重复操作。");}
                finally{foreach(var process in processes)process.Dispose();}
            }
        }
    }
    public partial class MainWindow {
        async void EnableClientMultiple(){
            if(preview||busy||closed||VaultDenied)return;
            Stop();HidePassword();SetBusy(true);status.Text="正在检查战网多开设置…";
            try{
                var backend=new OfficialRegionBackend(store.Home,OfficialRegionBackend.ConfigPath,MultiInstanceService.RequireIdle);
                status.Text=await Task.Run(()=>MultiInstanceService.Enable(backend,()=>!VaultDenied&&!closed));
            }catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"未能开启战网多开，请检查配置文件权限或稍后重试。";}
            finally{SetBusy(false);}
        }
    }
}
