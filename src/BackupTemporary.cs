using System;using System.IO;using System.Linq;using System.Text.RegularExpressions;
namespace BrowserManager {
    internal static class BackupTemporary {
        static bool PlainAncestors(string path){
            for(var dir=new DirectoryInfo(path);dir!=null;dir=dir.Parent)
                if(dir.Exists&&(dir.Attributes&FileAttributes.ReparsePoint)!=0)return false;
            return true;
        }
        internal static int CleanupLegacy(string home){
            string root=DataDirectory.Canonical(home),parent=Path.GetDirectoryName(root);
            if(string.IsNullOrEmpty(parent)||!PlainAncestors(root))return 0;
            int count=0;string operations=Path.Combine(root,"operations");
            if(Directory.Exists(operations)&&PlainAncestors(operations))
                foreach(string file in Directory.GetFiles(operations,"*.zip.tmp"))
                    if(Regex.IsMatch(Path.GetFileName(file),@"\A[0-9a-f]{32}\.zip\.tmp\z")&&Remove(file,operations))count++;
            if(Directory.Exists(parent))foreach(string file in Directory.GetFiles(parent,Path.GetFileName(root)+".stage-*.zip.tmp")){
                string prefix=root+".stage-",full=Path.GetFullPath(file);
                if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(full.Substring(prefix.Length),@"\A[0-9a-f]{32}\.zip\.tmp\z")&&Remove(full,parent))count++;
            }
            return count;
        }
        static bool Remove(string path,string expectedParent){
            string full=Path.GetFullPath(path);
            if(!string.Equals(Path.GetDirectoryName(full),DataDirectory.Canonical(expectedParent),StringComparison.OrdinalIgnoreCase))return false;
            try{
                if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return false;
                // Exclusive open refuses files in use; closing this handle deletes only this exact file.
                using(var stream=new FileStream(full,FileMode.Open,FileAccess.ReadWrite,FileShare.None,4096,FileOptions.DeleteOnClose)){}
                return true;
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
    }
}
