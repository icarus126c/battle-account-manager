using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
namespace BrowserManager {
    public static class WorkspaceTests {
        static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
        static void Call(object o,string n,params object[] args){o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);}
        static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var d in Walk(child))yield return d;}
        public static void Run(string report){var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};Func<string,string,string> advice=(json,region)=>RegionAdvice.Read(Encoding.UTF8.GetBytes(json),region);
            check(advice("{\"Services\":{\"LastLoginRegion\":\"CN\"}}","GLOBAL").Contains("可能不匹配"),"外服条目提示已保存的国服地区");
            check(advice("{\"Services\":{\"LastLoginRegion\":\"US\"}}","CN").Contains("可能不匹配"),"国服条目提示已保存的外服地区");
            foreach(string code in new[]{"US","EU","KR","TW"})check(advice("{\"Client\":{\"LastLoginRegion\":\""+code+"\"}}","GLOBAL").Contains("一致"),"识别外服地区 "+code);
            check(advice("{\"Services\":{\"LastLoginRegion\":\"CN\"},\"device\":{\"Services\":{\"LastLoginRegion\":\"US\"}}}","CN").Contains("不同区服"),"混合标记不武断指定当前区服");
            check(advice("{\"Services\":{\"LastLoginRegion\":\"NEW\"}}","CN").Contains("无法确定"),"未知地区不猜测");
            check(advice("{}","CN").Contains("无法确定"),"缺少地区不猜测");
            check(advice("{\"Account\":{\"Other\":{\"LastLoginRegion\":\"US\"}}}","CN").Contains("无法确定"),"不把任意深层字段视为登录地区");
            bool rejected=false;try{advice("{\"Services\":{\"LastLoginRegion\":\"CN\",\"LastLoginRegion\":\"US\"}}","CN");}catch{rejected=true;}check(rejected,"重复字段拒绝含糊判断");
            var bytes=Encoding.UTF8.GetBytes("{\"Services\":{\"LastLoginRegion\":\"CN\"},\"Credential\":\"FAKE_SECRET\"}");var before=(byte[])bytes.Clone();string result=RegionAdvice.Read(bytes,"CN");check(bytes.SequenceEqual(before)&&!result.Contains("FAKE_SECRET"),"检查只读且提示不泄露配置中的凭据");
            string parent=Path.GetDirectoryName(Path.GetFullPath(report));Directory.CreateDirectory(parent);
            var w=new MainWindow(true,"CN");try{
                check(!Field<Expander>(w,"accountMemo").IsExpanded&&!Field<Expander>(w,"accountNotes").IsExpanded,"账号密码和备忘默认收起");
                var labels=Walk((DependencyObject)w.Content).OfType<Button>().Select(b=>Convert.ToString(b.Content)).ToArray();check(labels.Contains("设置与备份")&&labels.Contains("解锁外服客户端")&&!labels.Contains("备份资料"),"侧栏保留两个集中入口");
                var details=Field<StackPanel>(w,"details");check(details.Children.IndexOf(Field<Border>(w,"browserCard"))<details.Children.IndexOf(Field<Expander>(w,"accountMemo")),"网页登录操作排在账号备忘之前");
                check(!Field<Expander>(w,"loginFeedback").IsExpanded&&!Walk((DependencyObject)w.Content).OfType<CheckBox>().Any(c=>Convert.ToString(c.Content).Contains("启用国服")),"反馈默认收起，删除重复的实验启用勾选");
                Entry.Render(w,Path.Combine(parent,"preview-cn.png"),1100,920);
                Call(w,"ShowRecovery");var actions=Field<WrapPanel>(w,"recoveryActions");check(actions.Visibility==Visibility.Visible&&actions.Children.OfType<Button>().Any(b=>Convert.ToString(b.Content)=="改用网页登录"),"国服失败提供网页登录处理按钮");
                check(actions.Children.OfType<Button>().Any(b=>Convert.ToString(b.Content)=="更换浏览器"),"国服浏览器失败可打开浏览器选项");
                Entry.Render(w,Path.Combine(parent,"preview-recovery.png"),1100,920);
                Field<ListBox>(w,"list").SelectedIndex=1;check(actions.Visibility==Visibility.Collapsed,"切换账号清除前一个账号的处理按钮");
                Call(w,"ShowRecovery");Call(w,"SetBusy",true);check(actions.Visibility==Visibility.Collapsed,"开始操作清除旧失败入口避免重复操作");Call(w,"SetBusy",false);
                Field<TextBlock>(w,"secret").Text="FAKE_SECRET";Field<Expander>(w,"accountMemo").IsExpanded=true;Field<Expander>(w,"accountMemo").IsExpanded=false;check(Field<TextBlock>(w,"secret").Text!="FAKE_SECRET","收起密码区域立即隐藏明文");
            }finally{w.Close();}
            var g=new MainWindow(true,"GLOBAL");try{Call(g,"ShowRecovery");var labels=Field<WrapPanel>(g,"recoveryActions").Children.OfType<Button>().Select(b=>Convert.ToString(b.Content)).ToArray();check(labels.Contains("重新认证")&&!labels.Contains("更换浏览器"),"外服失败提供外服认证入口");Entry.Render(g,Path.Combine(parent,"preview-global.png"),1100,920);}finally{g.Close();}
            var settings=new SettingsDialog(null,"资料：示例目录\n最近备份：暂无");try{var labels=Walk((DependencyObject)settings.Content).OfType<Button>().Select(b=>Convert.ToString(b.Content)).ToArray();check(new[]{"备份资料","导入备份","仅备份账号备忘","环境与战网位置","解锁外服客户端","开启战网多开"}.All(labels.Contains),"设置页集中了原有资料和环境功能");Entry.Render(settings,Path.Combine(parent,"preview-settings.png"),620,700);}finally{settings.Close();}
            rows.Add("合计 "+rows.Count+" 项通过；虚构配置和静态界面，不登录账号或修改真实战网。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
        }
    }
}
