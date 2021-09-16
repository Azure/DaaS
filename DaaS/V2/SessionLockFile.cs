// -----------------------------------------------------------------------
// <copyright file="SessionLockFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace DaaS.V2
{
    internal class SessionLockFile : LockFile
    {
        readonly string _lockFilePath;
        public SessionLockFile(string path, bool ensureLock = false) : base(path, ensureLock)
        {
            _lockFilePath = path;
        }

        public override void Release()
        {
            base.Release();
            FileSystemHelpers.DeleteFileSafe(_lockFilePath);
        }
    }
}
