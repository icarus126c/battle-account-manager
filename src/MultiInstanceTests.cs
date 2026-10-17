using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;
namespace BrowserManager {
    internal static class MultiInstanceTests {
        static byte[] Bytes(string s){return new UTF8Encoding(false).GetBytes(s);}
        static bool Reject(Action a){try{a();return false;}catch{return true;}}
        public static void Run(string report){
            var rows=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);rows.Add("通过："+label);};
            foreach(string raw in new[]{"true","\"true\""}){
                string input="\uFEFF{\r\n \"Client\" : {\"SingleInstance\" : "+raw+",\"SavedAccountNames\":\"FAKE\\u0040invalid\",\"AutoLoginCN\":\"true\",\"Credential\":\"FAKE_ONLY\"}, \"nested\":{\"SingleInstance\":true}}\r\n";
                byte[] original=Bytes(input);var changed=MultiInstanceConfig.Enable(original);
                string expected=input.Replace("\"SingleInstance\" : "+raw,"\"SingleInstance\" : "+raw.Replace("true","false"));
                check(changed.SequenceEqual(Bytes(expected)),"仅修改目标开关，完整保留其他字段、转义、BOM和换行");
                check(original.SequenceEqual(Bytes(input))&&MultiInstanceConfig.Enabled(changed),"输入不变且启用后可识别");
                check(changed.SequenceEqual(MultiInstanceConfig.Enable(changed)),"重复开启字节不变");
            }
            foreach(string input in new[]{"{\"Client\":{}}","{\"Client\":{\"Other\":123}}"})check(MultiInstanceConfig.Enabled(MultiInstanceConfig.Enable(Bytes(input))),"缺少开关时安全添加");
            foreach(string input in new[]{"{}","{\"Client\":null}","{\"Client\":{\"SingleInstance\":1}}","{\"Client\":{\"SingleInstance\":true,\"SingleInstance\":false}}"})check(Reject(()=>MultiInstanceConfig.Enable(Bytes(input))),"结构异常或重复字段时停止");
            string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"bam-multiple-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                string path=Path.Combine(dir,"fixture.config");byte[] before=Bytes("{\"Client\":{\"SingleInstance\":true,\"SavedAccountNames\":\"FAKE\"}}");File.WriteAllBytes(path,before);
                var backend=new OfficialRegionBackend(dir,path,()=>{});
                check(Reject(()=>MultiInstanceService.Enable(backend,()=>false))&&File.ReadAllBytes(path).SequenceEqual(before),"锁定时不写入");
                var running=new OfficialRegionBackend(dir,path,()=>{throw new InvalidOperationException("FAKE_RUNNING");});
                check(Reject(()=>MultiInstanceService.Enable(running,()=>true))&&File.ReadAllBytes(path).SequenceEqual(before),"运行中不修改配置");
                int calls=0;var drift=new OfficialRegionBackend(dir,path,()=>{if(++calls==3)File.WriteAllBytes(path,Bytes("{\"Client\":{\"SingleInstance\":true,\"Other\":42}}"));});
                check(Reject(()=>MultiInstanceService.Enable(drift,()=>true))&&File.ReadAllText(path).Contains("42"),"写入前配置变化时不覆盖其他操作");
                File.WriteAllBytes(path,before);MultiInstanceService.Enable(backend,()=>true);
                check(File.ReadAllBytes(path).SequenceEqual(MultiInstanceConfig.Enable(before)),"实际临时文件只改多开开关并核对结果");
                MultiInstanceService.Enable(running,()=>true);
                check(File.ReadAllBytes(path).SequenceEqual(MultiInstanceConfig.Enable(before)),"已开启时即使客户端运行也只读返回");
                check(!Directory.GetFiles(dir,"*.tmp").Any(),"成功和失败均清理临时文件");
            }finally{foreach(string file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}
            rows.Add("使用虚构配置和临时文件，未读取或修改真实战网配置。");File.WriteAllLines(report,rows,new UTF8Encoding(true));
        }
    }
}
