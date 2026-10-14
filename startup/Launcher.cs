using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

// Uses only baseline .NET 4 APIs so a missing 4.7.2 runtime can be explained before loading WPF/WebView2.
static class Launcher {
    const string Download="https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472";
    internal static string[] RequiredFiles={"BattleAccountManager.exe","BattleAccountManager.exe.config","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.Wpf.dll","WebView2Loader.dll"};
    internal static string Problem(string root,int release){
        if(release<461808)return "NET";
        foreach(string file in RequiredFiles)if(!File.Exists(Path.Combine(root,file)))return file;
        return null;
    }
    static int Release(){try{using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))return key==null?0:Convert.ToInt32(key.GetValue("Release",0));}catch{return 0;}}
    [STAThread] static int Main(string[] args){
        if(args.Length==2&&args[0]=="--self-test"){
            string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"launcher-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{if(Problem(root,461807)!="NET")throw new Exception("Older framework accepted");foreach(string f in RequiredFiles)File.WriteAllBytes(Path.Combine(root,f),new byte[]{0});if(Problem(root,461808)!=null||Problem(root,528040)!=null)throw new Exception("Supported framework rejected");foreach(string f in RequiredFiles){File.Delete(Path.Combine(root,f));if(Problem(root,528040)!=f)throw new Exception("Missing file accepted");File.WriteAllBytes(Path.Combine(root,f),new byte[]{0});}File.WriteAllText(args[1],"8 checks passed: runtime minimum / newer runtime / five missing-file cases. No app or client launched.");return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}finally{foreach(string f in Directory.GetFiles(root))File.Delete(f);Directory.Delete(root);}
        }
        if(args.Length!=0)return 1;
        Application.EnableVisualStyles();string folder=AppDomain.CurrentDomain.BaseDirectory;
        string problem=Problem(folder,Release());
        if(problem=="NET"){
            if(MessageBox.Show("需要 .NET Framework 4.7.2 或更新版本。\n是否打开微软官方下载页？安装完成后再打开管家。","缺少运行环境",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)try{Process.Start(Download);}catch{MessageBox.Show(Download);}
            return 1;
        }
        if(problem!=null){MessageBox.Show("软件包不完整，缺少："+problem+"\n请先把整个压缩包解压到一个文件夹，再打开本程序。账号资料不会因此删除。","请完整解压软件包");return 1;}
        try{Process.Start(new ProcessStartInfo(Path.Combine(folder,"BattleAccountManager.exe")){UseShellExecute=true,WorkingDirectory=folder});return 0;}catch{MessageBox.Show("账号管家未能启动。请确认软件已完整解压且目录可以访问，再重试。\n原账号资料保留。","启动未完成");return 1;}
    }
}
