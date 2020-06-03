using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaaS.Storage;

namespace DaaS.HeartBeats
{
    static class HeartBeatConstants
    {
        public const StorageLocation StorageLoc = StorageLocation.UserSiteData;
        public const string HeartBeatFolder = "Heartbeats";
    }

    public static class HeartBeatController
    {
        private static int _numberOfLiveInstancesCache = 1;
        private static DateTime _lastNumberOfLiveInstancesCheck = DateTime.MinValue;
        private static readonly TimeSpan CacheLifeSpan = new TimeSpan(0, 0, 5);

        public static void SendHeartBeat()
        {
            try
            {
                var heartBeat = HeartBeat.GetNewHeartBeat();
                heartBeat.Send();
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not send heartbeat. " + e.Message);
            }
        }

        public static void DeleteExpiredHeartBeats()
        {
            try
            {
                foreach (var heartBeat in GetHeartBeats().Where(heartBeat => !heartBeat.IsAlive()))
                {
                    Console.WriteLine(
                            "Deleting expired heartbeat from instance {0}. It last beat at {1} while the current time is {2}",
                            heartBeat.Instance,
                            heartBeat.ExpirationTime,
                            DateTime.UtcNow);
                    heartBeat.Delete();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("Couldn't delete heartbeat. Message: {0}\n{1}", e.Message, e.StackTrace));
            }
        }

        public static int GetNumberOfLiveInstances()
        {
            if (_lastNumberOfLiveInstancesCheck < DateTime.Now - CacheLifeSpan)
            {
                _numberOfLiveInstancesCache = GetHeartBeats().Count(heartBeat => heartBeat.IsAlive());
                // Ensure we always include ourselves even if we haven't yet sent out a heartbeat
                _numberOfLiveInstancesCache = Math.Max(_numberOfLiveInstancesCache, 1);
                _lastNumberOfLiveInstancesCheck = DateTime.Now;
            }

            return _numberOfLiveInstancesCache;
        }

        public static IEnumerable<Instance> GetLiveInstances()
        {
            List<Instance> instances = GetHeartBeats().Where(heartBeat => heartBeat.IsAlive()).Select(heartBeat => heartBeat.Instance).ToList();
            if (instances.Count == 0)
            {
                // Ensure we always include ourselves even if we haven't yet sent out a heartbeat
                instances.Add(Instance.GetCurrentInstance());
            }

            return instances;
        }

        private static IEnumerable<HeartBeat> GetHeartBeats()
        {
            var heartBeatFiles = Infrastructure.Storage.GetFilesInDirectory(HeartBeatConstants.HeartBeatFolder, HeartBeatConstants.StorageLoc, string.Empty);
            if (heartBeatFiles != null)
            {
                foreach (string heartBeatPath in heartBeatFiles)
                {
                    HeartBeat heartBeat = HeartBeat.OpenHeartBeat(heartBeatPath);
                    if (heartBeat != null)
                    {
                        yield return heartBeat;
                    }
                }
            }
        }
    }

    [Serializable]
    public class HeartBeat
    {
        public Instance Instance { get; set; }

        public DateTime ExpirationTime { get; set; }

        public bool IsAlive()
        {
            return ExpirationTime > DateTime.UtcNow;
        }

        public static HeartBeat GetNewHeartBeat()
        {
            HeartBeat heartBeat = GenerateHeartBeat(Instance.GetCurrentInstance());
            return heartBeat;
        }

        private static HeartBeat GenerateHeartBeat(Instance instance)
        {
            HeartBeat heartBeat = new HeartBeat()
            {
                ExpirationTime = NewHeartBeatExpirationTime(),
                Instance = instance
            };

            return heartBeat;
        }

        private static DateTime NewHeartBeatExpirationTime()
        {
            return DateTime.UtcNow + Infrastructure.Settings.HeartBeatLifeTime;
        }

        public void Send()
        {
            CreateHeartBeatDirectoryIfNotExists();
            var heartBeatPath = Path.Combine(Infrastructure.Settings.GetRootStoragePathForLocation(HeartBeatConstants.StorageLoc), GetHeartBeatRelativePath());

            RetryHelper.RetryOnException("Sending heartbeat...", () =>
            {
                this.ToJsonFile(heartBeatPath);
            }, TimeSpan.FromMilliseconds(100), 3, false);
        }


        private void CreateHeartBeatDirectoryIfNotExists()
        {
            var heartBeatFolder = Path.Combine(Infrastructure.Settings.GetRootStoragePathForLocation(HeartBeatConstants.StorageLoc), HeartBeatConstants.HeartBeatFolder);
            RetryHelper.RetryOnException("Creating heartbeat folder...", () =>
            {
                FileSystemHelpers.CreateDirectoryIfNotExists(heartBeatFolder);
            }, TimeSpan.FromMilliseconds(100), 3, false);
        }

        public string GetHeartBeatRelativePath()
        {
            return Path.Combine(HeartBeatConstants.HeartBeatFolder, Instance.Name);
        }

        public static HeartBeat OpenHeartBeat(string heartBeatPath)
        {

            try
            {
                HeartBeat heartBeat = FileSystemHelpers.FromJsonFile<HeartBeat>(heartBeatPath);
                return heartBeat;
            }
            catch (Exception)
            {
                // possibly a stale heartbeat file which is either empty or in XML format
                RetryHelper.RetryOnException("Deleting corrupt heartbeat...", () =>
                {
                    System.IO.File.Delete(heartBeatPath);
                }, TimeSpan.FromSeconds(1), 3, false);
                return null;
            }
        }

        public void Delete()
        {
            var heartBeatPath = Path.Combine(Infrastructure.Settings.GetRootStoragePathForLocation(HeartBeatConstants.StorageLoc), GetHeartBeatRelativePath());

            RetryHelper.RetryOnException("Deleting heartbeat...", () =>
            {
                System.IO.File.Delete(heartBeatPath);
            }, TimeSpan.FromSeconds(1), 3, false);
        }
    }
}
