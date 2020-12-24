﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NativeLibraryLoader;
using System.Linq;

namespace Jitex.Runtime
{
    internal sealed class NETCore : RuntimeFramework
    {
        private delegate IntPtr GetJitDelegate();

        public NETCore() : base(true)
        {
        }

        protected override IntPtr GetJitAddress()
        {
            string jitLibraryName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                jitLibraryName = "clrjit.dll";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                jitLibraryName = "libclrjit.so";
            else
                jitLibraryName = "libclrjit.dylib";
            
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (module.ModuleName.Contains("clr") || module.ModuleName.Contains("jit"))
                {
                    Console.WriteLine(module.ModuleName);
                }
            }
            
            LibraryLoader? defaultLoader = LibraryLoader.GetPlatformDefaultLoader();

            IntPtr libAddress = defaultLoader.LoadNativeLibrary(jitLibraryName);
            IntPtr getJitAddress = defaultLoader.LoadFunctionPointer(libAddress, "getJit");

            GetJitDelegate getJit = Marshal.GetDelegateForFunctionPointer<GetJitDelegate>(getJitAddress);
            IntPtr jitAddress = getJit();
            return jitAddress;
        }
    }
}