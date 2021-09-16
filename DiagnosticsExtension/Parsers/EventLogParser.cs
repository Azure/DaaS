// -----------------------------------------------------------------------
// <copyright file="EventLogParser.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;

namespace DiagnosticsExtension
{
    class EventResourceType
    {
        public string SourceStartsWith { get; set; }
        public string ResourceFile { get; set; }
        public IntPtr PtrHandle { get; set; }
    }
    public class EventLogParser
    {
        public const int FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100;
        public const int FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;
        public const int FORMAT_MESSAGE_FROM_STRING = 0x00000400;
        public const int FORMAT_MESSAGE_FROM_HMODULE = 0x00000800;
        public const int FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
        public const int FORMAT_MESSAGE_ARGUMENT_ARRAY = 0x00002000;
        public const int FORMAT_MESSAGE_MAX_WIDTH_MASK = 0x000000FF;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const string Kernel32 = "kernel32.dll";
        public const int LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
        static ConcurrentDictionary<string, EventResourceType> ResourceTypes = new ConcurrentDictionary<string, EventResourceType>();

        enum WebEngineEventType : uint
        {
            EVENTLOG_SUCCESS,
            EVENTLOG_INFORMATION_TYPE,
            EVENTLOG_WARNING_TYPE,
            EVENTLOG_ERROR_TYPE
        }

        [System.Flags]
        public enum LoadLibraryFlags : uint
        {
            DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
            LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
            LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
            LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
            LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
            LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
        }

        [DllImport(Kernel32, SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, LoadLibraryFlags dwFlags);

        [DllImport(Kernel32, CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true, BestFitMapping = true)]
        public static extern int FormatMessage(int dwFlags, IntPtr lpSource, uint dwMessageId,
            int dwLanguageId, StringBuilder lpBuffer, int nSize, IntPtr[] arguments);

        [DllImport(Kernel32, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        static public IntPtr GetMessageResources(string fullDLLNameWithPath)
        {
            IntPtr modToLoad = IntPtr.Zero;
            modToLoad = LoadLibraryEx(fullDLLNameWithPath, IntPtr.Zero, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);
            return modToLoad;
        }

        private static bool ExtractDateTimeFromFormattedEvent(string formatEventMessage, out DateTime parsedDateTime)
        {
            string[] arrformatEventMessage = formatEventMessage.Split(Environment.NewLine.ToCharArray());

            var strDateTime = arrformatEventMessage.FirstOrDefault(line => line.StartsWith("Event time"));
            if (!String.IsNullOrEmpty(strDateTime))
            {
                if (DateTime.TryParse(strDateTime, out parsedDateTime))
                    return true;
            }

            parsedDateTime = DateTime.MinValue;
            return false;
        }

        [HandleProcessCorruptedStateExceptions]
        public static string UnsafeTryFormatMessage(IntPtr hModule, uint messageNum, string[] insertionStrings)
        {
            string msg = null;
            int msgLen = 0;
            StringBuilder buf = new StringBuilder(1024);
            int flags = FORMAT_MESSAGE_FROM_HMODULE | FORMAT_MESSAGE_ARGUMENT_ARRAY; //| Utils.FORMAT_MESSAGE_FROM_SYSTEM;

            IntPtr[] addresses = new IntPtr[insertionStrings.Length];
            GCHandle[] handles = new GCHandle[insertionStrings.Length];
            GCHandle stringsRoot = GCHandle.Alloc(addresses, GCHandleType.Pinned);

            // Make sure that we don't try to pass in a zero length array of addresses.  If there are no insertion strings, 
            // we'll use the FORMAT_MESSAGE_IGNORE_INSERTS flag . 
            // If you change this behavior, make sure you look at TryFormatMessage which depends on this behavior!
            if (insertionStrings.Length == 0)
            {
                flags |= FORMAT_MESSAGE_IGNORE_INSERTS;
            }

            try
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i] = GCHandle.Alloc(insertionStrings[i], GCHandleType.Pinned);
                    addresses[i] = handles[i].AddrOfPinnedObject();
                }
                int lastError = ERROR_INSUFFICIENT_BUFFER;
                while (msgLen == 0 && lastError == ERROR_INSUFFICIENT_BUFFER)
                {
                    msgLen = FormatMessage(
                        flags,
                        hModule,
                        messageNum,
                        0,
                        buf,
                        buf.Capacity,
                        addresses);

                    if (msgLen == 0)
                    {
                        lastError = Marshal.GetLastWin32Error();
                        if (lastError == ERROR_INSUFFICIENT_BUFFER)
                        {
                            buf.Capacity = buf.Capacity * 2;
                        }
                        else
                        {
                            msg = string.Format("FormatMessage Failed with error = {0} for messageNum = {1} for hmodule {2} ", lastError.ToString(), messageNum, hModule.ToString());
                        }
                    }
                }
            }
#pragma warning disable CA2153 // Do Not Catch Corrupted State Exceptions
            catch
            {
                msgLen = 0; // return empty on failure
            }
#pragma warning restore CA2153 // Do Not Catch Corrupted State Exceptions
            finally
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    if (handles[i].IsAllocated)
                    {
                        handles[i].Free();
                    }
                }
                stringsRoot.Free();
            }

