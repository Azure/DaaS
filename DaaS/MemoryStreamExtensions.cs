//-----------------------------------------------------------------------
// <copyright file="MemoryStreamExtensions.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS
{
    public static class MemoryStreamExtensions
    {
        public static string ReadToEnd(this MemoryStream baseStream)
        {
            baseStream.Position = 0;
            StreamReader R = new StreamReader(baseStream);
            return R.ReadToEnd();
        }

        public static async Task CopyStreamAsync(Stream from, Stream to, CancellationToken cancellationToken, bool closeAfterCopy = false)
        {
            try
            {
                byte[] bytes = new byte[1024];
                int read = 0;
                while ((read = await from.ReadAsync(bytes, 0, bytes.Length, cancellationToken)) != 0)
                {
                    await to.WriteAsync(bytes, 0, read, cancellationToken);
                }

            }
            finally
            {
                // this is needed specifically for input stream
                // in order to tell executable that the input is done
                if (closeAfterCopy)
                {
                    to.Close();
                }
            }
        }

    }
}
