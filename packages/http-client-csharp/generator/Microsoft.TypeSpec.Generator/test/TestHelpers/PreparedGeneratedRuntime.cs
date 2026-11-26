// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.TestHelpers
{
    internal static class PreparedGeneratedRuntime
    {
        // Execute actual emitted IL, independently of operation fingerprints and
        // the downstream simplifier. Each fixture gets a collectible assembly.
        internal static object?[] Invoke(CSharpCompilation compilation, string typeName, string methodName,
            params object?[][] arguments)
        {
            using var image = new MemoryStream();
            var emitted = compilation.WithAssemblyName("PreparedRuntime_" + Guid.NewGuid().ToString("N")).Emit(image);
            Assert.That(emitted.Success, Is.True,
                string.Join(Environment.NewLine, emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var context = new AssemblyLoadContext("PreparedRuntime_" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var assembly = context.LoadFromStream(image);
                var type = assembly.GetType(typeName, throwOnError: true)!;
                var method = type.GetMethod(methodName)!;
                var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                return arguments.Select(values => method.Invoke(instance, values)).ToArray();
            }
            finally
            {
                context.Unload();
            }
        }
    }
}