            if (msgLen > 0)
            {
                msg = buf.ToString();
                // chop off a single CR/LF pair from the end if there is one. FormatMessage always appends one extra.
                if (msg.Length > 1 && msg[msg.Length - 1] == '\n')
                    msg = msg.Substring(0, msg.Length - 2);
            }
            //do not return null
            else
            {
                msg = string.Empty;
            }

            return msg;
        }

        public static long GenerateHexEventIdFromDecimalEventId(int MessageId, int Severity)
        {
            // From http://referencesource.microsoft.com/#System.ServiceModel.Internals/System/Runtime/Diagnostics/EventLogEventId.cs
            // When adding an EventLogEventId, an entry must also be added to src\ndp\cdf\src\WCF\EventLog\EventLog.mc.
            // The hexadecimal representation of each EventId ('0xabbbcccc') can be broken down into 3 parts:
            //     Hex digit  1   ('a')    : Severity : a=0 for Success, a=4 for Informational, a=8 for Warning, a=c for Error
            //     Hex digits 2-4 ('bbb')  : Facility : bbb=001 for Tracing, bbb=002 for ServiceModel, bbb=003 for TransactionBridge, bbb=004 for SMSvcHost, bbb=005 for Info_Cards, bbb=006 for Security_Audit
            //     Hex digits 5-8 ('cccc') : Code     : Each event within the same facility is assigned a unique "code".

            string sevInHexString = (Severity * 4).ToString("X");
            string strHex = sevInHexString + "000" + MessageId.ToString("X4");

            long returnValue = 0;
            long.TryParse(strHex, System.Globalization.NumberStyles.HexNumber, null, out returnValue);
            return returnValue;
        }

        public static async Task<List<ServerSideEvent>> GetEvents(string eventLogPath, string eventLogArchivePath = null)
        {
            var events = new List<ServerSideEvent>();
            XmlNodeList archiveXmlList = null;

            if (!File.Exists(eventLogPath))
            {
                return events;
            }

            InitializeKnownResourceTypes();

            if (!string.IsNullOrEmpty(eventLogArchivePath) && File.Exists(eventLogArchivePath))
            {
                string archiveText = string.Empty;
                XmlDocument archiveDom = null;

                await RetryHelper.RetryOnExceptionAsync<IOException>(10, TimeSpan.FromSeconds(1), async () =>
                {
                    archiveText = await FileSystemHelpers.ReadAllTextFromFileAsync(eventLogArchivePath);
                });

                if (!string.IsNullOrWhiteSpace(archiveText))
                {
                    archiveDom = new XmlDocument();
                    archiveDom.LoadXml(archiveText);
                    archiveXmlList = archiveDom.SelectNodes("/Events/Event");
                }
            }

            string fileText = string.Empty;
            await RetryHelper.RetryOnExceptionAsync<IOException>(10, TimeSpan.FromSeconds(1), async() => 
            {
                fileText = await FileSystemHelpers.ReadAllTextFromFileAsync(eventLogPath);
            });

            if (string.IsNullOrWhiteSpace(fileText))
            {
                return events;
            }

            XmlDocument currentDom = new XmlDocument();
            currentDom.LoadXml(fileText);

            XmlNodeList currentXmlList = currentDom.SelectNodes("/Events/Event");
            List<XmlNode> xmlList;
            if (archiveXmlList == null || archiveXmlList.Count == 0)
            {
                xmlList = new List<XmlNode>(currentXmlList.Cast<XmlNode>());
            }
            else
            {
                xmlList = new List<XmlNode>(archiveXmlList.Cast<XmlNode>().Concat(currentXmlList.Cast<XmlNode>()));
            }

            for (int i = (xmlList.Count - 1); i >= 0; i--)
            {
                var serverSideEvent = new ServerSideEvent();
                XmlNode eventNode = xmlList[i];
                var systemNode = eventNode.SelectSingleNode("System");
                var eventDataNode = eventNode.SelectSingleNode("EventData");

                string strProvider = systemNode["Provider"].GetAttribute("Name");
                serverSideEvent.Source = strProvider;
                string dateTimeString = systemNode["TimeCreated"].GetAttribute("SystemTime");

                bool booValidDateFound = false;

                if (dateTimeString.Contains("T") && dateTimeString.Contains("Z"))
                {
                    if (DateTime.TryParse(dateTimeString, out DateTime resultDateTime))
                    {
                        serverSideEvent.DateAndTime = resultDateTime;
                        booValidDateFound = true;
                    }
                    else
                    {
                        if (DateTime.TryParse(systemNode["TimeCreated"].GetAttribute("SystemTime"), out resultDateTime))
                        {
                            serverSideEvent.DateAndTime = resultDateTime;
                            booValidDateFound = true;
                        }
                    }
                }
                else
                {
                    if (DateTime.TryParse(systemNode["TimeCreated"].GetAttribute("SystemTime"), out DateTime resultDateTime))
                    {
                        serverSideEvent.DateAndTime = resultDateTime;
                        booValidDateFound = true;
                    }
                }

                serverSideEvent.EventID = systemNode["EventID"].InnerText;
                serverSideEvent.TaskCategory = systemNode["Task"].InnerText;
                serverSideEvent.EventRecordID = systemNode["EventRecordID"].InnerText;
                serverSideEvent.Computer = systemNode["Computer"].InnerText;

                List<string> arrayOfdata = new List<string>();

                foreach (XmlNode datanode in eventDataNode.ChildNodes)
                {
                    arrayOfdata.Add(datanode.InnerText);
                }

                string[] args = arrayOfdata.ToArray();
                int eventId = Convert.ToInt32(systemNode["EventID"].InnerText);

                string strLevel = systemNode["Level"].InnerText;

                int intLevel = -1;
                int.TryParse(strLevel, out intLevel);


                if (strProvider.StartsWith("ASP.NET"))
                {
                    int level = MapEventTypeToWebEngineEventType(intLevel);
                    var aspnetResourceType = ResourceTypes.Values.Where(x => x.SourceStartsWith.StartsWith("ASP.NET"));
                    if (aspnetResourceType != null)
                    {
                        serverSideEvent.Description = GetDescriptionForEvent(aspnetResourceType.FirstOrDefault().PtrHandle, eventId, intLevel, eventDataNode, args);
                    }
                    if (!booValidDateFound)
                    {
                        if (ExtractDateTimeFromFormattedEvent(serverSideEvent.Description, out DateTime parsedDateTime))
                        {
                            serverSideEvent.DateAndTime = parsedDateTime;
                        }
                        else
                        {
                            if (DateTime.TryParse(systemNode["TimeCreated"].GetAttribute("SystemTime"), out DateTime resultDateTime))
                            {
                                serverSideEvent.DateAndTime = resultDateTime;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var item in ResourceTypes)
                    {
                        if (strProvider.StartsWith(item.Value.SourceStartsWith))
                        {
                            serverSideEvent.Description = GetDescriptionForEvent(item.Value.PtrHandle, eventId, intLevel, eventDataNode, args);
                            serverSideEvent.Description += Environment.NewLine + string.Join(Environment.NewLine, args);
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(serverSideEvent.Description))
                {
                    serverSideEvent.Description = string.Join(Environment.NewLine, args);
                }

                serverSideEvent.Level = systemNode["Level"].InnerText;
                events.Add(serverSideEvent);
            }



            return events;
        }

        private static void InitializeKnownResourceTypes()
        {
            if (ResourceTypes.Count == 0)
            {
                ResourceTypes.TryAdd("g_hResourcesASPNET", new EventResourceType
                {
                    ResourceFile = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\aspnet_rc.dll"),
                    SourceStartsWith = "ASP.NET"
                });

                ResourceTypes.TryAdd("g_hResourcesPowerShell", new EventResourceType
                {
                    ResourceFile = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\WindowsPowerShell\v1.0\pwrshmsg.dll"),
                    SourceStartsWith = "PowerShell"
                });

                ResourceTypes.TryAdd("g_hiisres", new EventResourceType
                {
                    ResourceFile = Environment.ExpandEnvironmentVariables(@"%windir%\system32\inetsrv\iisres.dll"),
                    SourceStartsWith = "W3SVC-WP"
                });
                ResourceTypes.TryAdd("g_netruntime", new EventResourceType
                {
                    ResourceFile = Environment.ExpandEnvironmentVariables(@"%windir%\system32\mscoree.dll"),
                    SourceStartsWith = ".NET Runtime"
                });

                foreach (var key in ResourceTypes.Keys)
                {
                    ResourceTypes[key].PtrHandle = GetMessageResources(ResourceTypes[key].ResourceFile);
                }
            }
        }

        private static string GetDescriptionForEvent(IntPtr ptrResources, int messageId, int intLevel, XmlNode eventDataNode, string[] args)
        {
            string formatEventMessage = "";
            long longHexEventId = GenerateHexEventIdFromDecimalEventId(messageId, intLevel);

            uint uintEventId = 0;
            if (UInt32.TryParse(longHexEventId.ToString(), out uintEventId))
            {
                formatEventMessage = UnsafeTryFormatMessage(ptrResources, uintEventId, args);
            }

            if (formatEventMessage.StartsWith("FormatMessage Failed with error"))
            {
                formatEventMessage = eventDataNode.InnerXml;
            }

            return formatEventMessage;
        }

        private static int MapEventTypeToWebEngineEventType(int strLevel)
        {
            /*
                From https://msdn.microsoft.com/en-us/library/windows/desktop/aa363679(v=vs.85).aspx
                EVENTLOG_SUCCESS            0x0000
                EVENTLOG_AUDIT_FAILURE      0x0010
                EVENTLOG_AUDIT_SUCCESS      0x0008
                EVENTLOG_ERROR_TYPE         0x0001
                EVENTLOG_INFORMATION_TYPE   0x0004
                EVENTLOG_WARNING_TYPE       0x0002
            */
            int wType = -1;
            switch (strLevel)
            {
                case 0:
                    wType = (int)WebEngineEventType.EVENTLOG_SUCCESS;
                    break;
                case (int)EventLogEntryType.Error:
                    wType = (int)WebEngineEventType.EVENTLOG_ERROR_TYPE;
                    break;
                case (int)EventLogEntryType.Warning:
                    wType = (int)WebEngineEventType.EVENTLOG_WARNING_TYPE;
                    break;
                case (int)EventLogEntryType.Information:
                    wType = (int)WebEngineEventType.EVENTLOG_INFORMATION_TYPE;
                    break;
            }
            return wType;

        }
    }
}