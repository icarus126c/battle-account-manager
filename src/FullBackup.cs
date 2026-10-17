using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
namespace BrowserManager {
    public sealed class BundleFile {public string Name {get;set;}public long Length {get;set;}public string Hash {get;set;}}
    public sealed class BundleManifest {public int Format {get;set;}public DateTime CreatedUtc {get;set;}public List<BundleFile> Files {get;set;}}
    public sealed class RestoreStage:IDisposable { public string Path,Home;public BundleEnvelope Envelope;public BundleManifest Manifest;public bool Published;public void Dispose(){if(!Published)DataDirectory.CleanupStage(Home,Path);}}
    public static class FullBackup {
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("BAMPACK1");const long MaxTotal=20L*1024*1024*1024;const int MaxEntries=100000;
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
        static string HashFile(string path){return DataDirectory.Digest(path).Replace("-","");}
        public static string DefaultPath(string home){return System.IO.Path.Combine(home,"backups","full","完整备份-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".bampack");}
        public static void Create(Store store,string output,Action<string> progress=null,Action<string> idle=null) {
            string full=System.IO.Path.GetFullPath(output);if(DataDirectory.Inside(System.IO.Path.Combine(store.Home,"data"),full)||DataDirectory.Inside(System.IO.Path.Combine(store.Home,"cn-exp"),full))throw new InvalidOperationException("请将备份保存在资料目录之外，或使用默认备份位置。");
            (idle??DataDirectory.ProfileIdle)(store.Home);var inventory=DataDirectory.Inventory(store.Home);if(inventory.Count>MaxEntries)throw new InvalidDataException();
            BackupTemporary.CleanupLegacy(store.Home);
            string temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            byte[] keys=new byte[64],iv=new byte[16],packet=null;
            using(var rng=RandomNumberGenerator.Create()){rng.GetBytes(keys);rng.GetBytes(iv);}
            var manifest=new BundleManifest{Format=1,CreatedUtc=DateTime.UtcNow,Files=new List<BundleFile>()};
            try{
                string vaultHash=HashFile(store.FilePath);packet=store.SealBundleKeys(keys,vaultHash);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
                using(var dest=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
                    dest.Write(Magic,0,8);dest.Write(BitConverter.GetBytes(packet.Length),0,4);dest.Write(packet,0,packet.Length);dest.Write(iv,0,16);
                    using(var aes=Aes.Create()){
                        aes.Key=keys.Take(32).ToArray();aes.IV=iv;
                        using(var encrypt=new CryptoStream(dest,aes.CreateEncryptor(),CryptoStreamMode.Write,true)){
                            // ZipArchive supports non-seekable output; plaintext never lands in a ZIP file.
                            using(var archive=new ZipArchive(new PositionWriteStream(encrypt),ZipArchiveMode.Create,true)){
                                long total=0;int count=0;
                                foreach(var pair in inventory){
                                    long length=new FileInfo(pair.Value).Length;total+=length;if(total>MaxTotal)throw new InvalidDataException();
                                    string name=pair.Key.Replace('\\','/');var entry=archive.CreateEntry(name,CompressionLevel.Fastest);
                                    string copiedHash;
                                    using(var input=new FileStream(pair.Value,FileMode.Open,FileAccess.Read,FileShare.None))using(var outputEntry=entry.Open()){
                                        input.CopyTo(outputEntry);input.Position=0;using(var sha=SHA256.Create())copiedHash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                                    }
                                    manifest.Files.Add(new BundleFile{Name=name,Length=length,Hash=copiedHash});
                                    if(progress!=null&&++count%50==0)progress("备份资料 "+count+" / "+inventory.Count);
                                }
                                var metadata=archive.CreateEntry("bundle-info.json");using(var writer=new StreamWriter(metadata.Open(),new UTF8Encoding(false)))writer.Write(new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(manifest));
                            }
                            encrypt.FlushFinalBlock();
                        }
                    }
                    (idle??DataDirectory.ProfileIdle)(store.Home);var after=DataDirectory.Inventory(store.Home);
                    if(!after.Keys.SequenceEqual(inventory.Keys))throw new IOException("备份期间资料发生变化。");
                    foreach(var item in manifest.Files)if(item.Hash!=HashFile(System.IO.Path.Combine(store.Home,item.Name.Replace('/',System.IO.Path.DirectorySeparatorChar))))throw new IOException("备份期间资料发生变化。");
                    if(manifest.Files.Single(f=>f.Name=="data/vault-v2.bin").Hash!=vaultHash)throw new IOException("备份期间密码库发生变化。");
                    dest.Flush(true);long encryptedLength=dest.Length;dest.Position=0;byte[] tag=Mac(dest,encryptedLength,keys.Skip(32).ToArray());dest.Position=encryptedLength;dest.Write(tag,0,tag.Length);dest.Flush(true);Array.Clear(tag,0,tag.Length);
                }
                using(var verify=File.OpenRead(temporary))Verify(verify,keys);
                if(File.Exists(full))File.Replace(temporary,full,full+".bak");else File.Move(temporary,full);
            }finally{foreach(var bytes in new[]{keys,iv,packet})if(bytes!=null)Array.Clear(bytes,0,bytes.Length);if(File.Exists(temporary))File.Delete(temporary);}
        }

        static byte[] Mac(Stream input,long length,byte[] key){try{using(var hmac=new HMACSHA256(key)){byte[] buffer=new byte[65536];while(length>0){int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,length));if(read<=0)throw new EndOfStreamException();hmac.TransformBlock(buffer,0,read,buffer,0);length-=read;}hmac.TransformFinalBlock(new byte[0],0,0);return hmac.Hash;}}finally{Array.Clear(key,0,key.Length);}}
        static void Verify(FileStream file,byte[] keys){if(file.Length<80||file.Length>MaxTotal)throw new InvalidDataException();file.Position=0;byte[] actual=Mac(file,file.Length-32,keys.Skip(32).ToArray()),expected=new byte[32];if(file.Read(expected,0,32)!=32)throw new EndOfStreamException();int difference=0;for(int i=0;i<32;i++)difference|=actual[i]^expected[i];Array.Clear(actual,0,actual.Length);Array.Clear(expected,0,expected.Length);if(difference!=0)throw new InvalidDataException("完整备份校验失败，原资料未改动。");}
        public static byte[] ReadKeyPacket(string path){using(var file=File.OpenRead(path)){byte[] magic=new byte[8],size=new byte[4];if(file.Read(magic,0,8)!=8||!magic.SequenceEqual(Magic)||file.Read(size,0,4)!=4)throw new InvalidDataException();int length=BitConverter.ToInt32(size,0);if(length<1||length>17*1024*1024||file.Length<12L+length+16+16+32||file.Length>MaxTotal)throw new InvalidDataException();byte[] packet=new byte[length];int position=0;while(position<length){int read=file.Read(packet,position,length-position);if(read==0)throw new EndOfStreamException();position+=read;}return packet;}}
        static void CopyBounded(Stream input,Stream output,long length){byte[] buffer=new byte[65536];while(length>0){int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,length));if(read<=0)throw new EndOfStreamException();output.Write(buffer,0,read);length-=read;}if(input.ReadByte()!=-1)throw new InvalidDataException();}
        static bool BadSegment(string segment){string name=segment.Split('.')[0].ToUpperInvariant();return segment.Any(c=>System.IO.Path.GetInvalidFileNameChars().Contains(c))||new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(name);}
        static bool ValidName(string name){if(string.IsNullOrEmpty(name)||name.Length>1000||name.Contains('\\')||name.Contains(':')||name.StartsWith("/")||name.Split('/').Any(p=>p==""||p=="."||p==".."||p.TrimEnd(' ','.')!=p||BadSegment(p)))return false;return name.StartsWith("data/",StringComparison.Ordinal)||name.StartsWith("cn-exp/",StringComparison.Ordinal);}
        public static RestoreStage Prepare(string home,string input,BundleEnvelope envelope,Action<string> progress=null){
            string target=DataDirectory.Canonical(home);var stage=new RestoreStage{Home=target,Path=target+".stage-"+Guid.NewGuid().ToString("N"),Envelope=envelope};BackupTemporary.CleanupLegacy(target);Directory.CreateDirectory(stage.Path);string zip=stage.Path+".zip.tmp";FileStream zipStream=null;byte[] keys=Convert.FromBase64String(envelope.Keys);envelope.Keys=null;
            try{using(var file=File.OpenRead(input)){Verify(file,keys);byte[] packet=ReadKeyPacket(input);long start=12L+packet.Length;Array.Clear(packet,0,packet.Length);file.Position=start;var iv=new byte[16];if(file.Read(iv,0,16)!=16)throw new EndOfStreamException();long length=file.Length-file.Position-32;if(length<16||length%16!=0)throw new InvalidDataException();
                    using(var aes=Aes.Create()){aes.Key=keys.Take(32).ToArray();aes.IV=iv;using(var decrypt=new CryptoStream(zipStream=new FileStream(zip,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose),aes.CreateDecryptor(),CryptoStreamMode.Write,true)){byte[] buffer=new byte[65536];while(length>0){int read=file.Read(buffer,0,(int)Math.Min(buffer.Length,length));if(read<=0)throw new EndOfStreamException();decrypt.Write(buffer,0,read);length-=read;}decrypt.FlushFinalBlock();}}Array.Clear(iv,0,iv.Length);}
                zipStream.Position=0;using(var archive=new ZipArchive(zipStream,ZipArchiveMode.Read,true)){if(archive.Entries.Count>MaxEntries+1)throw new InvalidDataException();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var entry in archive.Entries)if(!names.Add(entry.FullName)||entry.FullName!="bundle-info.json"&&!ValidName(entry.FullName))throw new InvalidDataException("备份路径无效，原资料未改动。");
                    var metadata=archive.GetEntry("bundle-info.json");if(metadata==null||metadata.Length>16*1024*1024)throw new InvalidDataException();using(var reader=new StreamReader(metadata.Open()))stage.Manifest=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<BundleManifest>(reader.ReadToEnd());
                    if(stage.Manifest==null||stage.Manifest.Format!=1||stage.Manifest.Files==null||stage.Manifest.Files.Count!=archive.Entries.Count-1||stage.Manifest.Files.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=stage.Manifest.Files.Count)throw new InvalidDataException();long total=0;int count=0;
                    foreach(var item in stage.Manifest.Files){if(item==null||!ValidName(item.Name)||item.Hash==null||item.Hash.Length!=64||item.Length<0)throw new InvalidDataException();var entry=archive.GetEntry(item.Name);if(entry==null||entry.Length!=item.Length||(total+=item.Length)>MaxTotal)throw new InvalidDataException();string path=System.IO.Path.Combine(stage.Path,item.Name.Replace('/',System.IO.Path.DirectorySeparatorChar));if(!DataDirectory.Inside(stage.Path,path))throw new InvalidDataException();Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                        using(var source=entry.Open())using(var destination=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))CopyBounded(source,destination,item.Length);if(HashFile(path)!=item.Hash)throw new InvalidDataException();if(progress!=null&&++count%50==0)progress("核对备份 "+count+" / "+stage.Manifest.Files.Count);}
                }
                if(HashFile(System.IO.Path.Combine(stage.Path,"data","vault-v2.bin"))!=envelope.VaultHash)throw new InvalidDataException();return stage;
            }catch{stage.Dispose();throw;}finally{Array.Clear(keys,0,keys.Length);if(zipStream!=null)zipStream.Dispose();}
        }
        public static string Publish(RestoreStage stage,Func<bool> allowed=null,Action<string> idle=null){
            string home=DataDirectory.Canonical(stage.Home),staging=DataDirectory.Canonical(stage.Path);
            if(home==System.IO.Path.GetPathRoot(home).TrimEnd(System.IO.Path.DirectorySeparatorChar)||!staging.StartsWith(home+".stage-",StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetDirectoryName(home)!=System.IO.Path.GetDirectoryName(staging)||(File.GetAttributes(staging)&FileAttributes.ReparsePoint)!=0||!DataDirectory.HasVault(staging))throw new InvalidDataException("恢复暂存位置无效，未移动资料。");
            (idle??DataDirectory.ProfileIdle)(stage.Home);if(allowed!=null&&!allowed())throw new VaultLockedException();
            string previous=stage.Home+".previous-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);bool moved=false;
            File.WriteAllText(System.IO.Path.Combine(stage.Path,"directory-info.json"),new JavaScriptSerializer().Serialize(new DirectoryInfoRecord{Format=1,Source=previous,CreatedUtc=DateTime.UtcNow,Files=stage.Manifest.Files.Count}),new UTF8Encoding(false));
            try{if(Directory.Exists(stage.Home)){Directory.Move(stage.Home,previous);moved=true;}Directory.Move(stage.Path,stage.Home);stage.Published=true;return moved?previous:null;}
            catch{if(moved&&!Directory.Exists(stage.Home))Directory.Move(previous,stage.Home);throw;}
        }
    }
}
