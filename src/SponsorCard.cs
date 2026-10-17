using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace BrowserManager {
    public sealed class SponsorContent {
        public bool Enabled { get; set; }
        public string Sponsor { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
    }
    public static class SponsorPolicy {
        public static bool ValidUrl(string value) {
            Uri uri;
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 2048
                && !value.Any(char.IsControl) && !value.Contains('\\')
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
                && !uri.IsLoopback && uri.HostNameType == UriHostNameType.Dns
                && uri.Host.Contains(".");
        }
        static bool Text(string value, int limit) {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
        }
        public static SponsorContent Parse(string json) {
            if (json == null || json.Length > 8192) return null;
            try {
                var item = new JavaScriptSerializer { MaxJsonLength = 8192 }.Deserialize<SponsorContent>(json);
                if (item == null || !item.Enabled || !Text(item.Sponsor, 40) || !Text(item.Title, 60)
                    || !Text(item.Description, 180) || (!string.IsNullOrEmpty(item.Url) && !ValidUrl(item.Url))) return null;
                return item;
            } catch { return null; }
        }
        public static SponsorContent Load(string root) {
            string path = Path.Combine(root, "sponsor.json");
            try { return File.Exists(path) && new FileInfo(path).Length <= 32768 ? Parse(File.ReadAllText(path)) : null; }
            catch { return null; }
        }
        static string Preference(string home) { return Path.Combine(home, "data", "sponsor-hidden.txt"); }
        public static bool Hidden(string home) {
            try { string path = Preference(home); return File.Exists(path) && new FileInfo(path).Length < 16 && File.ReadAllText(path) == "hidden"; }
            catch { return true; }
        }
        public static void SetHidden(string home, bool value) {
            Store.WriteAtomic(Preference(home), Encoding.UTF8.GetBytes(value ? "hidden" : "visible"));
        }
    }
    public static class SponsorCard {
        public static FrameworkElement Create(string home, string root, bool preview) {
            var shell = new StackPanel { Margin = new Thickness(26, 0, 26, 0) };
            if (!Edition.Sponsorship) return shell;
            var content = SponsorPolicy.Load(root);
            if (content == null) return shell;
            var panel = new StackPanel();
            var card = new Border { Child = panel, Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(8), Background = Ui.Brush("#FFF6E9"),
                BorderBrush = Ui.Brush("#E9D9BE"), BorderThickness = new Thickness(1) };
            shell.Children.Add(card);
            var heading = new DockPanel();
            panel.Children.Add(heading);
            var close = Ui.Button("关闭", () => { }, false);
            close.Margin = new Thickness(8, 0, 0, 0);
            close.Padding = new Thickness(8, 2, 8, 2);
            DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            heading.Children.Add(Ui.Text("广告 / 赞助 · " + content.Sponsor, 11));
            panel.Children.Add(Ui.Text(content.Title, 15));
            panel.Children.Add(Ui.Text(content.Description, 12));
            if (!string.IsNullOrEmpty(content.Url)) {
                var visit = Ui.Button("访问合作方 · " + new Uri(content.Url).Host, () => {
                    if (preview) return;
                    try { using (var p = Process.Start(new ProcessStartInfo(content.Url) { UseShellExecute = true })) { } }
                    catch { MessageBox.Show("未能打开合作方网页。账号和登录功能不受影响。", "赞助展示"); }
                }, false);
                panel.Children.Add(visit);
            }
            var restore = Ui.Button("显示赞助卡片", () => { }, false);
            restore.HorizontalAlignment = HorizontalAlignment.Right;
            restore.Margin = new Thickness(0);
            shell.Children.Add(restore);
            Action<bool> update = hidden => {
                card.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                restore.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
            };
            Action<bool> choose = hidden => {
                update(hidden);
                if (preview) return;
                try { SponsorPolicy.SetHidden(home, hidden); }
                catch { restore.Content = hidden ? "显示赞助卡片（仅本次关闭）" : "显示赞助卡片"; }
            };
            close.Click += delegate { choose(true); };
            restore.Click += delegate { choose(false); };
            update(!preview && SponsorPolicy.Hidden(home));
            return shell;
        }
    }
}
