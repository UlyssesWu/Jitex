﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jitex.JIT;
using LocalVariableInfo = Jitex.Builder.LocalVariableInfo;
using MethodBody = Jitex.Builder.MethodBody;

namespace Testing
{
    public struct Teste
    {
        public int idade;

        public Teste(int num)
        {
            idade = new Random().Next();
        }
    }

    class Program
    {
        private static void Main()
        {
            ManagedJit managedJit = ManagedJit.GetInstance();
            managedJit.OnPreCompile = OnPreCompile;
            var result = ReSomar();
            Console.WriteLine(result);
            Console.ReadKey();
        }

        private static ReplaceInfo OnPreCompile(MethodBase method)
        {
            MethodInfo Somar = typeof(Program).GetMethod("ReSomar");

            if (Somar.MetadataToken == method.MetadataToken)
            {
                MethodInfo ReplaceSomar = typeof(Program).GetMethod("ReplaceSomar");
                byte[] il = ReplaceSomar.GetMethodBody().GetILAsByteArray();
                List<LocalVariableInfo> variables = ReplaceSomar.GetMethodBody().LocalVariables.Select(lv => new LocalVariableInfo(lv.LocalType)).ToList();
                return new ReplaceInfo(new MethodBody(il, variables, typeof(Program).Module));
            }

            return null;
        }

        public static float ReSomar()
        {
            Teste t = new Teste();
            float b = 10;
            float c = 10;
            return b + c;
        }

        public static float ReplaceSomar()
        {
            Teste t = new Teste();
            float b = 10;
            float c = 10;
            return b + c;
        }
    }
}