// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

namespace Microsoft.TypeSpec.Generator.Input
{
    // Internal plumbing between the built-in generator assemblies. Endpoints
    // are keyed weakly by exact producer type, never replaced, and may be absent;
    // this bridge does not inspect arguments or authorize name changes.
    internal static class ConstructedArgumentBridge
    {
        internal const int MaximumArguments = 4096;
        internal sealed record Endpoint(Func<bool> Enabled, Action<object, object[]> Record, Action<Type> RegisterWrapperType);
        private static readonly ConditionalWeakTable<Type, Endpoint> _endpoints = new();

        internal static void Install(Type producerType, Endpoint endpoint) => _endpoints.GetValue(producerType, _ => endpoint);
        internal static bool Enabled(object invocation) => _endpoints.TryGetValue(invocation.GetType(), out var endpoint) && endpoint.Enabled();
        internal static void Record(object invocation, object[] arguments)
        {
            if (_endpoints.TryGetValue(invocation.GetType(), out var endpoint)) { endpoint.Record(invocation, arguments); }
        }
        internal static void RegisterWrapperType(object invocation, Type wrapperType)
        {
            if (_endpoints.TryGetValue(invocation.GetType(), out var endpoint)) { endpoint.RegisterWrapperType(wrapperType); }
        }
    }
}
