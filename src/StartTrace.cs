using System;using System.IO;using System.Text;
namespace BrowserManager {
 public sealed class StartTrace {
  readonly string path;public StartTrace(string home):this(home,false){}public StartTrace(string home,bool foreign){path=TracePath(home,foreign);try{Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"客户端启动诊断，仅记录固定阶段及错误类型，不记录账号、密码或登录参数。\r\n",new UTF8Encoding(true));}catch{}}
  static string TracePath(string home,bool foreign){return Path.Combine(Path.GetFullPath(home),"data","diagnostics",foreign?"last-global-start.txt":"last-client-start.txt");}
  public void Stage(string value){Append("阶段："+value);}
  public void Failed(Exception e){Append("失败类型："+e.GetType().Name+"；错误码："+e.HResult);}
  void Append(string text){try{File.AppendAllText(path,DateTime.Now.ToString("HH:mm:ss")+" "+text+"\r\n",new UTF8Encoding(true));}catch{}}
  public static string Read(string home){return Read(home,false);}public static string Read(string home,bool foreign){string path=TracePath(home,foreign);if(!File.Exists(path))return "尚无启动记录。";if(new FileInfo(path).Length>32768)return "启动记录超出显示限制，请保留原文件。";return File.ReadAllText(path);}
 }
}
