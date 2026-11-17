// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Reflection;

namespace Microsoft.TypeSpec.Generator.Tests.TestHelpers
{
    // These fixtures replace process-wide state. Capture the nullable backing
    // field rather than forcing Instance to be initialized before the test.
    internal sealed class GeneratorGlobalStateSnapshot : IDisposable
    {
        private static readonly FieldInfo Instance = typeof(CodeModelGenerator).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly PropertyInfo Policy = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling), BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _instance = Instance.GetValue(null);
        private readonly object? _policy = Policy.GetValue(null);

        public void Dispose()
        {
            Instance.SetValue(null, _instance);
            Policy.SetValue(null, _policy);
        }
    }
}
