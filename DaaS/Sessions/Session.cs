using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.HeartBeats;
using DaaS.Leases;
using DaaS.Storage;
using File = DaaS.Storage.File;

namespace DaaS.Sessions
{
    
    public enum SessionStatus
    {
        Active,
        CollectedLogsOnly,
        Cancelled,
        Error,
        Complete
    }

    public sealed class Session : Storage.File
    {
        public string SiteName { get; private set; }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public SessionId SessionId { get; private set; }
        public string Description { get; internal set; }
        public List<Instance> InstancesSpecified { get; private set; }
        public new string BlobSasUri { get; internal set; }

        #region Status Checking

        public SessionStatus Status
        {
            get
            {
                foreach (SessionStatus statusType in (SessionStatus[]) Enum.GetValues(typeof (SessionStatus)))
                {
                    if (!_sessionStateCheckers.ContainsKey(statusType))
                    {
                        throw new Exception("No state checker setup for status " + statusType);
                    }

                    if (_sessionStateCheckers[statusType](_diagnoserSessions))
                    {
                        return statusType;
                    }
                }

                throw new InvalidDataException("Encountered invalid session state");
            }
        }

        readonly Dictionary<SessionStatus, Func<List<DiagnoserSession>, bool>> _sessionStateCheckers = new Dictionary<SessionStatus, Func<List<DiagnoserSession>, bool>>()
        {
            {SessionStatus.Active, IsActiveState},
            {SessionStatus.CollectedLogsOnly, IsCollectedLogsOnlyState},
            {SessionStatus.Complete, IsCompleteState},
            {SessionStatus.Cancelled, IsCancelledState},
            {SessionStatus.Error, IsErrorState}
        }; 

        private static bool IsActiveState(List<DiagnoserSession> diagnoserSessions)
        {
            if (diagnoserSessions == null || !diagnoserSessions.Any())
            {
                // Still setting up the diagnoser sessions
                return true;
            }

            if (IsErrorState(diagnoserSessions) || IsCancelledState(diagnoserSessions))
            {
                return false;
            }

            return diagnoserSessions.Any(diagnoserSession => 
                diagnoserSession.CollectorStatus == DiagnosisStatus.InProgress || 
                diagnoserSession.AnalyzerStatus == DiagnosisStatus.InProgress || 
                (diagnoserSession.AnalyzerStatus == DiagnosisStatus.WaitingForInputs && diagnoserSession.CollectorStatus != DiagnosisStatus.Error));
        }

        private static bool IsCompleteState(List<DiagnoserSession> diagnoserSessions)
        {
            return diagnoserSessions.All(diagnoserSession => diagnoserSession.IsComplete());
        }

        private static bool IsCollectedLogsOnlyState(List<DiagnoserSession> diagnoserSessions)
        {
            return !IsCompleteState(diagnoserSessions) && diagnoserSessions.All(diagnoserSession => diagnoserSession.IsCollectedLogsOnlyState() || diagnoserSession.IsComplete());
        }

        private static bool IsCancelledState(List<DiagnoserSession> diagnoserSessions)
        {
            return diagnoserSessions.Any(diagnoserSession => diagnoserSession.IsCancelled());
        }

        private static bool IsErrorState(List<DiagnoserSession> diagnoserSessions)
        {
            return !IsCancelledState(diagnoserSessions) && diagnoserSessions.Any(diagnoserSession => diagnoserSession.IsErrorState());
        }

        #endregion

        public override string FileName
        {
            get
            {
                return SessionId + SessionConstants.SessionFileExtension;
            }
        }

        protected internal override StorageLocation StorageLocation
        {
            get
            {
                return GetSessionStorageLocation();
            }
        }

        internal static StorageLocation GetSessionStorageLocation()
        {
            return StorageLocation.UserSiteData;
        }
        
        private readonly List<DiagnoserSession> _diagnoserSessions = new List<DiagnoserSession>();

        internal Session(
            List<Diagnoser> diagnosers, 
            DateTime startTime, 
            DateTime endTime, 
            SessionType sessionType,
            List<Instance> instancesToRun = null)
        {
            SessionId = new SessionId(DateTime.UtcNow.ToString(SessionConstants.SessionFileNameFormat));
            StartTime = startTime;
            EndTime = endTime;
            InstancesSpecified = instancesToRun;
            SiteName = Infrastructure.Settings.SiteName;
            RelativePath = GetDesiredRelativePath();

            if (diagnosers == null || diagnosers.Count == 0)
            {
                throw new ArgumentException("No diagnosers have been specified for the session");
            }

            foreach (var diagnoser in diagnosers)
            {
                _diagnoserSessions.Add(new DiagnoserSession(diagnoser, sessionType));
            }
        }

