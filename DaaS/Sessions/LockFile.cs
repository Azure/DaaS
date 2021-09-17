// -----------------------------------------------------------------------
// <copyright file="LockFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DaaS
{
    public class OperationLockInfo
    {
        public OperationLockInfo()
        {
            AcquiredDateTime = DateTime.UtcNow.ToString("o");
        }

        public string OperationName { get; set; }
        public string AcquiredDateTime { get; set; }
        public string StackTrace { get; set; }
        public string InstanceId { get; set; }
    }
    public interface IOperationLock
    {
        bool IsHeld { get; }
        OperationLockInfo LockInfo { get; }
        bool Lock(string operationName);

        // Waits until lock can be acquired after which the task completes.
        Task LockAsync(string operationName);
        void Release();
    }
    public class LockFile : IOperationLock
    {
        private const string NotEnoughSpaceText = "There is not enough space on the disk.";
        private readonly string _path;        

        // lock must be acquired without any error
        // default is false - meaning allow lock to be acquired during
        // file system readonly or disk full period.
        private readonly bool _ensureLock;

        private ConcurrentQueue<QueueItem> _lockRequestQueue;
        private FileSystemWatcher _lockFileWatcher;

        private Stream _lockStream;

        public LockFile(string path)
            : this(path,false)
        {
        }

        public LockFile(string path, bool ensureLock = false)
        {
            _path = Path.GetFullPath(path);            
            _ensureLock = ensureLock;
        }

        public OperationLockInfo LockInfo
        {
            get
            {
                if (IsHeld)
                {
                    var info = ReadLockInfo();
                    Logger.LogVerboseEvent(string.Format("Lock '{0}' is currently held by '{1}' operation started at {2}.", _path, info.OperationName, info.AcquiredDateTime));
                    return info;
                }

                // lock info represent no owner
                return new OperationLockInfo();
            }
        }

        public void InitializeAsyncLocks()
        {
            _lockRequestQueue = new ConcurrentQueue<QueueItem>();
            try
            {
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_path));
                // Set up lock file watcher. Note that depending on how the file is accessed the file watcher may generate multiple events.
                _lockFileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_path), Path.GetFileName(_path));
                _lockFileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
                _lockFileWatcher.Changed += OnLockReleasedInternal;
                _lockFileWatcher.Deleted += OnLockReleasedInternal;
                _lockFileWatcher.EnableRaisingEvents = true;
            }
            catch (UnauthorizedAccessException)
            {
                // If not authorized to create the locks directory, do nothing for now.
            }
        }

        /// <summary>
        /// Because of a bug in Ninject in how it disposes objects in the global scope for each request
        /// we can't use Dispose to shut down the file system watcher. Otherwise this would get disposed
        /// on every request.
        /// </summary>
        public void TerminateAsyncLocks()
        {
            if (_lockFileWatcher != null)
            {
                _lockRequestQueue = null;
                _lockFileWatcher.EnableRaisingEvents = false;
                _lockFileWatcher.Dispose();
                _lockFileWatcher = null;
            }
        }

        public bool IsHeld
        {
            get
            {
                // If there's no file then there's no process holding onto it
                if (!FileSystemHelpers.FileExists(_path))
                {
                    return false;
                }

                try
                {
                    // If there is a file, lets see if someone has an open handle to it, or if it's
                    // just hanging there for no reason
                    using (FileSystemHelpers.OpenFile(_path, FileMode.Open, FileAccess.Write, FileShare.Read)) { }
                }
                catch (UnauthorizedAccessException)
                {
                    // if it is ReadOnly file system, we will skip the lock
                    // which will enable all read action
                    // for write action, it will fail with UnauthorizedAccessException when perform actual write operation
                    //      There is one drawback, previously for write action, even acquire lock will fail with UnauthorizedAccessException,
                    //      there will be retry within given timeout. so if exception is temporary, previous`s implementation will still go thru.
                    //      While right now will end up failure. But it is a extreem edge case, should be ok to ignore.
                    return !FileSystemHelpers.IsFileSystemReadOnly();
                }
                catch (IOException ex)
                {
                    // if not enough disk space, no one has the lock.
                    // let the operation thru and fail where it would try to get the file
                    return !ex.Message.Contains(NotEnoughSpaceText);
                }
                catch (Exception ex)
                {
                    TraceIfUnknown(ex);
                    return true;
                }

                // cleanup inactive lock file.  technically, it is not needed
                // we just want to see the lock folder is clean, if no active lock.
                DeleteFileSafe();

                return false;
            }
        }

        public bool Lock(string operationName)
        {
            Stream lockStream = null;
            try
            {
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_path));

                lockStream = FileSystemHelpers.OpenFile(_path, FileMode.Create, FileAccess.Write, FileShare.Read);

                WriteLockInfo(operationName, lockStream);

                OnLockAcquired();

                _lockStream = lockStream;
                lockStream = null;

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                if (!_ensureLock)
                {
                    // if it is ReadOnly file system, we will skip the lock
                    // which will enable all read action
                    // for write action, it will fail with UnauthorizedAccessException when perform actual write operation
                    //      There is one drawback, previously for write action, even acquire lock will fail with UnauthorizedAccessException,
                    //      there will be retry within given timeout. so if exception is temporary, previous`s implementation will still go thru.
                    //      While right now will end up failure. But it is a extreem edge case, should be ok to ignore.
                    return FileSystemHelpers.IsFileSystemReadOnly();
                }
            }
            catch (IOException ex)
            {
                if (!_ensureLock)
                {
                    // if not enough disk space, no one has the lock.
                    // let the operation thru and fail where it would try to get the file
                    return ex.Message.Contains(NotEnoughSpaceText);
                }
            }
            catch (Exception ex)
            {
                TraceIfUnknown(ex);
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Close();
                }
            }

            return false;
        }

        protected virtual void OnLockAcquired()
        {
            // no-op
        }

        protected virtual void OnLockRelease()
        {
            // no-op
        }

        // we only write the lock info at lock's enter since
        // lock file will be cleaned up at release
        private static void WriteLockInfo(string operationName, Stream lockStream)
        {
            var json = JObject.FromObject(new OperationLockInfo
            {
                OperationName = operationName,
                StackTrace = System.Environment.StackTrace,
                InstanceId = InstanceIdUtility.GetShortInstanceId()
            });

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            lockStream.Write(bytes, 0, bytes.Length);
            lockStream.Flush();
        }

        private OperationLockInfo ReadLockInfo()
        {
            try
            {
                return JsonConvert.DeserializeObject<OperationLockInfo>(FileSystemHelpers.ReadAllTextFromFile(_path)) ?? new OperationLockInfo { OperationName = "unknown" };
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("ReadLockInfo failed to deserialize", ex);
                return new OperationLockInfo
                {
                    OperationName = "unknown",
                    StackTrace = ex.ToString()
                };
            };
        }

        /// <summary>
        /// Returns a lock right away or waits asynchronously until a lock is available.
        /// </summary>
        /// <returns>Task indicating the task of acquiring the lock.</returns>
        public Task LockAsync(string operationName)
        {
            if (_lockFileWatcher == null)
            {
                throw new InvalidOperationException("Must call Initialize before calling LockAsync!");
            }

            // See if we can get the lock -- if not then enqueue lock request.
            if (Lock(operationName))
            {
                return Task.FromResult(true);
            }

            QueueItem item = new QueueItem(operationName);
            _lockRequestQueue.Enqueue(item);
            return item.HasLock.Task;
        }

        public virtual void Release()
        {
            // Normally, this should never be null here, but currently some LiveScmEditorController code calls Release() incorrectly
            if (_lockStream == null)
            {
                OnLockRelease();
                return;
            }

            var temp = _lockStream;
            _lockStream = null;
            temp.Close();

            // cleanup inactive lock file.  technically, it is not needed
            // we just want to see the lock folder is clean, if no active lock.
            DeleteFileSafe();

            OnLockRelease();
        }

        // we cannot use FileSystemHelpers.DeleteFileSafe.
        // it does not handled IOException due to 'file in used'.
        private void DeleteFileSafe()
        {
            // Only clean up lock on Windows Env
            // When running on Mono with SMB share, delete action would cause wierd behavior on later OpenWrite action if a file has already been opened by another process
            try
            {
                FileSystemHelpers.DeleteFile(_path);
            }
            catch (Exception ex)
            {
                TraceIfUnknown(ex);
            }
        }
    

        private void TraceIfUnknown(Exception ex)
        {
            if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
            {
                // trace unexpected exception
                Logger.LogErrorEvent("Unknown error occurred", ex);
            }
        }

        /// <summary>
        /// When a lock file change has been detected we check whether there are queued up lock requests.
        /// If so then we attempt to get the lock and dequeue the next request.
        /// </summary>
        private void OnLockReleasedInternal(object sender, FileSystemEventArgs e)
        {
            if (!_lockRequestQueue.IsEmpty)
            {
                QueueItem item;
                if (_lockRequestQueue.TryPeek(out item) && Lock(item.OperationName))
                {
                    if (!_lockRequestQueue.IsEmpty)
                    {
                        if (!_lockRequestQueue.TryDequeue(out item))
                        {
                            string msg = String.Format("Got a lock but no lock request to dequeue -- releasing lock. Queue length is {0}.", _lockRequestQueue.Count);
                            Logger.LogErrorEvent("Failed while releasing lock", msg);
                            Release();
                        }

                        if (!item.HasLock.TrySetResult(true))
                        {
                            Logger.LogErrorEvent("Failed while releasing lock", "Async lock task is already in one of the three final states: RanToCompletion, Faulted, or Canceled -- releasing lock");
                            Release();
                        }
                    }
                    else
                    {
                        Release();
                    }
                }
            }
        }

        private class QueueItem
        {
            public QueueItem(string operationName)
            {
                OperationName = operationName;
                HasLock = new TaskCompletionSource<bool>();
            }

            public string OperationName { get; private set; }

            public TaskCompletionSource<bool> HasLock { get; private set; }
        }
    }
}
