using System;using System.IO;
namespace BrowserManager {
    // .NET Framework ZIP writer reads Position even on non-seekable output.
    // Count plaintext bytes without buffering them or seeking the encryption stream.
    internal sealed class PositionWriteStream:Stream {
        readonly Stream output;long position;
        internal PositionWriteStream(Stream output){this.output=output;}
        public override bool CanRead{get{return false;}}
        public override bool CanSeek{get{return false;}}
        public override bool CanWrite{get{return true;}}
        public override long Length{get{return position;}}
        public override long Position{get{return position;}set{throw new NotSupportedException();}}
        public override void Write(byte[] buffer,int offset,int count){output.Write(buffer,offset,count);position+=count;}
        public override void Flush(){output.Flush();}
        public override int Read(byte[] buffer,int offset,int count){throw new NotSupportedException();}
        public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}
        public override void SetLength(long value){throw new NotSupportedException();}
        // The caller owns and finalizes the encryption stream.
    }
}