        internal Session(Stream sessionContent, string relativeFilePath)
        {
            LoadFileFromStream(sessionContent);
            this.RelativePath = relativeFilePath;
        }

        private Session() { }

        private string GetDesiredRelativePath()
        {
            return Path.Combine(Paths.GetRelativeSessionDir(Status), FileName);
        }
        
        internal override async Task<Stream> GetFileStreamAsync()
        {
            return await Task.Run(() =>
            {
                return GetFileStream();
            });
        }

        internal bool ShouldCollectLogsOnInstance(Instance instance)
        {
            if (InstancesSpecified != null && InstancesSpecified.Count != 0)
            {
                Logger.LogDiagnostic(
                    InstancesSpecified.Contains(instance)
                        ? "Yes we need to collect logs on instance {0}"
                        : "No we don't need to collect logs on instance {0}", instance.Name);

                bool instanceStillValid = HeartBeatController.GetLiveInstances().Any(x => x.Name == instance.Name);

                Logger.LogDiagnostic("Is the instance still a valid instance ? {0}", instanceStillValid);

                return instanceStillValid && InstancesSpecified.Contains(instance);
            }

            Logger.LogDiagnostic("Yes we need to collect logs on instance {0}", instance.Name);

            return true;
        }

        internal override Stream GetFileStream()
        {
            var diagnoserSessionsXml = new XElement(SessionXml.Diagnosers);

            foreach (var diagnoserSession in _diagnoserSessions)
            {
                diagnoserSessionsXml.Add(diagnoserSession.GetXml());
            }

            XElement instancesXml = null;
            if (InstancesSpecified != null && InstancesSpecified.Count > 0)
            {
                instancesXml = new XElement(SessionXml.Instances);
                var instances = new StringBuilder();
                foreach (var instance in InstancesSpecified)
                {
                    instances.AppendFormat("{0},", instance.Name);
                }
                instances.Remove(instances.Length - 1, 1);
                instancesXml.Add(instances);
            }

            var xDoc = new XDocument(
                new XElement(SessionXml.Session,
                    new XElement(SessionXml.SessionId, SessionId),
                    new XElement(SessionXml.SiteName, SiteName),
                    new XElement(SessionXml.TimeRange,
                        new XElement(SessionXml.StartTime, StartTime.ToUniversalTime().ToString(SessionXml.TimeFormat)),
                        new XElement(SessionXml.EndTime, EndTime.ToUniversalTime().ToString(SessionXml.TimeFormat))
                        ),
                    new XElement(SessionXml.Status, Status),
                    new XElement(SessionXml.Description, Description),
                    new XElement(SessionXml.BlobSasUri, BlobSasUri),
                    instancesXml,
                    diagnoserSessionsXml
                    )
                );

            Stream fileStream = new MemoryStream();
            xDoc.Save(fileStream);
            fileStream.Position = 0;

            return fileStream;
        }

        private void LoadFileFromStream(Stream fileContents)
        {
            var sessionDoc = XDocument.Load(fileContents);
            var sessionXml = sessionDoc.Element(SessionXml.Session);
            // TODO: verify xml syntax

            var timeRangeXml = sessionXml.Element(SessionXml.TimeRange);
            StartTime = DateTime.Parse(timeRangeXml.Element(SessionXml.StartTime).Value).ToUniversalTime();
            EndTime = DateTime.Parse(timeRangeXml.Element(SessionXml.EndTime).Value).ToUniversalTime();
            SiteName = sessionXml.Element(SessionXml.SiteName).Value;
            SessionId = new SessionId(sessionXml.Element(SessionXml.SessionId).Value);

            var descriptionXml = sessionXml.Element(SessionXml.Description);
            if (descriptionXml != null)
            {
                Description = descriptionXml.Value;
            }

            var blobSasUriXml = sessionXml.Element(SessionXml.BlobSasUri);
            if (blobSasUriXml != null)
            {
                BlobSasUri = blobSasUriXml.Value;
            }

            var instancesXml = sessionXml.Element(SessionXml.Instances);
            if (instancesXml != null)
            {
                InstancesSpecified = new List<Instance>();
                var instanceNames = instancesXml.Value.Split(',');
                foreach (var instanceName in instanceNames)
                {
                    if (!string.IsNullOrWhiteSpace(instanceName))
                    {
                        InstancesSpecified.Add(new Instance(instanceName));
                    }
                }
            }

            var diagnosersXml = sessionXml.Element(SessionXml.Diagnosers);
            foreach (var diagnoserSessionXml in diagnosersXml.Elements())
            {
                _diagnoserSessions.Add(new DiagnoserSession(diagnoserSessionXml));
            }

            RelativePath = GetDesiredRelativePath();
        }

