// -----------------------------------------------------------------------
// <copyright file="DiagnosticAnalysis.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace DiagnosticAnalysisLauncher
{
    public class DiagnosticAnalysis
    {
        public Interpretedresult[] interpretedResults { get; set; }

        public Assets assets { get; set; }

        [JsonIgnore]
        public Result[] results { get; set; }
    }

    public class Assets
    {
        [JsonProperty("clrmodule", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IDictionary<string, ClrModuleAsset> Clrmodule { get; set; }
    }

    public class ClrModuleAsset
    {
        [JsonProperty("fileNameHash", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string FileNameHash { get; set; }

        [JsonProperty("version", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string Version { get; set; }

        [JsonProperty("isUserModule", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool IsUserModule { get; set; } = false;

        [JsonProperty("hasSymbols", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool HasSymbols { get; set; } = false;

        [JsonProperty("isAspnetCompiled", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool IsAspnetCompiled { get; set; } = false;
    }

    public class Interpretedresult
    {
        [JsonIgnore]
        public Detail[] details { get; set; }
        public string shortDescription { get; set; }
        public string severity { get; set; }
        public string errorCode { get; set; }
        public IDictionary<string, object> stats { get; set; }
    }

    public class Detail
    {
        public Region region { get; set; }
    }

    public class Region
    {
        public string header { get; set; }
        public Item[] items { get; set; }
    }

    public class Item
    {
        public string text { get; set; }
        public Tree tree { get; set; }
    }

    public class Tree
    {
        public Columnmodel columnModel { get; set; }
        public Root[] roots { get; set; }
    }

    public class Columnmodel
    {
        public Column[] columns { get; set; }
        public int defaultSortedColumnId { get; set; }
    }

    public class Column
    {
        public string localizedName { get; set; }
        public bool sortable { get; set; }
        public string defaultSortDirection { get; set; }
        public float widthRatio { get; set; }
    }

    public class Root
    {
        public object[] values { get; set; }
        public Child[] children { get; set; }
    }

    public class Child
    {
        public string[] values { get; set; }
        public object[] children { get; set; }
    }

    public class Result
    {
        public string ErrorCode { get; set; }
        public string AnalyzerId { get; set; }
        public string AnalysisId { get; set; }
        public DumpThread[] Threads { get; set; }
        public int TotalHeapSize { get; set; }
        public Generationstats GenerationStats { get; set; }
        public bool IsPartialHeap { get; set; }
        public Largeobject[] LargeObjects { get; set; }
        public DumpException[] Exceptions { get; set; }
    }

    public class Generationstats
    {
        public _0 _0 { get; set; }
        public _1 _1 { get; set; }
        public _2 _2 { get; set; }
        public _3 _3 { get; set; }
    }

    public class _0
    {
        public int TotalObjectSize { get; set; }
        public int TotalSegmentSize { get; set; }
        public int TotalObjectCount { get; set; }
        public Toptype[] TopTypes { get; set; }
    }

    public class Toptype
    {
        public string TypeName { get; set; }
        public int Size { get; set; }
    }

    public class _1
    {
        public int TotalObjectSize { get; set; }
        public int TotalSegmentSize { get; set; }
        public int TotalObjectCount { get; set; }
        public Toptype1[] TopTypes { get; set; }
    }

    public class Toptype1
    {
        public string TypeName { get; set; }
        public int Size { get; set; }
    }

    public class _2
    {
        public int TotalObjectSize { get; set; }
        public int TotalSegmentSize { get; set; }
        public int TotalObjectCount { get; set; }
        public Toptype2[] TopTypes { get; set; }
    }

    public class Toptype2
    {
        public string TypeName { get; set; }
        public int Size { get; set; }
    }

    public class _3
    {
        public int TotalObjectSize { get; set; }
        public int TotalSegmentSize { get; set; }
        public int TotalObjectCount { get; set; }
        public Toptype3[] TopTypes { get; set; }
    }

    public class Toptype3
    {
        public string TypeName { get; set; }
        public int Size { get; set; }
    }

    public class DumpThread
    {
        public int ThreadId { get; set; }
        public string ThreadName { get; set; }
        public string KernelTime { get; set; }
        public string UserTime { get; set; }
        public object[] Stack { get; set; }
    }

    public class Largeobject
    {
        public string Address { get; set; }
        public string TypeName { get; set; }
        public int Size { get; set; }
    }

    public class DumpException
    {
        public Object Object { get; set; }
        public int Count { get; set; }
    }

    public class Object
    {
        public string TypeName { get; set; }
        public string Message { get; set; }
        public object Thread { get; set; }
        public object[] StackTrace { get; set; }
        public object InnerException { get; set; }
        public string Address { get; set; }
    }
}
