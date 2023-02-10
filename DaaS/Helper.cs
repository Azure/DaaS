// -----------------------------------------------------------------------
// <copyright file="Helper.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;

namespace DaaS
{
    public static class Helper
    {
        public static string ConvertForwardSlashesToBackSlashes(this string path)
        {
            return path.Replace('/', '\\');
        }
        public static string ConvertBackSlashesToForwardSlashes(this string path)
        {
            return path.Replace('\\', '/');
        }

        internal static Stream GetXmlStream(this Object obj)
        {
            var x = new System.Xml.Serialization.XmlSerializer(obj.GetType());
            var objStream = new MemoryStream();
            x.Serialize(objStream, obj);
            objStream.Position = 0;
            return objStream;
        }

        internal static T LoadFromXmlStream<T>(this Stream xmlStream)
        {
            var x = new System.Xml.Serialization.XmlSerializer(typeof(T));
            var obj = (T)x.Deserialize(xmlStream);
            xmlStream.Dispose();
            return obj;
        }
    }
}
