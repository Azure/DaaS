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
        public string BlobContainer;
        [DataMember]
        public string BlobKey;
        [DataMember]
        public string BlobAccount;
        [DataMember]
        public string EndpointSuffix;
    }
}
