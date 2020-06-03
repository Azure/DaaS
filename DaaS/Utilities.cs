using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace DaaS
{
    [SuppressUnmanagedCodeSecurity]
    internal static class ProcessNativeMethods
    {
        public const uint TOKEN_QUERY = 0x0008;

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref ProcessInformation processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref IntPtr processInformation,
            int processInformationLength,
            ref int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            // These members must match PROCESS_BASIC_INFORMATION
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }

        public const int ProcessBasicInformation = 0;
        public const int ProcessWow64Information = 26;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            out IntPtr lpPtr,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING_32 lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(
            IntPtr hProcess,
            UInt32 dwDesiredAccess,
            out IntPtr processToken);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public int AllocationProtect;
            public IntPtr RegionSize;
            public int State;
            public int Protect;
            public int Type;
        }

        public const int PAGE_NOACCESS = 0x01;
        public const int PAGE_EXECUTE = 0x10;

        [DllImport("kernel32")]
        public static extern IntPtr VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, ref MEMORY_BASIC_INFORMATION lpBuffer, IntPtr dwLength);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)]out bool wow64Process);

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING_32
        {
            public ushort Length;
            public ushort MaximumLength;
            public int Buffer;
        }
    }
    public static class Utilities
    {
        public static bool GetIsScmSite(Dictionary<string, string> environment)
        {
            string appPool = null;
            if (environment.TryGetValue("APP_POOL_ID", out appPool) && !string.IsNullOrEmpty(appPool))
            {
                return appPool.StartsWith("~1", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        private static IntPtr GetPeb64(IntPtr hProcess)
        {
            return GetPebNative(hProcess);
        }

        private static IntPtr GetPebNative(IntPtr hProcess)
        {
            var pbi = new ProcessNativeMethods.ProcessInformation();
            int res_len = 0;
            int pbiSize = Marshal.SizeOf(pbi);
            ProcessNativeMethods.NtQueryInformationProcess(
                hProcess,
                ProcessNativeMethods.ProcessBasicInformation,
                ref pbi,
                pbiSize,
                out res_len);

            if (res_len != pbiSize)
            {
                throw new Win32Exception("Unable to query process information.");
            }

            return pbi.PebBaseAddress;
        }

        // should just be ReadIntPtr not TryReadIntPtr, throw when failed
        private static bool TryReadIntPtr(IntPtr hProcess, IntPtr ptr, out IntPtr readPtr)
        {
            var dataSize = new IntPtr(IntPtr.Size);
            var res_len = IntPtr.Zero;
            if (!ProcessNativeMethods.ReadProcessMemory(
                hProcess,
                ptr,
                out readPtr,
                dataSize,
                ref res_len))
            {
                // automatically GetLastError() and format message
                throw new Win32Exception();
            }

            // This is more like an assert
            return res_len == dataSize;
        }

        private static IntPtr GetPeb32(IntPtr hProcess)
        {
            if (System.Environment.Is64BitProcess)
            {
                var ptr = IntPtr.Zero;
                int res_len = 0;
                int pbiSize = IntPtr.Size;
                ProcessNativeMethods.NtQueryInformationProcess(
                    hProcess,
                    ProcessNativeMethods.ProcessWow64Information,
                    ref ptr,
                    pbiSize,
                    ref res_len);

                if (res_len != pbiSize)
                {
                    throw new Win32Exception("Unable to query process information.");
                }

                return ptr;
            }
            else
            {
                return GetPebNative(hProcess);
            }
        }

        public static int GetProcessBitness(IntPtr hProcess)
        {
            if (System.Environment.Is64BitOperatingSystem)
            {
                bool wow64;
                if (!ProcessNativeMethods.IsWow64Process(hProcess, out wow64))
                {
                    return 32;
                }
                if (wow64)
                {
                    return 32;
                }
                return 64;
            }
            else
            {
                return 32;
            }
        }

        private static IntPtr GetPenv(IntPtr hProcess)
        {
            int processBitness = GetProcessBitness(hProcess);

            if (processBitness == 64)
            {
                if (!System.Environment.Is64BitProcess)
                {
                    throw new Win32Exception(
                        "The current process should run in 64 bit mode to be able to get the environment of another 64 bit process.");
                }

                IntPtr pPeb = GetPeb64(hProcess);

                IntPtr ptr;
                if (!TryReadIntPtr(hProcess, pPeb + 0x20, out ptr))
                {
                    throw new Win32Exception("Unable to read PEB.");
                }

                IntPtr penv;
                if (!TryReadIntPtr(hProcess, ptr + 0x80, out penv))
                {
                    throw new Win32Exception("Unable to read RTL_USER_PROCESS_PARAMETERS.");
                }

                return penv;
            }
            else
            {
                IntPtr pPeb = GetPeb32(hProcess);

                IntPtr ptr;
                if (!TryReadIntPtr(hProcess, pPeb + 0x10, out ptr))
                {
                    throw new Win32Exception("Unable to read PEB.");
                }

                IntPtr penv;
                if (!TryReadIntPtr(hProcess, ptr + 0x48, out penv))
                {
                    throw new Win32Exception("Unable to read RTL_USER_PROCESS_PARAMETERS.");
                }

                return penv;
            }
        }

        private static bool HasReadAccess(IntPtr hProcess, IntPtr address, out int size)
        {
            size = 0;

            var memInfo = new ProcessNativeMethods.MEMORY_BASIC_INFORMATION();
            IntPtr result = ProcessNativeMethods.VirtualQueryEx(
                hProcess,
                address,
                ref memInfo,
                (IntPtr)Marshal.SizeOf(memInfo));

            if (result == IntPtr.Zero)
            {
                return false;
            }

            if (memInfo.Protect == ProcessNativeMethods.PAGE_NOACCESS || memInfo.Protect == ProcessNativeMethods.PAGE_EXECUTE)
            {
                return false;
            }

            try
            {
                size = Convert.ToInt32(memInfo.RegionSize.ToInt64() - (address.ToInt64() - memInfo.BaseAddress.ToInt64()));
            }
            catch (OverflowException)
            {
                return false;
            }

            if (size <= 0)
            {
                return false;
            }

            return true;
        }

        public static Dictionary<string, string> GetEnvironmentVariablesCore(IntPtr hProcess)
        {
            IntPtr penv = GetPenv(hProcess);

            int dataSize;
            if (!HasReadAccess(hProcess, penv, out dataSize))
            {
                throw new Win32Exception("Unable to read environment block.");
            }

            const int maxEnvSize = 32767;
            if (dataSize > maxEnvSize)
            {
                dataSize = maxEnvSize;
            }

            var envData = new byte[dataSize];
            var res_len = IntPtr.Zero;
            bool b = ProcessNativeMethods.ReadProcessMemory(
                hProcess,
                penv,
                envData,
                new IntPtr(dataSize),
                ref res_len);

            if (!b || (int)res_len != dataSize)
            {
                throw new Win32Exception("Unable to read environment block data.");
            }

            return EnvToDictionary(envData);
        }

        private static Dictionary<string, string> EnvToDictionary(byte[] env)
        {
            var result = new Dictionary<string, string>();

            int len = env.Length;
            if (len < 4)
            {
                return result;
            }

            int n = len - 3;
            for (int i = 0; i < n; ++i)
            {
                byte c1 = env[i];
                byte c2 = env[i + 1];
                byte c3 = env[i + 2];
                byte c4 = env[i + 3];

                if (c1 == 0 && c2 == 0 && c3 == 0 && c4 == 0)
                {
                    len = i + 3;
                    break;
                }
            }

            // envs are key=value pair separated by '\0'
            var envs = Encoding.Unicode.GetString(env, 0, len).Split('\0');
            var separators = new[] { '=' };
            for (int i = 0; i < envs.Length; i++)
            {
                var pair = envs[i].Split(separators, 2);
                if (pair.Length != 2)
                {
                    continue;
                }
                result[pair[0]] = pair[1];
            }

            return result;
        }
    }
}
