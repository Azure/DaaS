//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.IO;
using Newtonsoft.Json;
using StackTracerCore;

namespace StackTracer64
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Pass processId as first parameter and outputPath as second parameter");
            }
            else
            {
                string outputFilePath = args[1];
                string outputDirectory = args[1];
                if (!Directory.Exists(outputFilePath))
                {
                    Console.WriteLine($"Outputpath '{outputFilePath}' does not exist");
                    return;
                }
                else
                {
                    outputFilePath = Path.Combine(outputFilePath, "stacks.json");
                }

                bool processFound = Int32.TryParse(args[0], out int processId);
                if (processFound)
                {
                    DateTime dtStart = DateTime.Now;
                    var threads = Debugger.CollectTraces(processId, outputDirectory);
                    Console.WriteLine($"Process paused for {DateTime.Now.Subtract(dtStart).TotalMilliseconds} ms with {threads.Count} threads");

                    if (threads.Count > 0)
                    {
                        using (StreamWriter file = File.CreateText(outputFilePath))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            serializer.Formatting = Formatting.Indented;
                            serializer.Serialize(file, threads);
                        }
                        Console.WriteLine($"Saved file {outputFilePath} successfully");
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to parse '{args[0]}' as processId");
                }
            }
        }
    }
}
