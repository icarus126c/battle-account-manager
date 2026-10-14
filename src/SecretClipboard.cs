using System;using System.IO;using System.Windows;using System.Windows.Threading;
namespace BrowserManager {
    public sealed class SecretClipboard:IDisposable {
        const string OwnerFormat="BattleAccountManager.CopyOwner";string owner;
        readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
        public SecretClipboard(){timer.Tick+=delegate{ClearOwn();};}
        public static DataObject Package(string text,string tag){var data=new DataObject();data.SetText(text,TextDataFormat.UnicodeText);data.SetData(OwnerFormat,tag);data.SetData("CanIncludeInClipboardHistory",new MemoryStream(new byte[4]));data.SetData("CanUploadToCloudClipboard",new MemoryStream(new byte[4]));return data;}
        public static bool Owns(string expected,object actual){return expected!=null&&actual is string&&(string)actual==expected;}
        internal static void WriteWithRetry(Action write){for(int attempt=0;;attempt++){try{write();return;}catch(System.Runtime.InteropServices.ExternalException){if(attempt>=2)throw;System.Threading.Thread.Sleep(50);}}}
        public void Copy(string text){ClearOwn();string tag=Guid.NewGuid().ToString("N");var package=Package(text,tag);WriteWithRetry(()=>Clipboard.SetDataObject(package,true));owner=tag;timer.Interval=TimeSpan.FromSeconds(30);timer.Start();}
        public void ClearOwn(){timer.Stop();if(owner==null)return;try{if(Owns(owner,Clipboard.GetData(OwnerFormat)))Clipboard.Clear();owner=null;timer.Interval=TimeSpan.FromSeconds(30);}catch{timer.Interval=TimeSpan.FromSeconds(1);timer.Start();}}
        public void Dispose(){for(int attempt=0;attempt<3&&owner!=null;attempt++){ClearOwn();if(owner!=null)System.Threading.Thread.Sleep(25);}timer.Stop();}
    }
}
