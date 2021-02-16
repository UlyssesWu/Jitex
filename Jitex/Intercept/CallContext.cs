﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jitex.Utils;
using Jitex.Utils.Extension;

namespace Jitex.Intercept
{
    /// <summary>
    /// Context of call.
    /// </summary>
    /// <remarks>
    /// That should contains all information necessary to continue (or not) with call.
    /// </remarks>
    public class CallContext : IDisposable
    {
        private readonly int _sizeType;
        private readonly Type? _returnType;
        private Parameter? _returnValue;
        private Parameter? _instanceValue;

        /// <summary>
        /// Generic method.
        /// </summary>
        private readonly Parameter? _methodHandle;

        public bool HasInstance => !Method.IsConstructor && !Method.IsStatic;
        public bool HasReturn => _returnType != typeof(void);

        /// <summary>
        /// Method source call.
        /// </summary>
        public MethodBase Method { get; }

        /// <summary>
        /// Delegate to original call.
        /// </summary>
        /// <remarks>
        /// This delegate is a pointer to our unmodified native code of original method.
        /// Original native code has be detoured and cant run "normal" again, so we should continue call in another address.
        /// </remarks>
        private Delegate Call { get; }

        /// <summary>
        /// Instance passed in call (non-static methods).
        /// </summary>
        public object? Instance
        {
            get => HasInstance ? _instanceValue!.Value : _instanceValue;

            set
            {
                if (!HasInstance) throw new ArgumentException($"Method {Method.Name} don't have instance.");

                _instanceValue!.Dispose();

                if (value == null)
                    _instanceValue = new Parameter(IntPtr.Zero, Method.DeclaringType!);
                else
                    _instanceValue = new Parameter(value, Method.DeclaringType!);
            }
        }

        public bool HasParameters => Parameters != null && Parameters.Any();

        /// <summary>
        /// Parameters passed in call.
        /// </summary>
        public Parameters? Parameters { get; }

        /// <summary>
        /// If original call should proceed.
        /// </summary>
        public bool ProceedCall { get; set; } = true;

        public bool IsAwaitable { get; }

        /// <summary>
        /// Return value of call.
        /// </summary>
        public object? ReturnValue
        {
            get => _returnValue?.Value;

            set
            {
                _returnValue?.Dispose();

                if (value != null)
                    _returnValue = new Parameter(ref value, ((MethodInfo)Method).ReturnType);

                ProceedCall = false;
            }
        }

        public IntPtr ReturnAddress
        {
            get
            {
                if (_returnValue == null)
                    return IntPtr.Zero;

                return _returnValue.AddressValue;
            }
        }

        /// <summary>
        /// Raw parameters from call.
        /// </summary>
        /// <remarks>
        /// That include all arguments passed: Instance, Parameters and Generic Arguments.
        /// </remarks>
        public IEnumerable<Parameter> RawParameters
        {
            get
            {
                List<Parameter> rawParameters = new List<Parameter>();

                if (Method.IsGenericMethod)
                    rawParameters.Add(_methodHandle!);

                if (!Method.IsStatic)
                    rawParameters.Add(_instanceValue!);

                if (Parameters != null && Parameters.Any())
                    rawParameters.AddRange(Parameters);

                return rawParameters;
            }
        }

        private object[] ParametersCall => RawParameters.Select(w => w.RealValue).ToArray()!;

        internal CallContext(MethodBase method, Delegate call, in object[] parameters)
        {
            Method = method;
            Call = call;
            IsAwaitable = method.IsAwaitable();

            int startIndex = 0;

            if (Method.IsGenericMethod)
            {
                IntPtr handle = (IntPtr)parameters[startIndex++];
                _methodHandle = new Parameter(handle, typeof(IntPtr), false);
            }

            if (HasInstance)
            {
                IntPtr instanceAddress = (IntPtr)parameters[startIndex++];
                _instanceValue = new Parameter(instanceAddress, Method.DeclaringType!);
            }

            Parameter[] parametersInfo = new Parameter[parameters.Length - startIndex];

            Type[] parametersMethod = Method.GetParameters().Select(w => w.ParameterType).ToArray();

            for (int i = startIndex; i < parameters.Length; i++)
            {
                object parameter = parameters[i];
                Type parameterType = parametersMethod[i - startIndex];

                parametersInfo[i - startIndex] = new Parameter((IntPtr)parameter, parameterType);
            }

            Parameters = new Parameters(parametersInfo);

            if (method is MethodInfo methodInfo)
            {
                _returnType = methodInfo.ReturnType;

                if (_returnType.IsValueType)
                    _sizeType = Marshal.SizeOf(_returnType);
            }

        }

