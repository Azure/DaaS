using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DaaS
{
    public static class ZipStreamContent
    {
        public static PushStreamContent Create(string fileName, Action<ZipArchive> onZip)
        {
            var content = new PushStreamContent((outputStream, httpContent, transportContext) =>
            {
                using (var zip =
                new ZipArchive(new StreamWrapper(outputStream), ZipArchiveMode.Create, leaveOpen: false))
                {
                    onZip(zip);
                }
            });
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
            content.Headers.ContentDisposition.FileName = fileName;
            return content;
        }

        // this wraps the read-only HttpResponseStream to support ZipArchive Position getter.
        public class StreamWrapper : DelegatingStream
        {
            private long _position = 0;

            public StreamWrapper(Stream stream)
                : base(stream)
            {
            }

            public override long Position
            {
                get { return _position; }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _position += count;
                base.Write(buffer, offset, count);
            }

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                _position += count;
                return base.BeginWrite(buffer, offset, count, callback, state);
            }
        }
    }

}
