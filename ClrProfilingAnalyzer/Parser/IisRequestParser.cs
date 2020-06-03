using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.IIS_Trace;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace ClrProfilingAnalyzer.Parser
{

    public class IisRequestParser
    {
        static int g_processId = 0;
        public static bool ContainsIisEvents { get; set; } = false;

        static private IisRequest GenerateFakeIISRequest(Guid contextId, TraceEvent traceEvent, double timeStamp = 0)
        {
            IisRequest request = new IisRequest()
            {
                ContextId = contextId
            };
            if (traceEvent != null)
            {
                request.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            }
            else
            {
                request.StartTimeRelativeMSec = timeStamp;
            }
            request.Method = "UNKNOWN";
            request.Path = "Unkwown (GENERAL_REQUEST_START event not captured in trace)";

            return request;
        }

        static private void AddGenericStartEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, IisRequest> iisRequests, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            if (traceEvent.ProcessID != g_processId)
            {
                return;
            }

            ContainsIisEvents = true;

            IisRequest request;

            if (!iisRequests.TryGetValue(contextId, out request))
            {
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event but we got a Module Event fired for this request 
                // so we do our best to create a FAKE start request event
                // populating as much information as we can.
                request = GenerateFakeIISRequest(contextId, null, traceEvent.TimeStampRelativeMSec);
                iisRequests.Add(contextId, request);
            }

            var iisPipelineEvent = new IisPipelineEvent();
            if (string.IsNullOrEmpty(pipelineEventName))
            {
                if (traceEvent.OpcodeName.ToLower().EndsWith("_start"))
                {
                    iisPipelineEvent.Name = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 6);
                }
                // For All the AspnetReq events, they start with Enter or Begin
                // Also, we want to append the AspnetReq/ in front of them so we can easily distinguish them
                // as coming from ASP.NET pipeline
                else if (traceEvent.OpcodeName.ToLower().EndsWith("enter") || traceEvent.OpcodeName.ToLower().EndsWith("begin"))
                {
                    iisPipelineEvent.Name = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                }
            }
            else
            {
                iisPipelineEvent.Name = pipelineEventName;
            }

            int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);

            iisPipelineEvent.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            iisPipelineEvent.StartThreadId = traceEvent.ThreadID;
            iisPipelineEvent.ProcessId = traceEvent.ProcessID;
            iisPipelineEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
            request.PipelineEvents.Add(iisPipelineEvent);

        }

        static private void AddGenericStopEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, IisRequest> iisRequests, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            if (traceEvent.ProcessID != g_processId)
            {
                return;
            }

            ContainsIisEvents = true;
            IisRequest request;
            if (iisRequests.TryGetValue(contextId, out request))
            {
                string eventName = "";

                if (string.IsNullOrEmpty(pipelineEventName))
                {
                    if (traceEvent.OpcodeName.ToLower().EndsWith("_end"))
                    {
                        eventName = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 4);
                    }

                    // For All the AspnetReq events, they finish with Leave. Also, we want to append the AspnetReq/ 
                    // in front of them so we can easily distinguish them as coming from ASP.NET pipeline
                    else if (traceEvent.OpcodeName.ToLower().EndsWith("leave"))
                    {
                        eventName = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                    }
                }
                else
                {
                    eventName = pipelineEventName;
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);
                var iisPipelineEvent = request.PipelineEvents.FirstOrDefault(m => (m.Name == eventName) && m.EndTimeRelativeMSec == 0 && m.ChildRequestRecurseLevel == childRequestRecurseLevel);
                if (iisPipelineEvent != null)
                {
                    iisPipelineEvent.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    iisPipelineEvent.EndThreadId = traceEvent.ThreadID;
                }
            }
        }
        static public IisPipelineEvent GetSlowestEvent(IisRequest request)
        {
            IisPipelineEvent slowestPipelineEvent = new IisPipelineEvent();
            double slowestTime = 0;

            foreach (var pipeLineEvent in request.PipelineEvents)
            {
                if (pipeLineEvent.StartTimeRelativeMSec != 0 && pipeLineEvent.EndTimeRelativeMSec != 0)
                {
                    var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;
                    if (timeinThisEvent > slowestTime)
                    {
                        slowestTime = timeinThisEvent;
                        slowestPipelineEvent = pipeLineEvent;

                    }
                }
            }

            // Lets check for containment to see if a child event is taking more than 50% 
            // of the time of this pipeline event, then we want to call that out
            foreach (var pipeLineEvent in request.PipelineEvents.Where(x => (x.StartTimeRelativeMSec > slowestPipelineEvent.StartTimeRelativeMSec) && (x.EndTimeRelativeMSec <= slowestPipelineEvent.EndTimeRelativeMSec)))
            {
                var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;

                if (((timeinThisEvent / slowestTime) * 100) > 50)
                {
                    slowestTime = timeinThisEvent;
                    slowestPipelineEvent = pipeLineEvent;
                }

            }

            var timeInSlowestEvent = slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;
            var requestExecutionTime = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

            if (timeInSlowestEvent > 0 && requestExecutionTime > 500)
            {
                if (((timeInSlowestEvent / requestExecutionTime) * 100) < 50)
                {
                    // So this is the scenario where the default set of events that we are tracking
                    // do not have any delay. Lets do our best and see if we can atleast
                    // populate the StartTime, EndTime                    

                    IisPipelineEvent unKnownPipeLineEvent = CheckForDelayInUnknownEvents(request, timeInSlowestEvent);

                    if (unKnownPipeLineEvent != null)
                    {
                        slowestPipelineEvent = unKnownPipeLineEvent;
                    }
                }
            }

            return slowestPipelineEvent;
        }

        public static IisPipelineEvent CheckForDelayInUnknownEvents(IisRequest request, double timeInSlowestEvent)
        {
            double slowestTimeInThisEvent = 0;
            int position = 0;
            var pipelineEventsArray = request.PipelineEvents.ToArray();
            for (int i = 0; i < pipelineEventsArray.Length - 1; i++)
            {
                if (pipelineEventsArray[i].EndTimeRelativeMSec != 0)
                {
                    var timeDiff = pipelineEventsArray[i + 1].StartTimeRelativeMSec - pipelineEventsArray[i].EndTimeRelativeMSec;
                    if (slowestTimeInThisEvent < timeDiff)
                    {
                        slowestTimeInThisEvent = timeDiff;
                        position = i;
                    }
                }
            }

            IisPipelineEvent unknownEvent = null;

            if ((slowestTimeInThisEvent / timeInSlowestEvent) > 1.5)
            {
                if (position > 0)
                {
                    unknownEvent = new IisPipelineEvent();
                    unknownEvent.Name = "UNKNOWN";
                    unknownEvent.StartThreadId = pipelineEventsArray[position].EndThreadId;
                    unknownEvent.EndThreadId = pipelineEventsArray[position + 1].StartThreadId;
                    unknownEvent.StartTimeRelativeMSec = pipelineEventsArray[position].EndTimeRelativeMSec;
                    unknownEvent.EndTimeRelativeMSec = pipelineEventsArray[position + 1].StartTimeRelativeMSec;
                    unknownEvent.ProcessId = pipelineEventsArray[position + 1].ProcessId;
                }
            }

            return unknownEvent;
        }

        public static List<IisRequestInfo> ParseClrExceptions(TraceLog dataFile, Dictionary<Guid, IisRequest> iisRequests, out int failedRequestsWithClrExceptions, out List<ExceptionSummaryByName> exceptionSummary)
        {
            List<IisRequestInfo> listRequestsFailed = new List<IisRequestInfo>();

            List<ExceptionDetails> allExceptions = new List<ExceptionDetails>();
            failedRequestsWithClrExceptions = 0;

            var dispatcher = dataFile.Events.GetSource();

            var clr = new ClrTraceEventParser(dispatcher);

            clr.ExceptionStart += delegate (ExceptionTraceData data)
            {
                //if (data.ProcessID == g_processId)
                {
                    ExceptionDetails ex = new ExceptionDetails();

                    if (data.ExceptionMessage == "NULL")
                    {
                        ex.ExceptionMessage = "''";
                    }
                    else
                    {
                        ex.ExceptionMessage = data.ExceptionMessage;
                    }
                    ex.ExceptionType = data.ExceptionType;
                    ex.ThreadId = data.ThreadID;
                    ex.ProcessId = data.ProcessID;
                    ex.TimeStampRelativeMSec = data.TimeStampRelativeMSec;
                    ex.ProcessName = data.ProcessName;
                    if (ex.StackTrace == null)
                    {
                        ex.StackTrace = new List<string>();
                    }

                    var cs = data.CallStack();

                    var stackTrace = "";

                    if (cs != null)
                    {
                        while (cs != null)
                        {

                            stackTrace = $"{cs.CodeAddress.ModuleName}!{cs.CodeAddress.FullMethodName}";
                            cs = cs.Caller;
                            if (!string.IsNullOrWhiteSpace(stackTrace))
                            {
                                if (stackTrace.Contains("."))
                                {
                                    if (stackTrace.IndexOf('(') > 1)
                                    {
                                        ex.StackTrace.Add(stackTrace.Substring(0, stackTrace.IndexOf('(')));
                                    }
                                    else
                                    {
                                        ex.StackTrace.Add(stackTrace);
                                    }
                                }
                            }
                        }
                    }
                    ex.StackTraceHash = string.Join(",", ex.StackTrace).GetHashCode();

                    if (ex.ExceptionMessage.Length > 5)
                    {
                        allExceptions.Add(ex);
                    }
                }
            };

            dispatcher.Process();

            foreach (var request in iisRequests.Values.Where(x => x.FailureDetails != null))
            {
                IisRequestInfo iisRequest = new IisRequestInfo();

                iisRequest.Method = request.Method;
                iisRequest.ContextId = request.ContextId;
                iisRequest.slowestPipelineEvent = IisRequestParser.GetSlowestEvent(request);
                iisRequest.totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;
                iisRequest.requestPath = request.Path;
                iisRequest.csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                iisRequest.scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();
                iisRequest.statusCode = (request.StatusCode == 0) ? "-" : request.StatusCode.ToString();
                iisRequest.SubStatusCode = request.SubStatusCode.ToString();
                iisRequest.FailureDetails = request.FailureDetails;
                iisRequest.FailureDetails.ExceptionDetails = FindExceptionForThisRequest(request, allExceptions);
                if (iisRequest.FailureDetails.ExceptionDetails.Count > 0)
                {
                    failedRequestsWithClrExceptions++;
                }
                listRequestsFailed.Add(iisRequest);
            }

            var groupedExceptions = from c in allExceptions
                                    group c by new
                                    {
                                        c.ProcessName,
                                        c.ExceptionType,
                                        c.ExceptionMessage,
                                        c.StackTraceHash
                                    }
                                     into g
                                    select new ExceptionSummary()
                                    {
                                        ProcessName = g.Key.ProcessName,
                                        ExceptionType = g.Key.ExceptionType,
                                        ExceptionMessage = g.Key.ExceptionMessage,
                                        StackTraceHash = g.Key.StackTraceHash,
                                        Count = g.Count()
                                    };

            //
            // This additional grouping is done to remove similar stacks
            // and take only top 10 stack traces.
            //

            var groupedExceptionsByName = from c in groupedExceptions
                                          group c by new
                                          {
                                              c.ProcessName,
                                              c.ExceptionType,
                                              c.ExceptionMessage,
                                              c.Count
                                          }
                                     into g
                                          select new ExceptionSummaryByName()
                                          {
                                              ProcessName = g.Key.ProcessName,
                                              ExceptionType = g.Key.ExceptionType,
                                              ExceptionMessage = g.Key.ExceptionMessage,
                                              Count = g.Key.Count,
                                              StackTraceHashes = g.Select(x => x.StackTraceHash).Take(10).ToList()

                                          };

            var groupedExceptionsArray = groupedExceptionsByName.ToArray();
            for (int i = 0; i < groupedExceptionsArray.Count(); i++)
            {
                groupedExceptionsArray[i].StackTrace = new List<StackSummary>();
                foreach (var stackTraceHash in groupedExceptionsArray[i].StackTraceHashes)
                {
                    StackSummary s = new StackSummary
                    {
                        StackTraceHash = stackTraceHash,
                        StackTrace = allExceptions.Find(x => x.StackTraceHash == stackTraceHash).StackTrace
                    };
                    groupedExceptionsArray[i].StackTrace.Add(s);
                }

            }

            exceptionSummary = groupedExceptionsArray.OrderByDescending(x => x.Count).ToList();

            return listRequestsFailed;
        }

        public static Dictionary<Guid, IisRequest> ParseIISEvents(TraceLog dataFile, int processId, Dictionary<Guid, IisRequest> iisRequests)
        {
            g_processId = processId;

            var dispatcher = dataFile.Events.GetSource();

            dispatcher.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-ASPNET", "Request/Send", delegate (TraceEvent data)
            {
                if (iisRequests.TryGetValue(data.ActivityID, out IisRequest iisRequest))
                {
                    if (data.RelatedActivityID != Guid.Empty)
                    {
                        iisRequest.RelatedActivityId = data.RelatedActivityID;
                    }
                }
            });

            Dictionary<Guid, int> childRequests = new Dictionary<Guid, int>();

            var iis = new IisTraceEventParser(dispatcher);

            int startcount = 0;
            int endcount = 0;

            iis.IISGeneralGeneralChildRequestStart += delegate (W3GeneralChildRequestStart traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel + 1;
                    }
                }
                else
                {
                    childRequests.Add(traceEvent.ContextId, 1);
                }

            };

            iis.IISGeneralGeneralChildRequestEnd += delegate (W3GeneralChildRequestEnd traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel - 1;
                    }
                }
            };

            iis.IISGeneralGeneralRequestStart += delegate (W3GeneralStartNewRequest traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest req = new IisRequest()
                {
                    ContextId = traceEvent.ContextId,
                    StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                    Method = traceEvent.RequestVerb,
                    Path = traceEvent.RequestURL
                };

                // This check is required for requests which have child
                // request events in them. For those, the StartNewRequest 
                // would be called twice for the same request. At this 
                // point, I don't think that is causing any problems to us
                if (!iisRequests.ContainsKey(traceEvent.ContextId))
                {
                    iisRequests.Add(traceEvent.ContextId, req);
                }

                startcount++;
            };
            iis.IISGeneralGeneralRequestEnd += delegate (W3GeneralEndNewRequest traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    request.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    request.BytesReceived = traceEvent.BytesReceived;
                    request.BytesSent = traceEvent.BytesSent;
                    request.StatusCode = traceEvent.HttpStatus;
                    request.SubStatusCode = traceEvent.HttpSubStatus;

                }

                endcount++;
            };
            iis.IISRequestNotificationPreBeginRequestStart += delegate (IISRequestNotificationPreBeginStart traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (!iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(traceEvent.ContextId, traceEvent);
                    iisRequests.Add(traceEvent.ContextId, request);
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(traceEvent.ContextId, childRequests);

                var iisPrebeginModuleEvent = new IisPrebeginModuleEvent()
                {
                    Name = traceEvent.ModuleName,
                    StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                    ProcessId = traceEvent.ProcessID,
                    StartThreadId = traceEvent.ThreadID,
                    ChildRequestRecurseLevel = childRequestRecurseLevel

                };
                request.PipelineEvents.Add(iisPrebeginModuleEvent);
            };
            iis.IISRequestNotificationPreBeginRequestEnd += delegate (IISRequestNotificationPreBeginEnd traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(traceEvent.ContextId, childRequests);
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    var module = request.PipelineEvents.FirstOrDefault(m => m.Name == traceEvent.ModuleName && m.ChildRequestRecurseLevel == childRequestRecurseLevel);

                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                        module.EndThreadId = traceEvent.ThreadID;
                    }
                }
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event as well as Module Start event for the request but we got 
                // a Module End Event fired for this request. Assuming this happens, 
                // the worst we will miss is delay between this module end event
                // to the next module start event and that should ideally be very
                // less. Hence we don't need the else part for this condition
                //else { }  
            };
            iis.IISRequestNotificationNotifyModuleStart += delegate (IISRequestNotificationEventsStart traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (!iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(traceEvent.ContextId, traceEvent);
                    iisRequests.Add(traceEvent.ContextId, request);
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(traceEvent.ContextId, childRequests);
                var iisModuleEvent = new IisModuleEvent()
                {
                    Name = traceEvent.ModuleName,
                    StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                    ProcessId = traceEvent.ProcessID,
                    StartThreadId = traceEvent.ThreadID,
                    fIsPostNotification = traceEvent.fIsPostNotification,
                    Notification = (RequestNotification)traceEvent.Notification,
                    ChildRequestRecurseLevel = childRequestRecurseLevel,
                    foundEndEvent = false
                };
                request.PipelineEvents.Add(iisModuleEvent);
            };
            iis.IISRequestNotificationNotifyModuleEnd += delegate (IISRequestNotificationEventsEnd traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(traceEvent.ContextId, childRequests);
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    IEnumerable<IisModuleEvent> iisModuleEvents = request.PipelineEvents.OfType<IisModuleEvent>();
                    var module = iisModuleEvents.FirstOrDefault(m => m.Name == traceEvent.ModuleName && m.Notification == (RequestNotification)traceEvent.Notification && m.fIsPostNotification == traceEvent.fIsPostNotificationEvent && m.ChildRequestRecurseLevel == childRequestRecurseLevel && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                        module.EndThreadId = traceEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }

                // so this is the case where we dont have a GENERAL_REQUEST_START event as well 
                // as Module Start event for the request but we got a Module End Event fired for 
                // this request. Assuming this happens, the worst we will miss is delay between 
                // this module end event to the next module start event and that should ideally be 
                // less. Hence we don't need the else part for this condition

            };
            iis.IISRequestNotificationModuleSetResponseErrorStatus += delegate (IISRequestNotificationEventsResponseErrorStatus traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    request.FailureDetails = new RequestFailureDetails()
                    {
                        HttpReason = traceEvent.HttpReason,
                        HttpStatus = traceEvent.HttpStatus,
                        HttpSubStatus = traceEvent.HttpSubStatus,
                        ModuleName = traceEvent.ModuleName,
                        ConfigExceptionInfo = traceEvent.ConfigExceptionInfo,
                        Notification = (RequestNotification)traceEvent.Notification,
                        TimeStampRelativeMSec = traceEvent.TimeStampRelativeMSec
                    };
                }
            };
            iis.IISGeneralGeneralFlushResponseStart += delegate (W3GeneralFlushResponseStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISGeneralGeneralFlushResponseEnd += delegate (W3GeneralFlushResponseEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISGeneralGeneralReadEntityStart += delegate (W3GeneralReadEntityStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISGeneralGeneralReadEntityEnd += delegate (W3GeneralReadEntityEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheFileCacheAccessStart += delegate (W3CacheFileCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheFileCacheAccessEnd += delegate (W3CacheFileCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheUrlCacheAccessStart += delegate (W3CacheURLCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheUrlCacheAccessEnd += delegate (W3CacheURLCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISFilterFilterStart += delegate (W3FilterStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISFilterFilterEnd += delegate (W3FilterEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISAuthenticationAuthStart += delegate (W3AuthStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISAuthenticationAuthEnd += delegate (W3AuthEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheOutputCacheLookupStart += delegate (W3OutputCacheLookupStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCacheOutputCacheLookupEnd += delegate (W3OutputCacheLookupEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCompressionDynamicCompressionStart += delegate (W3DynamicCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCompressionDynamicCompressionEnd += delegate (W3DynamicCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCompressionStaticCompressionStart += delegate (W3StaticCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISCompressionStaticCompressionEnd += delegate (W3StaticCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersStart += delegate (W3FilterPreprocStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersEnd += delegate (W3FilterPreprocEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests);
            };


            var aspNet = new AspNetTraceEventParser(dispatcher);

            // The logic used here is that delays between "AspNetTrace/AspNetReq/Start" and "AspNetTrace/AspNetReq/AppDomainEnter"
            // will be due to the delay introduced due to the CLR threadpool code based on how
            // ASP.NET code emits these events.
            aspNet.AspNetReqStart += delegate (AspNetStartTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests, "CLRThreadPoolQueue");
                }
            };
            aspNet.AspNetReqAppDomainEnter += delegate (AspNetAppDomainEnterTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests, "CLRThreadPoolQueue");
                }
            };

            aspNet.AspNetReqSessionDataBegin += delegate (AspNetAcquireSessionBeginTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests, "AspNetReqSessionData");
                }
            };
            aspNet.AspNetReqSessionDataEnd += delegate (AspNetAcquireSessionEndTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, iisRequests, childRequests, "AspNetReqSessionData");
                }
            };

            aspNet.AspNetReqPipelineModuleEnter += delegate (AspNetPipelineModuleEnterTraceData traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {

                    var aspnetPipelineModuleEvent = new AspNetPipelineModuleEvent()
                    {
                        Name = traceEvent.ModuleName,
                        ModuleName = traceEvent.ModuleName,
                        StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                        ProcessId = traceEvent.ProcessID,
                        StartThreadId = traceEvent.ThreadID,
                    };
                    request.PipelineEvents.Add(aspnetPipelineModuleEvent);
                }

            };

            aspNet.AspNetReqPipelineModuleLeave += delegate (AspNetPipelineModuleLeaveTraceData traceEvent)
            {
                if (traceEvent.ProcessID != g_processId)
                {
                    return;
                }

                IisRequest request;
                if (iisRequests.TryGetValue(traceEvent.ContextId, out request))
                {
                    IEnumerable<AspNetPipelineModuleEvent> aspnetPipelineModuleEvents = request.PipelineEvents.OfType<AspNetPipelineModuleEvent>();
                    var module = aspnetPipelineModuleEvents.FirstOrDefault(m => m.ModuleName == traceEvent.ModuleName && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                        module.EndThreadId = traceEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }
            };

            // Lets look at the rest of Enter/Leave events in AspNetReq now.

            aspNet.AddCallbackForEvents(name => name.EndsWith("Enter"), null, (TraceEvent traceEvent) =>
            {

                // We are using AspNetReqAppDomainEnter to compute for ClrThreadPool so exclude that for now
                if (!traceEvent.OpcodeName.EndsWith("AppDomainEnter") && !traceEvent.OpcodeName.EndsWith("PipelineModuleEnter"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (iisRequests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStartEventToRequest(contextGuid, traceEvent, iisRequests, childRequests);
                        }

                    }
                }
            });

            aspNet.AddCallbackForEvents(name => name.EndsWith("Leave"), null, (TraceEvent traceEvent) =>
            {
                if (!traceEvent.OpcodeName.EndsWith("PipelineModuleLeave"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (iisRequests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStopEventToRequest(contextGuid, traceEvent, iisRequests, childRequests);
                        }
                    }
                }
            });


            dispatcher.Process();

            // manual fixup for incomplete requests
            foreach (var request in iisRequests.Values.Where(x => x.EndTimeRelativeMSec == 0))
            {
                // so these are all the requests for which we see a GENERAL_REQUEST_START and no GENERAL_REQUEST_END
                // for these it is safe to set the request.EndTimeRelativeMSec to the last timestamp in the trace
                // because that is pretty much the duration that the request is active for.

                request.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;

                // Also, for this request, lets first try to find pipleline start events which don't have a pipeline                
                // stop event next to it. If we find, we just set their end to EndTimeRelativeMSec 
                var incompletePipeLineEvents = request.PipelineEvents.Where(m => m.EndTimeRelativeMSec == 0);

                // not setting incompleteEvent.EndThreadId as this is incorrectly adding a hyperlink for requests
                // requests that are stuck in the session state module
                foreach (var incompleteEvent in incompletePipeLineEvents)
                {
                    incompleteEvent.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;
                }
            }

            return iisRequests;
        }

        private static int GetChildEventRecurseLevel(Guid contextId, Dictionary<Guid, int> childRequests)
        {
            int childRequestRecurseLevel = 0;
            if (childRequests.ContainsKey(contextId))
            {
                childRequests.TryGetValue(contextId, out childRequestRecurseLevel);
            }
            return childRequestRecurseLevel;
        }

        private static List<ExceptionDetails> FindExceptionForThisRequest(IisRequest request, List<ExceptionDetails> allExceptions)
        {
            double startTimeForPipeLineEvent = 0;
            int processId = 0;
            int threadId = 0;
            foreach (IisModuleEvent moduleEvent in request.PipelineEvents.OfType<IisModuleEvent>().Where(x => x.Name == request.FailureDetails.ModuleName))
            {
                if (moduleEvent.Notification == request.FailureDetails.Notification)
                {
                    startTimeForPipeLineEvent = moduleEvent.StartTimeRelativeMSec;
                    processId = moduleEvent.ProcessId;
                    if (moduleEvent.StartThreadId == moduleEvent.EndThreadId)
                    {
                        threadId = moduleEvent.StartThreadId;
                    }
                }
            }

            List<ExceptionDetails> exceptionsList = new List<ExceptionDetails>();

            if (startTimeForPipeLineEvent > 0 && processId != 0 && threadId != 0)
            {

                foreach (var ex in allExceptions.Where(x => x.TimeStampRelativeMSec > startTimeForPipeLineEvent && x.TimeStampRelativeMSec <= request.FailureDetails.TimeStampRelativeMSec
                                                        && processId == x.ProcessId
                                                        && threadId == x.ThreadId))
                {
                    exceptionsList.Add(ex);
                }
            }

            return exceptionsList;
        }
    }
}