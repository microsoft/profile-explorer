// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IRExplorer {
    static class Minidump {
        public enum ExceptionInfo {
            None,
            Present
        }

        [Flags]
        public enum Option : uint {
            Normal = 0x00000000,
            WithDataSegs = 0x00000001,
            WithFullMemory = 0x00000002,
            WithHandleData = 0x00000004,
            FilterMemory = 0x00000008,
            ScanMemory = 0x00000010,
            WithUnloadedModules = 0x00000020,
            WithIndirectlyReferencedMemory = 0x00000040,
            FilterModulePaths = 0x00000080,
            WithProcessThreadData = 0x00000100,
            WithPrivateReadWriteMemory = 0x00000200,
            WithoutOptionalData = 0x00000400,
            WithFullMemoryInfo = 0x00000800,
            WithThreadInfo = 0x00001000,
            WithCodeSegs = 0x00002000,
            WithoutAuxiliaryState = 0x00004000,
            WithFullAuxiliaryState = 0x00008000,
            WithPrivateWriteCopyMemory = 0x00010000,
            IgnoreInaccessibleMemory = 0x00020000,
            ValidTypeFlags = 0x0003ffff
        }

        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump",
                   CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
                   ExactSpelling = true, SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeHandle hFile,
                                                     uint dumpType, ref MiniDumpExceptionInformation expParam,
                                                     IntPtr userStreamParam, IntPtr callbackParam);

        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump",
                   CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode,
                   ExactSpelling = true, SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeHandle hFile,
                                                     uint dumpType, IntPtr expParam, IntPtr userStreamParam,
                                                     IntPtr callbackParam);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId", ExactSpelling = true)]
        private static extern uint GetCurrentThreadId();

        public static bool WriteDump(SafeHandle fileHandle, Option options,
                                     ExceptionInfo exceptionInfo = ExceptionInfo.None) {
            var currentProcess = Process.GetCurrentProcess();
            var currentProcessHandle = currentProcess.Handle;
            uint currentProcessId = (uint)currentProcess.Id;
            MiniDumpExceptionInformation exInfo;
            exInfo.ThreadId = GetCurrentThreadId();
            exInfo.ClientPointers = false;
            exInfo.ExceptionPointers = IntPtr.Zero;

            if (exceptionInfo == ExceptionInfo.Present) {
                exInfo.ExceptionPointers = Marshal.GetExceptionPointers();
            }

            if (exInfo.ExceptionPointers == IntPtr.Zero) {
                return MiniDumpWriteDump(currentProcessHandle, currentProcessId, fileHandle, (uint)options,
                                         IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            else {
                return MiniDumpWriteDump(currentProcessHandle, currentProcessId, fileHandle, (uint)options,
                                         ref exInfo, IntPtr.Zero, IntPtr.Zero);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)] // Pack=4 is important! So it works also for x64!
        public struct MiniDumpExceptionInformation {
            public uint ThreadId;
            public IntPtr ExceptionPointers;
            [MarshalAs(UnmanagedType.Bool)]
            public bool ClientPointers;
        }
    }
}
