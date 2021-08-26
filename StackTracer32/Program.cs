// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Newtonsoft.Json;
using StackTracerCore;

namespace StackTracer32
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

                bool processFound = int.TryParse(args[0], out int processId);
                if (processFound)
                {
                    var threads = Debugger.CollectTraces(processId, outputDirectory);
                    Console.WriteLine($"Found {threads.Count} threads in process {processId}");

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
