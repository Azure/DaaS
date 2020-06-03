using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MySiteDiagnostics.Storage
{
    public interface ILog : IFile
    {
        DateTime StartTime { get; }

        DateTime EndTime { get; }
    }

    public interface IReport : IFile
    { }

    public interface IFile
    {
        string FileName { get; }

        string FullPath { get; }
    }







    class LogStub: ILog
    {
        public DateTime StartTime
        {
            get;
            internal set;
        }

        public DateTime EndTime
        {
            get;
            internal set;
        }

        public string FileName
        {
            get;
            internal set;
        }

        public string FullPath
        {
            get;
            internal set;
        }
    }

    class ReportStub: IReport
    {
        public string FileName
        {
            get;
            internal set;
        }

        public string FullPath
        {
            get;
            internal set;
        }
    }

    class FileStub: IFile
    {
        public string FileName
        {
            get;
            internal set;
        }

        public string FullPath
        {
            get;
            internal set;
        }
    }
}
