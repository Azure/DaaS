// -----------------------------------------------------------------------
// <copyright file="JavaThread.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

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

        public static int GetThreadIdandState(string line, out string state)
        {
            int intThreadId = -1;
            state = string.Empty;
            var lineParts = line.Split(':');
            if (lineParts.Length > 0)
            {
                var threadId = lineParts[0].Replace("Thread ", "");
                if (Int32.TryParse(threadId, out intThreadId))
                {
                    state = GetThreadState(lineParts[1]);
                }
            }

            return intThreadId;
        }

        public static string GetThreadState(string v)
        {
            var state = v.Replace(" ", "");
            if (state.StartsWith("(state="))
            {
                state = state.Substring(7);
                state = state.TrimEnd(')');
            }
            return state;
        }

        public static JStackDump ParseJstackLog(string log)
        {
            JStackDump stackDump = new JStackDump();
            List<JavaThread> threads = new List<JavaThread>();

            stackDump.Threads = threads;

            JavaThread currentThread = null;

            foreach (var line in File.ReadAllLines(log))
            {
                if (line.StartsWith("Thread "))
                {
                    string state = string.Empty;
                    int threadId = JavaThread.GetThreadIdandState(line, out state);
                    JavaThread j = new JavaThread
                    {
                        Id = threadId,
                        State = state,
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
                        if (line.StartsWith(" - "))
                        {
                            currentThread.CallStack.Add(line.Replace(" - ", ""));
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
