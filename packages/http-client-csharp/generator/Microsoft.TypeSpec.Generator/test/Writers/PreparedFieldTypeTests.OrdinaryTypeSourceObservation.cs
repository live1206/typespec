// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    internal partial class PreparedFieldTypeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PocDirectLocalTypeSourceContextIsPreparedBeforeWriting(bool isUsing)
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: isUsing);
            var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
            var body = new MethodBodyStatements(new MethodBodyStatement[] { assignment.Terminate(), new ExpressionStatement(new KeywordExpression("return", new LiteralExpression(null))) });
            var method = owner.Methods.Single();
            method.Update(bodyStatements: body);
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            var observation = PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence);
            Assert.That(observation, Is.Not.Null);
            Assert.That(observation!.IsCurrent, Is.True);
            Assert.That(observation.Occurrence, Is.SameAs(occurrence));
            var started = typeof(PreparedSourceFile).GetField("_started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.That(started.GetValue(file), Is.EqualTo(false));
            method.Update(bodyStatements: new MethodBodyStatements(Array.Empty<MethodBodyStatement>()));
            Assert.That(observation.IsCurrent, Is.False);
            method.Update(bodyStatements: body);
            Assert.That(observation.IsCurrent, Is.False, "Observed source staleness remains sticky after restoration.");
        }

        [Test]
        public void PocLocalTypeInsideAnUnprovedEnclosingCallIsNotDeclaredUnobserved()
        {
            using var host = new BodyOnlyPhysicalHost();
            var (owner, _, _) = CreateBodyOnlyHostOwner();
            var declaration = new DeclarationExpression(typeof(System.IO.MemoryStream), "seed", isUsing: true);
            var assignment = new AssignmentExpression(declaration, new KeywordExpression("default", null));
            var lambda = new FuncExpression(Array.Empty<CodeWriterDeclaration?>(),
                new MethodBodyStatements(new MethodBodyStatement[] { assignment.Terminate() }));
            var call = new InvokeMethodExpression(null, "Capture", new ValueExpression[] { lambda });
            owner.Methods.Single().Update(bodyStatements: new MethodBodyStatements(new MethodBodyStatement[] { call.Terminate() }));
            using var prepared = PreparedFieldTypes.CreateForGeneration(host.Generator, new TypeProvider[] { owner }, _ => true);
            Assert.That(prepared, Is.Not.Null);
            var file = prepared!.SourceFiles.Single();
            var occurrence = file.Roots.SelectMany(root => root.DescendantsAndSelf()).Single(node => ReferenceEquals(node.Producer, declaration));
            Assert.That(PreparedOrdinaryTypeSourceObservation.TryCapture(file, occurrence), Is.Null,
                "A captured or otherwise unproved enclosing call cannot inherit the no-enclosing-call POC rule.");
        }
    }
}