        public void AnalyzeLogs()
        {
            var lockFile = AcquireSessionLock("AnalyzeLogs");
            try
            {
                LoadLatestUpdates(false);
                foreach (var diagnoserSession in _diagnoserSessions)
                {
                    if (diagnoserSession.AnalyzerStatus == DiagnosisStatus.NotRequested)
                    {
                        diagnoserSession.AnalyzerStatus = DiagnosisStatus.InProgress;
                        diagnoserSession.AnalyzerStartTime = DateTime.UtcNow;
                    }
                }

                SaveUpdatesAsync(waitForLease: true).Wait();
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed in AnalyzeLogs", ex, SessionId.ToString());
            }

            if (lockFile != null)
            {
                lockFile.Release();
            }
            
        }        

        public Task CancelSessionAsync()
        {
            LoadLatestUpdates(false);

            Logger.LogSessionVerboseEvent($"Cancelling Session - {SessionId}. Session cancelled after { DateTime.UtcNow.Subtract(StartTime).TotalMinutes } minutes", SessionId.ToString());

            List<Instance> InstancesToClean = new List<Instance>();
            if (InstancesSpecified != null && InstancesSpecified.Count != 0)
            {
                InstancesToClean = InstancesSpecified;
            }
            else
            {
                InstancesToClean = HeartBeatController.GetLiveInstances().ToList();
            }

            foreach (var diagnoserSession in _diagnoserSessions)
            {
                if (diagnoserSession.AnalyzerStatus == DiagnosisStatus.Cancelled || diagnoserSession.CollectorStatus == DiagnosisStatus.Cancelled)
                {
                    continue;
                }

                // Cancel anything that's not already complete
                diagnoserSession.CollectorStatus = (DiagnosisStatus) Math.Max((int) diagnoserSession.CollectorStatus, (int) DiagnosisStatus.Cancelled);
                diagnoserSession.AnalyzerStatus  = (DiagnosisStatus) Math.Max((int) diagnoserSession.AnalyzerStatus,  (int) DiagnosisStatus.Cancelled);

                foreach (var instance in InstancesToClean)
                {
                    CancelledInstance cancelled = new CancelledInstance
                    {
                        ProcessCleanupOnCancel = diagnoserSession.Diagnoser.ProcessCleanupOnCancel,
                        CancellationTime = DateTime.UtcNow,
                        DiagnoserName = diagnoserSession.Diagnoser.Name.Replace(" ", ""),
                        Name = instance.Name,
                        SessionId = SessionId.ToString()
                    };

                    var filePath = Path.Combine(Settings.CancelledDir, $"{cancelled.Name}.{cancelled.DiagnoserName}");

                    Logger.LogSessionVerboseEvent($"Creating file {filePath}", SessionId.ToString());

                    Infrastructure.Storage.SaveFile(cancelled.GetXmlStream(), filePath, StorageLocation.UserSiteData);
                }
            }            

            // Lets do this without a lock. If by any chance our 
            // locking logic has an issue, at-least we should cancel 
            // session properly
            return SaveUpdatesAsync(waitForLease: true);
        }

        internal void MoveSessionToCorrectStorageFolderBasedOnStatus()
        {
            var sessionStatus = GetDesiredRelativePath();
            Infrastructure.Storage.MoveFile(
                this,
                sessionStatus,
                GetSessionStorageLocation());

            if (!sessionStatus.Contains(SessionDirectories.ActiveSessionsDir))
            {
                var totalMinutes = DateTime.UtcNow.Subtract(StartTime).TotalMinutes;
                Logger.LogSessionVerboseEvent($"Session {SessionId} is marked {Status.ToString()} after {totalMinutes.ToString("0.00")} min", SessionId.ToString());
            }

        }

        public IEnumerable<DiagnoserSession> GetDiagnoserSessions()
        {
            return _diagnoserSessions;
        }

        private LockFile AcquireSessionLock(string methodName ="")
        {
            string lockFilePath = this.FullPermanentStoragePath + ".lock";
            LockFile _lockFile = new LockFile(lockFilePath);
            int loopCount = 0;
            int lognum = 1;
            int maximumWaitTimeInSeconds = 15 * 60;

            while(!_lockFile.Lock($"AcquireSessionLock by {methodName} on {Environment.MachineName}") && loopCount <= maximumWaitTimeInSeconds)
            {
                ++loopCount;
                if (loopCount > lognum * 120)
                {
                    ++lognum;
                    Logger.LogSessionVerboseEvent($"Waiting to acquire the lock on session file , loop {lognum}", SessionId.ToString());
                }                
                Thread.Sleep(1000);
            }
            if (loopCount == maximumWaitTimeInSeconds)
            {
                Logger.LogSessionVerboseEvent($"Deleting the lock file as it seems to be in an orphaned stage", SessionId.ToString());
                _lockFile.Release();
                return null;
            }
            return _lockFile;
        }

