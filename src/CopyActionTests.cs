using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;

namespace BrowserManager {
    public static class CopyActionTests {
        static T Field<T>(object instance,string name){return (T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(instance);}
        static void Call(object instance,string name,params object[] values){typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(instance,values);}
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        public static void Run(string report){
            var lines=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);lines.Add("通过："+label);};
            string parent=Path.GetDirectoryName(Path.GetFullPath(report)),root=Path.Combine(parent,"copy-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                var store=new Store(root);var data=Store.Initial();var account=Account.New("FAKE external","GLOBAL");account.Username="";account.LoginAccount="fake-login@example.invalid";account.Password="  FAKE copy secret 口令  ";data.Accounts.Add(account);store.Save(data);byte[] before=File.ReadAllBytes(store.FilePath);
                string captured=null;int calls=0;var window=new MainWindow(false,"GLOBAL",root);
                typeof(MainWindow).GetField("copyToClipboard",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,new Action<string>(value=>{captured=value;calls++;}));
                try{
                    var login=Field<Button>(window,"quickLoginCopy");var password=Field<Button>(window,"quickPasswordCopy");var nick=Field<Button>(window,"quickNicknameCopy");
                    check(Convert.ToString(login.Content)=="复制登录邮箱"&&Convert.ToString(nick.Content)=="复制战网昵称","登录邮箱与昵称入口明确分开");
                    check(!Field<Expander>(window,"accountMemo").IsExpanded&&login.Parent is WrapPanel,"复制入口不在折叠的账号备忘里");
                    Click(login);check(captured==account.LoginAccount&&calls==1,"实际邮箱按钮在昵称为空时仍复制登录邮箱");
                    check(Field<TextBlock>(window,"copyFeedback").Visibility==Visibility.Visible&&Field<TextBlock>(window,"copyFeedback").Text.Contains("已复制"),"按钮旁直接显示成功反馈");
                    Click(nick);check(calls==1&&Field<TextBlock>(window,"copyFeedback").Text.Contains("未填写"),"空昵称明确提示且不误复制邮箱");
                    Click(password);check(captured==account.Password,"密码复制保留中文和前后空白");
                    check(!Field<TextBlock>(window,"copyFeedback").Text.Contains(account.Password)&&!Field<TextBlock>(window,"status").Text.Contains(account.Password),"复制提示不显示密码内容");
                    Call(window,"SetBusy",true);check(!login.IsEnabled&&!password.IsEnabled,"登录处理中复制按钮停用");Click(login);check(calls==2,"登录处理中即使触发按钮也不会复制");Call(window,"SetBusy",false);
                    typeof(MainWindow).GetField("copyToClipboard",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,new Action<string>(value=>{throw new ExternalException("FAKE busy");}));
                    Click(login);check(Field<TextBlock>(window,"copyFeedback").Text.Contains("占用")&&!Field<TextBlock>(window,"copyFeedback").Text.Contains("已复制"),"剪贴板失败显示占用提示，不谎报成功");
                    var loaded=Field<Data>(window,"data");loaded.Accounts[0].Username="FAKE BattleTag";
                    check(MainWindow.CopyValue(loaded.Accounts[0],"nickname")=="FAKE BattleTag"&&MainWindow.CopyValue(loaded.Accounts[0],"login")==account.LoginAccount,"昵称与邮箱内容不同也不会混用");
                    Call(window,"UpdateDetails");Entry.Render(window,Path.Combine(parent,"preview-copy-global.png"),1100,920);
                    check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"复制操作不更改原账号资料");
                    var localStore=Field<Store>(window,"store");localStore.PasswordIterations=10000;localStore.EnableProtection(loaded,"FAKE master password 123");Call(window,"LockVault");
                    check(!login.IsEnabled&&!password.IsEnabled&&!nick.IsEnabled,"锁定后快捷复制按钮停用");
                }finally{window.Close();}
                int attempts=0;SecretClipboard.WriteWithRetry(()=>{attempts++;if(attempts<3)throw new ExternalException();});check(attempts==3,"剪贴板短暂占用后可重试成功");
                attempts=0;bool rejected=false;try{SecretClipboard.WriteWithRetry(()=>{attempts++;throw new ExternalException();});}catch(ExternalException){rejected=true;}check(rejected&&attempts==3,"持续占用有限重试后返回失败");
                var package=SecretClipboard.Package("FAKE_COPY","TAG");check(package.GetText(TextDataFormat.UnicodeText)=="FAKE_COPY"&&package.GetData("CanIncludeInClipboardHistory") is MemoryStream&&package.GetData("CanUploadToCloudClipboard") is MemoryStream,"保留标准 Unicode 文本及禁用历史与云同步标记");
                lines.Add("全部虚构账号；复制目标为内存回调，没有读取或改写系统剪贴板，没有向真实战网粘贴。");File.WriteAllLines(report,lines,new UTF8Encoding(true));
            }finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("copy-fixture-",StringComparison.Ordinal))Directory.Delete(root,true);}
        }
    }
}
