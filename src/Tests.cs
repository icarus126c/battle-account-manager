using System;using System.IO;using System.Text;using System.Linq;using System.Diagnostics;using System.Collections.Generic;using System.Runtime.InteropServices;
namespace CnDirect {
    public static class Tests {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]public delegate uint ShellW(IntPtr hwnd,IntPtr verb,IntPtr url,IntPtr args,IntPtr dir,int show);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]public delegate int ShellEx(IntPtr info);
        public const string Dummy="https://oauth.g.mkey.163.com/oauth-front/?client_id=ld12&state=TEST_ONLY&fromApp=apph&redirect_uri=https%3A%2F%2Faccount.battlenet.com.cn%2Flogin%2Fsignin%2Fntes";
        public const string CurrentDummy="https://oauth.g.mkey.163.com/oauth-front/?client_id=ld12&state=TEST_ONLY&fromApp=app&scheme=&scene=1&flowTrackingId=TEST_ONLY&deviceProof=TEST_ONLY&redirect_uri=https://account.battlenet.com.cn/login/signin/ntes&scope=&captchaRequired=false&isMASDK=false";
        public const string FirstDummy="https://account.battlenet.com.cn/login/zh/login.app?state=TEST_ONLY&from=app&next=TEST_ONLY%2B%2F%3D";
        public static int Fixture(bool extended) {
            var w=new ShellW((h,v,u,a,d,s)=>42);var ex=new ShellEx(info=>77);Delegate original=extended?(Delegate)ex:w;
            IntPtr slot=Marshal.AllocHGlobal(4);Marshal.WriteInt32(slot,Marshal.GetFunctionPointerForDelegate(original).ToInt32());Console.WriteLine(Native.Address(slot));Console.Out.Flush();
            string cmd;while((cmd=Console.ReadLine())!=null&&cmd!="exit"){
                string url=cmd=="first"?FirstDummy:cmd=="current"?CurrentDummy:cmd=="match"?Dummy:cmd=="alternate"?"https://account.battlenet.com.cn/login/zh/login.app?app=app":cmd=="normal"?"https://example.invalid/":cmd=="short"?"h":cmd=="long"?Stub.Prefix+new string('x',32768):null;
                IntPtr text=url==null?IntPtr.Zero:Marshal.StringToHGlobalUni(url), pointer=Native.Ptr(unchecked((uint)Marshal.ReadInt32(slot)));long value;
                try{if(extended){IntPtr info=Marshal.AllocHGlobal(60);try{for(int i=0;i<60;i++)Marshal.WriteByte(info,i,0);Marshal.WriteInt32(info,cmd=="small"?4:60);Marshal.WriteIntPtr(info,16,text);Marshal.WriteInt32(info,32,199);Marshal.WriteInt32(info,56,199);value=((ShellEx)Marshal.GetDelegateForFunctionPointer(pointer,typeof(ShellEx)))(info);Console.WriteLine(value+","+Marshal.ReadInt32(info,32)+","+Marshal.ReadInt32(info,56));}finally{Marshal.FreeHGlobal(info);}}
                    else {value=((ShellW)Marshal.GetDelegateForFunctionPointer(pointer,typeof(ShellW)))(IntPtr.Zero,IntPtr.Zero,text,IntPtr.Zero,IntPtr.Zero,1);Console.WriteLine(value);}
                }finally{if(text!=IntPtr.Zero)Marshal.FreeHGlobal(text);}Console.Out.Flush();
            }Marshal.FreeHGlobal(slot);GC.KeepAlive(original);return 0;
        }
        static string Line(Process p){var task=p.StandardOutput.ReadLineAsync();if(!task.Wait(5000))throw new TimeoutException("测试子程序未响应");string s=task.Result;if(s==null)throw new Exception("测试子程序已退出");return s;}
        static string Send(Process p,string value){p.StandardInput.WriteLine(value);p.StandardInput.Flush();return Line(p);}
        public static void Run(string report){
            var results=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);results.Add("通过："+name);};
            check(IntPtr.Size==4,"验证程序采用与当前客户端一致的 32 位调用方式");check(Flow.Allowed(Dummy),"接受已确认的国服网易回调");check(!Flow.Allowed(Dummy.Replace("account.battlenet.com.cn","evil.invalid")),"拒绝其他回调站点");check(!Flow.Allowed(Dummy+"&client_id=other"),"拒绝重复关键参数");check(!Flow.Allowed("https://example.invalid/"),"不把普通网页当国服登录");check(!Flow.Allowed(Dummy+"\""),"拒绝原始引号");
            check(Flow.Allowed(CurrentDummy)&&SessionProbe.BrowserProfiles.Allowed(CurrentDummy),"兼容当前 fromApp=app 格式与未编码的官方返回地址");
            check(!Flow.Allowed(CurrentDummy.Replace("fromApp=app&","fromApp=other&")),"仍拒绝未确认的 fromApp 值");
            check(!Flow.Allowed(CurrentDummy+"&fromApp=apph"),"拒绝重复 fromApp 参数");
            check(!Flow.Allowed(CurrentDummy.Replace("account.battlenet.com.cn","evil.invalid")),"当前格式仍拒绝其他返回域名");
            check(Flow.Allowed(FirstDummy),"允许官方登录首跳携带原始查询，不要求 app=app");
            check(Flow.Allowed("https://account.battlenet.com.cn/login/zh/login.app?app=apph"),"官方登录页的 app 值由战网处理");
            check(Flow.Allowed("https://account.battlenet.com.cn/login/zh/login.app"),"允许没有查询参数的官方登录页");
            foreach(string bad in new[]{FirstDummy.Replace("https://","http://"),FirstDummy.Replace("account.battlenet.com.cn","account.battlenet.com.cn.evil.invalid"),FirstDummy.Replace("account.battlenet.com.cn","user@account.battlenet.com.cn"),FirstDummy.Replace("account.battlenet.com.cn","account.battlenet.com.cn:444"),FirstDummy.Replace("/login/zh/login.app","/other"),"https://account.battlenet.com.cn/"})check(!Flow.Allowed(bad),"首跳仍拒绝错误域名、协议或非登录路径 · "+results.Count);
            foreach(bool extended in new[]{false,true}){
                var start=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,extended?"--fixture-ex":"--fixture-w"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var p=Process.Start(start))try{
                    uint slot=uint.Parse(Line(p));uint original;
                    using(var hook=new Hook(p.Id)){
                        original=Native.UInt(hook.ProcessHandle,Native.Ptr(slot));hook.Install(new ImportSlot{Address=Native.Ptr(slot),Original=original,Function=extended?"ShellExecuteExW":"ShellExecuteW",Module="OwnTestFixture"},Stub.Prefix,Stub.Alternate);
                        check(Send(p,"normal").StartsWith(extended?"77,":"42"),"普通网址沿原调用返回 · "+extended);
                        check(Send(p,"short").StartsWith(extended?"77,":"42"),"短字符串安全转交原调用 · "+extended);
                        check(Send(p,"null").StartsWith(extended?"77,":"42"),"空地址安全转交原调用 · "+extended);
                        check(Send(p,"long").StartsWith(extended?"77,":"42"),"超长请求不拦截 · "+extended);
                        if(extended)check(Send(p,"small").StartsWith("77,"),"不支持的结构沿原调用返回");
                        check(hook.Take()==null,"非登录调用不会生成账号请求 · "+extended);
                        check(Send(p,"match")== (extended?"1,33,0":"33"),"跨进程接管返回正常的调用结果 · "+extended);
                        check(hook.Take()==Dummy,"首次请求完整保留，不经过浏览器加载 · "+extended);
                        check(hook.Take()==null,"请求只提取一次 · "+extended);
                        check(Native.Read(hook.ProcessHandle,Native.Add(hook.Shared,8),32).All(b=>b==0),"提取后擦除测试请求缓冲区 · "+extended);
                        check(Send(p,"match").StartsWith(extended?"77,":"42"),"同一接管仅接受下一次请求 · "+extended);
                        check(hook.SeenCalls>=6,"能够判别调用入口是否实际执行 · "+extended);
                        hook.Dispose();check(!hook.RestorationFailed,"移除接管成功 · "+extended);
                    }
                    check(Send(p,"match").StartsWith(extended?"77,":"42"),"恢复后测试程序调用保持原行为 · "+extended);
                    using(var hook=new Hook(p.Id)){
                        hook.Install(new ImportSlot{Address=Native.Ptr(slot),Original=original,Function=extended?"ShellExecuteExW":"ShellExecuteW",Module="OwnTestFixture"},Stub.Prefix,Stub.Alternate);
                        check(Send(p,"alternate")== (extended?"1,33,0":"33"),"支持先打开战网官方登录地址 · "+extended);
                        check(Flow.Allowed(hook.Take()),"官方登录入口接受后验证网址 · "+extended);
                    }
                    using(var hook=new Hook(p.Id)){
                        hook.Install(new ImportSlot{Address=Native.Ptr(slot),Original=original,Function=extended?"ShellExecuteExW":"ShellExecuteW",Module="OwnTestFixture"},Stub.Prefix,Stub.Alternate);
                        check(Send(p,"current")== (extended?"1,33,0":"33"),"跨进程接管当前 app 格式 · "+extended);
                        string incoming=hook.Take();check(incoming==CurrentDummy&&Flow.Allowed(incoming),"保持当前请求原文并通过校验，不重写登录参数 · "+extended);
                    }
                    using(var hook=new Hook(p.Id)){
                        hook.Install(new ImportSlot{Address=Native.Ptr(slot),Original=original,Function=extended?"ShellExecuteExW":"ShellExecuteW",Module="OwnTestFixture"},Stub.Prefix,Stub.Alternate);
                        check(Send(p,"first")== (extended?"1,33,0":"33"),"跨进程接管不带 app=app 的官方首跳 · "+extended);
                        string incoming=hook.Take();check(incoming==FirstDummy&&Flow.Allowed(incoming)&&SessionProbe.BrowserProfiles.Allowed(incoming),"官方首跳完整保留并通过两层校验 · "+extended);
                    }
                    p.StandardInput.WriteLine("exit");p.StandardInput.Flush();check(p.WaitForExit(3000)&&p.ExitCode==0,"测试子程序正常结束 · "+extended);
                }finally{if(!p.HasExited)p.Kill();}
            }
            results.Add("合计 "+results.Count+" 项通过。测试只操作本工具的测试子程序，使用虚构网址，没有启动浏览器、接管真实战网或操作真实登录状态。真实客户端回跳仍待实测。");File.WriteAllLines(report,results,new UTF8Encoding(true));
        }
    }
}
