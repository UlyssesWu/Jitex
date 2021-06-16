﻿using System;
using System.Reflection;

namespace Jitex.Tests
{
    internal static class Utils
    {
        public static MethodInfo GetMethod<T>(string name)
        {
            return typeof(T).GetMethod(name, (BindingFlags) (-1)).GetBaseDefinition();
        }

        public static MethodInfo GetMethod(Type type, string name)
        {
            return type.GetMethod(name, (BindingFlags)(-1)).GetBaseDefinition();
        }
    }
}