        /// <summary>
        /// Continue original call.
        /// </summary>
        internal void ContinueFlow()
        {
            if (_returnType == typeof(void))
            {
                Call.DynamicInvoke(ParametersCall);
            }
            else
            {
                object returnValue = Call.DynamicInvoke(ParametersCall);

                if (returnValue is IntPtr returnAddress)
                {
                    if (_returnType!.IsValueType)
                    {
                        if (_sizeType <= IntPtr.Size)
                        {
                            IntPtr reference = TypeHelper.GetReferenceFromTypedReference(__makeref(returnValue));
                            IntPtr valueAddress;

                            unsafe
                            {
                                valueAddress = *(IntPtr*)reference;
                                valueAddress += IntPtr.Size;
                            }

                            returnAddress = valueAddress;
                        }
                        else
                        {
                            returnAddress += IntPtr.Size;
                        }
                    }

                    _returnValue = new Parameter(returnAddress, _returnType, isReturnAddress: true)
                    {
                        IsReturnAddress = true
                    };
                }
                else
                {
                    ReturnValue = returnValue;
                }
            }
        }

        /// <summary>
        /// Continue original call.
        /// </summary>
        internal async ValueTask ContinueFlowAsync()
        {
            if (!IsAwaitable)
            {
                ContinueFlow();
                return;
            }

            if (_returnType == typeof(Task))
            {
                await (Task)Call.DynamicInvoke(ParametersCall);
            }
            else
            {
                object returnValue = (await ContinueAsync().ConfigureAwait(false))!;

                if (returnValue is IntPtr returnAddress)
                {
                    _returnValue = new Parameter(returnAddress, _returnType!, isReturnAddress: true)
                    {
                        IsReturnAddress = true
                    };
                }
                else
                {
                    ReturnValue = returnValue;
                }
            }
        }

        public object? Continue()
        {
            if (_returnType == typeof(void))
            {
                Call.DynamicInvoke(ParametersCall);
                ProceedCall = false;
                return null;
            }

            object returnValue = Call.DynamicInvoke(ParametersCall);
            ProceedCall = false;
            return CreateReturnValue(ref returnValue);
        }

        public async Task<object?> ContinueAsync()
        {
            if (!IsAwaitable)
                return Continue();

            Task task = Continue<Task>()!;
            ProceedCall = false;

            if (_returnType!.IsGenericType)
            {
                await task.ConfigureAwait(false);

                Type taskGeneric = typeof(Task<>).MakeGenericType(_returnType.GetGenericArguments().First());
                PropertyInfo getResult = taskGeneric.GetProperty("Result")!;
                return getResult.GetValue(task);
            }

            await task.ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// Continue original call and retrieve result.
        /// </summary>
        /// <typeparam name="TResult">Type expected from result.</typeparam>
        /// <returns>Result from original call (Returns default on void or constructor).</returns>
        public TResult? Continue<TResult>()
        {
            object? returnValue = Continue();

            if (returnValue == null)
                return default;

            return (TResult?)returnValue;
        }

        /// <summary>
        /// Continue original call and retrieve result.
        /// </summary>
        /// <typeparam name="TResult">Type expected from result.</typeparam>
        /// <returns>Result from original call (Returns default on void or constructor).</returns>
        public async ValueTask<TResult?> ContinueAsync<TResult>()
        {
            object? returnValue = await ContinueAsync();

            if (returnValue == null)
                return default;

            return (TResult?)returnValue;
        }

        private object CreateReturnValue(ref object returnValue)
        {
            if (Method is MethodInfo methodInfo)
            {
                Type returnType = methodInfo.ReturnType;

                if (returnType == typeof(void))
                    return default;

                if (returnType.IsValueType)
                    return returnValue;

                IntPtr ptrReturn = (IntPtr)returnValue; //Address of instance/Value is returned.
                IntPtr refReturn;

                if (returnType == typeof(Task) || Marshal.ReadIntPtr(ptrReturn) == returnType.TypeHandle.Value)
                {
                    unsafe
                    {
                        refReturn = (IntPtr)(&ptrReturn); //It's necessary create a reference to address of instance/value.
                    }
                }
                else
                {
                    refReturn = ptrReturn;
                }

                returnValue = TypeHelper.GetObjectFromReference(refReturn);
                return returnValue;
            }

            return default;
        }

        /// <summary>
        /// Disable method to be intercepted again.
        /// </summary>
        public void DisableIntercept()
        {
            JitexManager.DisableIntercept(Method);
        }

        public void Dispose()
        {
            _returnValue?.Dispose();
            _instanceValue?.Dispose();
            _methodHandle?.Dispose();
            Parameters?.Dispose();
        }
    }
}