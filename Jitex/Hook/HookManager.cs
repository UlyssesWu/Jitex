﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static Jitex.Utils.WinApi;

namespace Jitex.Hook
{
    internal sealed class HookManager
    {
        private readonly IList<VTableHook> _hooks = new List<VTableHook>();

        public void InjectHook(IntPtr pointerAddress, Delegate delToInject)
        {
            IntPtr originalAdress = Marshal.ReadIntPtr(pointerAddress);
            IntPtr hookAddress = Marshal.GetFunctionPointerForDelegate(delToInject);
            VTableHook hook = new VTableHook(delToInject, originalAdress, pointerAddress);
            WritePointer(pointerAddress, hookAddress);
            _hooks.Add(hook);
        }

        public bool RemoveHook(Delegate del)
        {
            var hookFound = _hooks.FirstOrDefault(h => h.Delegate.Method.Equals(del.Method));

            if (hookFound == null)
                return false;

            return RemoveHook(hookFound);
        }

        private bool RemoveHook(VTableHook hook)
        {
            WritePointer(hook.Address, hook.OriginalAddress);
            _hooks.Remove(hook);
            return true;
        }

        private void WritePointer(IntPtr address, IntPtr pointer)
        {
            VirtualProtect(address, new IntPtr(IntPtr.Size), MemoryProtection.ReadWrite, out var oldFlags);
            Marshal.WriteIntPtr(address, pointer);
            VirtualProtect(address, new IntPtr(IntPtr.Size), oldFlags, out _);
        }
    }
}