        public async Task SaveAndMergeUpdatesAsync(bool waitForLease = false)
        {
            var lockFile = AcquireSessionLock("SaveAndMergeUpdatesAsync");
            try
            {
                LoadLatestUpdates(false);
                await SaveUpdatesAsync(waitForLease);
            }
            catch(Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed in SaveAndMergeUpdatesAsync", ex, SessionId.ToString());
            }
            
            if (lockFile !=null)
            {
                lockFile.Release();
            }
           
            
        }

        private async Task SaveUpdatesAsync(bool waitForLease)
        {
            Logger.LogDiagnostic("Time to save a new update for session {0}", SessionId);
            // Grab a lease on the session
            Lease sessionLease;
            if (waitForLease)
            {
                sessionLease = Infrastructure.LeaseManager.GetLease(RelativePath, string.Empty);
            }
            else
            {
                sessionLease = Infrastructure.LeaseManager.TryGetLease(RelativePath, string.Empty);
            }

            Logger.LogDiagnostic("Do I have a lease?");
            if (!Lease.IsValid(sessionLease))
            {
                Console.WriteLine("Nope. Darn it");
                return;
            }
            Logger.LogDiagnostic("I do! Sweet!");

            int retryCount = 0;
            retryLabel:

            if (retryCount > 2)
            {
                goto Finished;
            }

            try
            {
                Logger.LogSessionVerboseEvent($"About to save the new session with retryCount = {retryCount} and SessionId = {SessionId.ToString()}", SessionId.ToString());
                // Save the session
                Save(sessionLease);
                Logger.LogSessionVerboseEvent($"Session {SessionId.ToString()} saved successfully", SessionId.ToString());
            }
            catch (IOException ioException)
            {
                if (ioException.Message.StartsWith("The process cannot access the file", StringComparison.InvariantCultureIgnoreCase))
                {
                    Logger.LogSessionErrorEvent($"The Session is in use. Waiting for 5 seconds and retrying", ioException, SessionId.ToString());
                    await Task.Delay(5000);
                    ++retryCount;
                    LoadLatestUpdates(false);
                    goto retryLabel;
                }
            }
            catch (Exception e)
            {
                Logger.LogSessionErrorEvent("Couldn't save the updated session", e, SessionId.ToString());
            }

            try
            {
                MoveSessionToCorrectStorageFolderBasedOnStatus();
                Logger.LogDiagnostic("Saved the session");
            }
            catch (Exception e)
            {
                Logger.LogSessionErrorEvent("Couldn't move session to correct folders", e, SessionId.ToString());
            }

            Finished:
            sessionLease.Release();
        }

        public void LoadLatestUpdates(bool checkForLocks = true)
        {
            Logger.LogDiagnostic("Time to load updates for session {0}", SessionId);
            
            if (checkForLocks)
            {
                int loopCount = 0;
                int lognum = 1;
                int maximumWaitTimeInSeconds = 15 * 60;

                while (Infrastructure.Storage.FileExists(this.FullPermanentStoragePath + ".lock", StorageLocation.UserSiteData, string.Empty) && loopCount <= maximumWaitTimeInSeconds)
                {
                    ++loopCount;
                    if (loopCount > lognum * 300)
                    {
                        ++lognum;
                        Logger.LogSessionVerboseEvent($"Waiting to someone to delete the session lock file , loop {lognum}", SessionId.ToString());
                    }
                    Thread.Sleep(1000);
                    loopCount++;
                }
                if (loopCount == maximumWaitTimeInSeconds)
                {
                    Logger.LogSessionVerboseEvent($"Giving up waiting to delete the session lock file", SessionId.ToString());
                }
            }

            Session loadedSession = null;
            try
            {
                // Load the most up to date copy of the session from storage
                loadedSession = (new SessionController()).GetSessionWithId(this.SessionId);
                Logger.LogDiagnostic("Re-loaded session {0}", SessionId);
            }
            catch (Exception e)
            {
                Logger.LogSessionErrorEvent($"Couldn't re-read the session", e, SessionId.ToString());
                return;
            }

            // Apply all changes from the updated session to our copy of the session
            foreach (var loadedDiagnoserSession in loadedSession._diagnoserSessions)
            {
                var diagnoserSession =
                    _diagnoserSessions.First(d => d.Diagnoser.Name == loadedDiagnoserSession.Diagnoser.Name);

                diagnoserSession.MergeUpdates(loadedDiagnoserSession);
            }
        }
    }
}
