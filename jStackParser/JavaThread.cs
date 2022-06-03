// -----------------------------------------------------------------------
// <copyright file="JavaThread.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jStackParser
{
    class JStackDump
    {
        public List<JavaThread> Threads;
        public string DeadlockMessage;
        public string SiteName;
        public string Timestamp;
        public string FileName;
        public string FullFilePath;
        public string MachineName;
    }
    class JavaThread
    {
        public string State;
        public int Id;
        public List<string> CallStack;
        public int CallStackHash;
        public string AdditionalStateInfo;

        //
        // Gets ThreadId from output string like below
        // "http-nio-127.0.0.1-30053-ClientPoller" #26 daemon prio=5 os_prio=0 tid=0x00000000169d2000 nid=0x36a4 runnable [0x000000001851e000]
        //
        public static int GetThreadId(string line)
        {
            string threadId = "";
            foreach (var token in line.Split(' '))
            {
                if (token.StartsWith("#"))
                {
                    threadId = token.Substring(1);
                    break;
                }
            }

            if (int.TryParse(threadId, out int tid))
            {
                return tid;
            }

            return -1;
        }

        //
        // Gets State from a string like below
        //    java.lang.Thread.State: RUNNABLE
        //

        private static string GetState(string[] fileContents, int i, out string additionalStateInformation)
        {
            string state = "";
            additionalStateInformation = "";
            int nextLineIndex = i + 1;
            if (nextLineIndex + 1 < fileContents.Length)
            {
                string nextLineContent = fileContents[nextLineIndex];
                if (nextLineContent.Contains("java.lang.Thread.State"))
                {
                    nextLineContent = nextLineContent.Replace(" ", "");
                    state = nextLineContent.Replace("java.lang.Thread.State:", "");
                    int intBracketIndex = state.IndexOf("(");
                    if (intBracketIndex > 0 && intBracketIndex < state.Length - 1) 
                    {
                        additionalStateInformation = state.Substring(intBracketIndex);
                        state = state.Substring(0,intBracketIndex);
                    }
                }
            }

            return state;
        }

        public static JStackDump ParseJstackLog(string log)
        {
            JStackDump stackDump = new JStackDump();
            List<JavaThread> threads = new List<JavaThread>();

            stackDump.Threads = threads;

            JavaThread currentThread = null;

            var fileContents = File.ReadAllLines(log);

            for (int i = 0; i < fileContents.Length; i++)
            {
                string line = fileContents[i];
                if (line.Contains("tid=0x") && line.Contains("nid=0x"))
                {
                    int threadId = GetThreadId(line);
                    if (threadId == -1)
                    {
                        continue;
                    }

                    var state = GetState(fileContents, i, out string addtionalStateInfo);
                    JavaThread j = new JavaThread
                    {
                        Id = threadId,
                        State = state,
                        AdditionalStateInfo = addtionalStateInfo,
                        CallStack = new List<string>()
                    };

                    threads.Add(j);
                    currentThread = j;
                }
                else
                {
                    if (currentThread == null)
                    {
                        if (line.ToLower().Contains("Java-level deadlock".ToLower()))
                        {
                            stackDump.DeadlockMessage = line;
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(stackDump.DeadlockMessage))
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    stackDump.DeadlockMessage += Environment.NewLine + line;
                                }
                            }
                        }

                        continue;
                    }
                    else
                    {
                        if (line.StartsWith("\tat ") || line.StartsWith("\t-"))
                        {
                            currentThread.CallStack.Add(line.Replace("\t", ""));
                        }

                    }
                }
            }

            foreach (var t in threads)
            {
                t.CallStackHash = string.Join(",", t.CallStack).GetHashCode();
            }

            return stackDump;
        }
    }
}
