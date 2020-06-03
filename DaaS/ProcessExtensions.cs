using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DaaS
{
    public static class ProcessExtensions
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            // These members must match PROCESS_BASIC_INFORMATION
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }

        enum ProcessInfoClass
        {
            ProcessBasicInformation = 0,
            ProcessDebugPort = 7,
            ProcessWow64Information = 26,
            ProcessImageFileName = 27,
            ProcessBreakOnTermination = 29
        }

        private const int WinXPMajorVersion = 5;
        private const int WinXPMinorVersion = 1;

        public static string GetPrintableInfo(this Process process)
        {
            return string.Format("Process {0}, PID = {1}", process.ProcessName, process.Id);
        }

        public static void SafeKillProcess(this Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
            }
        }

        public static List<Process> GetAllChildProcesses(this Process process)
        {
            // Determine the parents of all processes just once so that we don't have to do this caluclation multiple times
            Dictionary<int, List<int>> processChildren = new Dictionary<int, List<int>>();
            foreach (var p in Process.GetProcesses())
            {
                var parent = p.GetParentProcess();

                if (parent == null)
                {
                    continue;
                }

                if (!processChildren.ContainsKey(parent.Id))
                {
                    processChildren[parent.Id] = new List<int>();
                }

                processChildren[parent.Id].Add(p.Id);
            }

            List<Process> childProcess = new List<Process>();

            Queue<Process> processesToGetChildrenFor = new Queue<Process>();
            processesToGetChildrenFor.Enqueue(process);

            while (processesToGetChildrenFor.Count > 0)
            {
                Process parent = processesToGetChildrenFor.Dequeue();
                if (!processChildren.ContainsKey(parent.Id))
                {
                    // This process has no children
                    continue;
                }
                foreach (int childPid in processChildren[parent.Id])
                {
                    Process child = Process.GetProcessById(childPid);
                    childProcess.Add(child);
                    processesToGetChildrenFor.Enqueue(child);
                }
            }

            return childProcess;
        }

        public static bool IsWin64(this Process process)
        {
            if ((Environment.OSVersion.Version.Major > WinXPMajorVersion)
                || ((Environment.OSVersion.Version.Major == WinXPMajorVersion) && (Environment.OSVersion.Version.Minor >= WinXPMinorVersion)))
            {
                IntPtr processHandle;
                bool is32BitProcessRunningUnderWow64;

                try
                {
                    processHandle = Process.GetProcessById(process.Id).Handle;
                }
                catch
                {
                    return false; // access is denied to the process
                }

                return IsWow64Process(processHandle, out is32BitProcessRunningUnderWow64) && !is32BitProcessRunningUnderWow64;
            }

            return false; // not on 64-bit Windows
        }

        /// <summary>
        /// Gets the parent process of the current process.
        /// </summary>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(this Process process)
        {
            try
            {
                return GetParentProcess(process.Handle);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetMainModuleFileName(this Process process, int buffer = 1024)
        {
            var fileNameBuilder = new StringBuilder(buffer);
            uint bufferLength = (uint)fileNameBuilder.Capacity + 1;
            return QueryFullProcessImageName(process.Handle, 0, fileNameBuilder, ref bufferLength) ?
                fileNameBuilder.ToString() :
                null;
        }


        /// <summary>
        /// Gets the parent process of specified process.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(int id)
        {
            Process process = Process.GetProcessById(id);
            return GetParentProcess(process.Handle);
        }

        /// <summary>
        /// Gets the parent process of a specified process.
        /// </summary>
        /// <param name="handle">The process handle.</param>
        /// <returns>An instance of the Process class.</returns>
        private static Process GetParentProcess(IntPtr handle)
        {
            ProcessBasicInformation pbi = new ProcessBasicInformation();
            int returnLength;
            int status = NtQueryInformationProcess(handle, (int)ProcessInfoClass.ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out returnLength);
            if (status != 0)
                throw new Win32Exception(status);

            try
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch (ArgumentException)
            {
                // not found
                return null;
            }
        }

        #region Native Methods

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

        /// <summary>
        /// A utility class to determine a process parent.
        /// </summary>
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processBasicInformation, int processInformationLength, out int returnLength);

        [DllImport("Kernel32.dll")]
        private static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] uint dwFlags, [Out] StringBuilder lpExeName, [In, Out] ref uint lpdwSize);

        #endregion
    }
}
