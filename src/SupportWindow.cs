using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowserManager {
    // A local, user-opened page. It never reads account data or starts a payment.
    public sealed class SupportWindow : Window {
        public static Button Link(Action open) {
            var text = new TextBlock { Text="支持作者", FontSize=12, Foreground=Ui.Brush("#596579") };
            var button = new Button {
                Content=text, Background=Brushes.Transparent, BorderThickness=new Thickness(0),
                HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(2,6,8,6),
                Margin=new Thickness(0,2,0,0), Cursor=System.Windows.Input.Cursors.Hand,
                ToolTip="自愿赞助 · 支付宝"
            };
            button.Click += delegate { open(); };
            return button;
        }

        public SupportWindow(Window owner) {
            if(owner!=null)Owner=owner;
            Title="支持作者"; Width=Math.Min(460,SystemParameters.WorkArea.Width*.92);
            Height=Math.Min(710,SystemParameters.WorkArea.Height*.9);
            WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            ResizeMode=ResizeMode.CanResize; ShowInTaskbar=false;
            FontFamily=new FontFamily("Microsoft YaHei UI"); Background=Ui.Brush("#F7F8FA");
            UseLayoutRounding=true;
            var dock=new DockPanel { Margin=new Thickness(24,20,24,18) }; Content=new Border { Background=Background, Child=dock };
            var close=Ui.Button("返回",()=>Close(),false);
            close.Width=96;close.HorizontalAlignment=HorizontalAlignment.Center;close.IsCancel=true;
            DockPanel.SetDock(close,Dock.Bottom);dock.Children.Add(close);
            var panel=new StackPanel();
            dock.Children.Add(new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            var title=Ui.Text("谢谢你的支持",22);title.Foreground=Ui.Brush("#293447");panel.Children.Add(title);
            var intro=Ui.Text("如果这个小工具帮到了你，欢迎随意赞助一点，支持后续维护。",13);
            intro.Foreground=Ui.Brush("#596579");intro.Margin=new Thickness(0,8,0,16);panel.Children.Add(intro);
            var bitmap=new BitmapImage();
            using(var stream=typeof(SupportWindow).Assembly.GetManifestResourceStream("BattleAccountManager.Support.Alipay.png")) {
                if(stream==null)throw new InvalidOperationException("赞助图片暂时无法加载，请继续正常使用账号管家。");
                bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
            }
            var image=new Image { Source=bitmap, MaxWidth=360, Stretch=Stretch.Uniform, HorizontalAlignment=HorizontalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(image,"支付宝赞助收款码");
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);panel.Children.Add(image);
            var caption=Ui.Text("使用支付宝扫一扫，金额由你决定。",13);
            caption.TextAlignment=TextAlignment.Center;caption.Margin=new Thickness(0,14,0,5);panel.Children.Add(caption);
            var note=Ui.Text("赞助自愿，不影响任何功能。\n付款前请核对收款人，到账以支付宝为准。",11);
            note.TextAlignment=TextAlignment.Center;note.Foreground=Ui.Brush("#667085");note.Margin=new Thickness(0,0,0,12);panel.Children.Add(note);
        }
    }
}
