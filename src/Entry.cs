using System;using System.IO;using System.Linq;using System.Threading;using System.Windows;using System.Windows.Media;using System.Windows.Media.Imaging;
[assembly:System.Reflection.AssemblyTitle("战网账号管家")]
[assembly:System.Reflection.AssemblyVersion("1.14.7.0")]
namespace BrowserManager {
    public static class Entry {
        internal static void Render(Window window,string path,int width,int height){var visual=(FrameworkElement)window.Content;visual.Measure(new Size(width,height));visual.Arrange(new Rect(0,0,width,height));visual.UpdateLayout();var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);}
        [STAThread] public static int Main(string[] args){
            if(args.Length==2&&args[0]=="--release-regression-test"){try{ReleaseRegressionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--multiple-test"){try{MultiInstanceTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--client-window-test"){try{ClientWindowLaunchTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--copy-test"){try{CopyActionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--portable-test"){try{PortableAccountsTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--preview-support"){
                try{var window=new SupportWindow(null);try{Render(window,args[1],460,680);}finally{window.Close();}return 0;}
                catch(Exception e){File.WriteAllText(args[1]+".error",e.ToString());return 1;}
            }
            if(args.Length==2&&args[0]=="--edition-test"){try{EditionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}} if(args.Length==2&&args[0]=="--startup-ui-test"){try{StartupUiTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}} if(args.Length==2&&args[0]=="--auto-web-test"){try{AutoWebTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}} if(args.Length==2&&args[0]=="--workspace-test"){try{WorkspaceTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}} if(args.Length==2&&args[0]=="--region-test"){try{RegionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--region-plan"){try{RegionTests.ReadOnly(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"只读预演失败："+e.GetType().Name+"。未写入实际配置。");return 1;}}
            if(args.Length==2&&args[0]=="--distribution-test"){try{DistributionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--management-test"){try{DataManagementTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--migrate-data"){try{using(var guard=new Mutex(false,@"Local\BattleLogin.CnDirect")){bool held=false;try{try{held=guard.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new InvalidOperationException("请先正常关闭旧管家再迁移资料。");DataDirectory.Ensure(AppDomain.CurrentDomain.BaseDirectory);}finally{if(held)guard.ReleaseMutex();}}File.WriteAllText(args[1],"资料已逐文件复制与校验，固定目录已启用，原目录保留；未解密密码库，未改写战网配置。",new System.Text.UTF8Encoding(true));return 0;}catch(Exception e){File.WriteAllText(args[1],e is InvalidOperationException?e.Message:"资料复制未完成，原文件保留。");return 1;}}
            if(args.Length==2&&args[0]=="--factor-test"){try{TwoFactorTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"Two-factor check failed: "+e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--password-test"){try{PasswordProtectionTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"Password protection check failed: "+e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--cn-flow-test"){try{CnEngine.Tests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"国服实验流程检查失败："+e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--cn-ui-test"){try{CnManagerTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"国服实验登记检查失败："+e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--global-engine-test"){try{GlobalTests.Engine(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"原版只读互通检查失败："+e.GetType().Name);return 1;}}
            if(args.Length==2&&args[0]=="--global-plan"){try{var data=new Store(Store.ResolveHome(AppDomain.CurrentDomain.BaseDirectory)).Load();int before=data.Accounts.Count(a=>a.Region=="GLOBAL");int added=GlobalImport.FromProbe(AppDomain.CurrentDomain.BaseDirectory,data);File.WriteAllText(args[1],"旧资料只读检查通过。已有外服条目="+before+"；可新增="+added+"；导入后外服条目="+data.Accounts.Count(a=>a.Region=="GLOBAL")+"。未写入旧资料或战网。",new System.Text.UTF8Encoding(true));return 0;}catch{File.WriteAllText(args[1],"只读导入规划未完成，原资料未改动。");return 1;}}
            if(args.Length==2&&args[0]=="--global-test"){try{GlobalTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
            if(args.Length==1&&args[0]=="--fixture-w")return CnDirect.Tests.Fixture(false);if(args.Length==1&&args[0]=="--fixture-ex")return CnDirect.Tests.Fixture(true);
            if(args.Length==2&&args[0]=="--self-test"){try{ManagerTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"虚构资料检查失败："+e.ToString());return 1;}}
            if(args.Length==2&&args[0]=="--webview-test"){try{EmbeddedTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"内置浏览器检查失败："+e.GetType().Name);return 1;}}
            if(args.Length==2&&args[0]=="--browser-test"){try{BrowserChoiceTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"浏览器选择检查失败："+e.ToString());return 1;}}
            if(args.Length==3&&args[0]=="--browser-ui-test"){try{BrowserChoiceUiTests.Run(args[1],args[2]);return 0;}catch(Exception e){File.WriteAllText(args[1],"虚构浏览器界面检查失败："+e.ToString());return 1;}}
            if(args.Length==3&&args[0]=="--webview-probe")return EmbeddedProbe.Run(args[1],args[2]);
            if(args.Length==2&&args[0]=="--check-existing"){try{string home=Store.ResolveHome(AppDomain.CurrentDomain.BaseDirectory);var data=new Store(home).Load();Store.Validate(data);int cn=data.Accounts.Count(a=>a.Region=="CN"),global=data.Accounts.Count(a=>a.Region=="GLOBAL");File.WriteAllText(args[1],"旧资料只读兼容检查通过。\r\n国服条目数量="+cn+"；外服条目数量="+global+"。\r\n原账号标识及浏览器路径保留；没有写入真实资料。\r\n");return 0;}catch{File.WriteAllText(args[1],"旧资料只读兼容检查未完成，未写入真实资料。");return 1;}}
            if(args.Length==2&&args[0]=="--formal-test"){try{FormalTests.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],"正式版检查失败："+e.ToString());return 1;}}
            bool preview=args.Length>=2&&(args[0]=="--preview"||args[0]=="--preview-editor");if(args.Length>0&&!preview)return 1;bool first;using(var mutex=new Mutex(true,preview?@"Local\BattleAccountManager.Preview":@"Local\BattleLogin.CnDirect",out first)){
                if(!first){MessageBox.Show("旧版账号管家或直连工具仍在运行，请先正常关闭旧工具，再打开本版。");return 0;}
                try{var app=new Application();if(preview){string region=args.Length==3?args[2]:"CN";var window=new MainWindow(true,region);if(args[0]=="--preview-editor"){var editor=new Editor(null,MainWindow.PreviewData().Accounts.First(a=>a.Region==region));Render(editor,args[1],640,740);}else Render(window,args[1],1100,920);}else {app.ShutdownMode=ShutdownMode.OnExplicitShutdown;string home=DataDirectory.Ready(DataDirectory.FixedHome)?DataDirectory.FixedHome:null;if(home==null){var setup=new DataSetupWindow(AppDomain.CurrentDomain.BaseDirectory);if(setup.ShowDialog()!=true)return 0;home=setup.Home;}app.ShutdownMode=ShutdownMode.OnMainWindowClose;app.Run(new MainWindow(false,"CN",null,home));}return 0;}
                catch(Exception e){if(preview){File.WriteAllText(args[1]+".error",e.ToString());return 1;}MessageBox.Show(e is InvalidOperationException?e.Message:"工具未能打开，原资料及备份已保留。请保留此提示。","战网账号管家");return 1;}
            }
        }
    }
}
