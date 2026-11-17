// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [Test]
        public void PocNativeTypeCaptureDoesNotAdditionallyFormatSuccessfulTask03Declaration()
        {
            // Reuse the unchanged original real Task03 host/fixture; no new
            // admission or diagnostic-candidate promotion is introduced here.
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf())
                .Single(node => ReferenceEquals(node.Producer, owner.Seed));
            var permission = prepared.GetTask03TypeOfNamePermission(file, occurrence);
            Assert.That(permission, Is.Not.Null);
            var standard = host.Generator.GetWriter(owner);
            standard.PreparedFieldTypes = prepared;
            using var writer = new CodeWriter();
            writer.EnableQualifiedFallback(prepared, owner);
            writer.AttachSourceFile(prepared, owner, standard);
            Assert.That(writer.TryEnableNativeTypeCapture(owner.Seed, owner.ExceptionType, occurrence.Id,
                new PocWorkBudget(1200), 100), Is.True);
            using (writer.SetNamespace(owner.Type.Namespace))
            {
                // Test-only access to the existing renderer permits an opt-in
                // CodeWriter setup without adding production TypeProviderWriter glue.
                typeof(TypeProviderWriter).GetMethod("WriteType", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(standard, new object?[] { writer, null });
            }
            var content = writer.ToString(false);
            Assert.That(content, Does.Contain("Exception seed"));
            Assert.That(writer.QualifiedContent, Does.Contain("global::System.Exception seed"));
            Assert.That(writer.NativeTypeUse, Is.Null, "A successful prepared use must never enter or replay the native formatter.");
            Assert.That(writer.CompleteSourceFile()!.WasCurrentAtEndOfWrite, Is.True);
            Assert.That(prepared.GetTask03TypeOfNamePermission(file, occurrence), Is.SameAs(permission));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PocNativeTypeCaptureObservesSeparateRegistrationAndEmissionReadsAtNativeImportCallback(bool throws)
        {
            using var host = new BodyOnlyPhysicalHost(customizationSource: OriginalTypeOfSupportSource(false));
            var owner = new OriginalTypeOfHostOwner(false);
            owner.EnsureBuilt();
            FinalizeGeneratedInstanceOwner(owner);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var imports = (Dictionary<TypeProvider, HashSet<string>>)typeof(PreparedFieldTypes)
                .GetField("_bodyImports", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared)!;
            foreach (bool capture in new[] { false, true })
            {
                var type = new CSharpType("Foo", "Registered", false, false, null, Array.Empty<CSharpType>(), true, false);
                var declaration = new DeclarationExpression(type, "value", isUsing: true);
                var error = new InvalidOperationException("native import callback");
                int callbacks = 0;
                // Synthetic native callback seam, NOT compiler/permission proof:
                // the existing ObserveBodyImport HashSet.Add invokes this comparer.
                imports[owner] = new HashSet<string>(new PocNativeCaptureImportComparer(() =>
                {
                    callbacks++;
                    if (throws) { throw error; }
                    type.Namespace = "Emitted";
                }));
                using var writer = new CodeWriter();
                writer.EnableQualifiedFallback(prepared, owner);
                if (capture)
                {
                    var id = new SourceOccurrenceId(Guid.NewGuid(), "Capture.cs", "M", "body", 0, "local");
                    Assert.That(writer.TryEnableNativeTypeCapture(declaration, type, id, new PocWorkBudget(1200), 100), Is.True);
                }
                if (throws)
                {
                    Assert.That(Assert.Throws<InvalidOperationException>(() => declaration.Write(writer)), Is.SameAs(error));
                    Assert.That(writer.ToString(false), Is.EqualTo("using "));
                    Assert.That(writer.NativeTypeUse, Is.Null);
                }
                else
                {
                    declaration.Write(writer);
                    var content = writer.ToString(true);
                    Assert.That(content, Does.Contain("using Registered;\n"));
                    Assert.That(content, Is.EqualTo(writer.QualifiedContent));
                    Assert.That(content, Does.Contain("using global::Emitted.Foo value"));
                    Assert.That(writer.NativeTypeUse is not null, Is.EqualTo(capture));
                    if (capture)
                    {
                        Assert.That(writer.NativeTypeUse!.RegistrationNamespace, Is.EqualTo("Registered"));
                        Assert.That(writer.NativeTypeUse.EmittedNamespace, Is.EqualTo("Emitted"));
                    }
                }
                Assert.That(callbacks, Is.EqualTo(1), "Capture must not replay registration or user comparison.");
            }
        }

        private sealed class PocNativeCaptureImportComparer(Action callback) : StringComparer
        {
            public override int Compare(string? x, string? y) => StringComparer.Ordinal.Compare(x, y);
            public override bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
            public override int GetHashCode(string obj) { callback(); return StringComparer.Ordinal.GetHashCode(obj); }
        }
    }
}
