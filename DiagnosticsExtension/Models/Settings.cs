using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DiagnosticsExtension.Models
{
    public class Settings
    {
        [DataMember]
        public string BlobSasUri;
        [DataMember]
        public string StorageConnectionString;
    }
}
