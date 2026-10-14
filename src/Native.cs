using System; using System.IO; using System.Text; using System.Collections.Generic; using System.ComponentModel; using System.Runtime.InteropServices;
namespace CnDirect {
    public static class Native {
        [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(uint rights,bool inherit,int pid);
        [DllImport("kernel32.dll",SetLastError=true)] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr address,byte[] bytes,int count,out IntPtr read);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr address,byte[] bytes,int count,out IntPtr wrote);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr address,UIntPtr size,uint type,uint protect);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr p,IntPtr address,UIntPtr size,uint protect,out uint old);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(IntPtr p,IntPtr address,UIntPtr size);
        public static IntPtr Ptr(uint n) { return new IntPtr(unchecked((int)n)); }
        public static uint Address(IntPtr p) { return unchecked((uint)p.ToInt32()); }
        public static IntPtr Add(IntPtr p,uint n) { return Ptr(checked(Address(p)+n)); }
        public static byte[] Read(IntPtr p,IntPtr a,int size) { if(size<0||size>131072)throw new InvalidDataException(); byte[] b=new byte[size]; IntPtr done; if(!ReadProcessMemory(p,a,b,size,out done)||done.ToInt32()!=size)throw new Win32Exception(); return b; }
        public static void Write(IntPtr p,IntPtr a,byte[] b) { IntPtr done; if(!WriteProcessMemory(p,a,b,b.Length,out done)||done.ToInt32()!=b.Length)throw new Win32Exception(); }
        public static IntPtr Alloc(IntPtr p,int size) { IntPtr a=VirtualAllocEx(p,IntPtr.Zero,(UIntPtr)size,0x3000,4);if(a==IntPtr.Zero)throw new Win32Exception();return a; }
        public static uint Protect(IntPtr p,IntPtr a,int size,uint flags) { uint old;if(!VirtualProtectEx(p,a,(UIntPtr)size,flags,out old))throw new Win32Exception();return old; }
        public static void Executable(IntPtr p,IntPtr a,int size) { Protect(p,a,size,0x20);if(!FlushInstructionCache(p,a,(UIntPtr)size))throw new Win32Exception(); }
        public static uint UInt(IntPtr p,IntPtr a) { return BitConverter.ToUInt32(Read(p,a,4),0); }
        public static string Ascii(IntPtr p,IntPtr a) { var b=new List<byte>();for(uint i=0;i<256;i++){byte c=Read(p,Add(a,i),1)[0];if(c==0)return Encoding.ASCII.GetString(b.ToArray());b.Add(c);}throw new InvalidDataException(); }
        public static void PointerWrite(IntPtr p,IntPtr a,uint expected,uint value) {
            if(UInt(p,a)!=expected)throw new InvalidOperationException("战网的网页调用已发生变化，已停止接管。");
            uint old=Protect(p,a,4,4);
            try { if(UInt(p,a)!=expected)throw new InvalidOperationException("网页调用已被其他组件更改，已停止。");Write(p,a,BitConverter.GetBytes(value)); }
            finally { Protect(p,a,4,old); }
        }
    }
    class Assembler {
        readonly List<byte> bytes=new List<byte>();readonly Dictionary<string,int> labels=new Dictionary<string,int>();readonly List<Tuple<int,string,bool>> fixes=new List<Tuple<int,string,bool>>();
        public void Emit(params byte[] values){bytes.AddRange(values);} public void Dword(uint n){Emit(BitConverter.GetBytes(n));}
        public void Label(string name){labels.Add(name,bytes.Count);}public void Addr(string name){fixes.Add(Tuple.Create(bytes.Count,name,true));Dword(0);}
        public void Jump(string name,params byte[] opcode){Emit(opcode);fixes.Add(Tuple.Create(bytes.Count,name,false));Dword(0);}
        public byte[] Finish(uint address){foreach(var f in fixes){uint value=f.Item3?checked(address+(uint)labels[f.Item2]):unchecked((uint)(labels[f.Item2]-f.Item1-4));byte[] n=BitConverter.GetBytes(value);for(int i=0;i<4;i++)bytes[f.Item1+i]=n[i];}return bytes.ToArray();}
    }
    public static class Stub {
        public const string Prefix="https://oauth.g.mkey.163.com/oauth-front/";
        public const string Alternate="https://account.battlenet.com.cn/login/";
        // x86 stdcall wrapper: captures one matching URL before the original browser
        // launch. All other calls retain the original arguments, registers and flags.
        public static byte[] Make(uint code,uint shared,uint original,bool extended,string prefix,string alternate) {
            if(prefix.Length==0||prefix.Length>200)throw new InvalidDataException(); var a=new Assembler();
            a.Emit(0x9c,0x60); // pushfd; pushad
            if(extended){a.Emit(0x8b,0x44,0x24,0x28,0x85,0xc0);a.Jump("pass",0x0f,0x84);a.Emit(0x83,0x38,0x3c);a.Jump("pass",0x0f,0x82);a.Emit(0x8b,0x70,0x10);}
            else a.Emit(0x8b,0x74,0x24,0x30); // original lpFile at esp+12+36
            a.Emit(0xf0,0xff,0x05);a.Dword(checked(shared+65544));
            a.Emit(0x85,0xf6);a.Jump("pass",0x0f,0x84);
            a.Emit(0x89,0xf5,0xbf);a.Addr("prefix");a.Emit(0xb9);a.Dword((uint)prefix.Length);
            // Do not read beyond a short string's terminator while comparing prefix.
            a.Label("compare");a.Emit(0x66,0x8b,0x06,0x66,0x3b,0x07);a.Jump(alternate==null?"pass":"second",0x0f,0x85);a.Emit(0x83,0xc6,0x02,0x83,0xc7,0x02,0x49);a.Jump("compare",0x0f,0x85);
            if(alternate!=null){a.Jump("matched",0xe9);a.Label("second");a.Emit(0x89,0xee,0xbf);a.Addr("alternate");a.Emit(0xb9);a.Dword((uint)alternate.Length);a.Label("compare2");a.Emit(0x66,0x8b,0x06,0x66,0x3b,0x07);a.Jump("pass",0x0f,0x85);a.Emit(0x83,0xc6,0x02,0x83,0xc7,0x02,0x49);a.Jump("compare2",0x0f,0x85);}
            a.Label("matched");
            a.Emit(0x31,0xc9);a.Label("length");a.Emit(0x81,0xf9);a.Dword(32767);a.Jump("pass",0x0f,0x83);
            a.Emit(0x66,0x83,0x7c,0x4d,0x00,0x00);a.Jump("ready",0x0f,0x84);a.Emit(0x41);a.Jump("length",0xe9);
            a.Label("ready");a.Emit(0x89,0xcb,0x31,0xc0,0xba);a.Dword(0xffffffff);a.Emit(0xf0,0x0f,0xb1,0x15);a.Dword(shared);a.Jump("pass",0x0f,0x85);
            a.Emit(0x89,0xee,0xbf);a.Dword(checked(shared+8));a.Emit(0x41,0xfc,0xf3,0x66,0xa5);
            a.Emit(0x89,0x1d);a.Dword(checked(shared+4));a.Emit(0xc7,0x05);a.Dword(shared);a.Dword(1);
            if(extended){a.Emit(0x8b,0x54,0x24,0x28,0xc7,0x42,0x20);a.Dword(33);a.Emit(0xc7,0x42,0x38);a.Dword(0);}
            a.Emit(0x61,0x9d,0xb8);a.Dword(extended?1u:33u);a.Emit(0xc2,extended?(byte)4:(byte)24,0);
            a.Label("pass");a.Emit(0x61,0x9d,0xff,0x25);a.Addr("original");
            a.Label("original");a.Dword(original);a.Label("prefix");a.Emit(Encoding.Unicode.GetBytes(prefix+"\0"));if(alternate!=null){a.Label("alternate");a.Emit(Encoding.Unicode.GetBytes(alternate+"\0"));}return a.Finish(code);
        }
    }
    public class ImportSlot { public IntPtr Address;public uint Original;public string Function;public string Module; }
    public static class Imports {
        public static List<ImportSlot> Find(IntPtr process,IntPtr module,string name) {
            var result=new List<ImportSlot>();var dos=Native.Read(process,module,64);
            if(dos[0]!=0x4d||dos[1]!=0x5a)throw new InvalidDataException();uint offset=BitConverter.ToUInt32(dos,60);if(offset>8192)throw new InvalidDataException();
            var pe=Native.Read(process,Native.Add(module,offset),160);
            if(BitConverter.ToUInt32(pe,0)!=0x4550||BitConverter.ToUInt16(pe,4)!=0x14c||BitConverter.ToUInt16(pe,24)!=0x10b)throw new InvalidOperationException("这个战网版本不适用于当前验证版。");
            uint imageSize=BitConverter.ToUInt32(pe,24+56), directory=BitConverter.ToUInt32(pe,24+104);if(directory==0)return result;if(directory>=imageSize)throw new InvalidDataException();
            Func<uint,int,byte[]> read=(rva,size)=>{if((ulong)rva+(uint)size>imageSize)throw new InvalidDataException();return Native.Read(process,Native.Add(module,rva),size);};
            for(uint n=0;n<1024;n++) {
                var d=read(checked(directory+n*20),20);uint lookup=BitConverter.ToUInt32(d,0), lib=BitConverter.ToUInt32(d,12), first=BitConverter.ToUInt32(d,16);
                if(lib==0&&first==0)break;if(lookup==0)continue;
                for(uint i=0;i<8192;i++) {
                    uint value=BitConverter.ToUInt32(read(checked(lookup+i*4),4),0);if(value==0)break;if((value&0x80000000)!=0)continue;
                    if(value>=imageSize)throw new InvalidDataException();string function=Native.Ascii(process,Native.Add(module,checked(value+2)));
                    if(function!="ShellExecuteW"&&function!="ShellExecuteExW")continue;
                    uint original=BitConverter.ToUInt32(read(checked(first+i*4),4),0);if(original==0)throw new InvalidDataException();
                    result.Add(new ImportSlot{Address=Native.Add(module,checked(first+i*4)),Original=original,Function=function,Module=name});
                }
            }
            return result;
        }
    }
}
