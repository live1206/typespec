// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.Tests.Writers
{
    public class Task04BaselineIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OptionalNativeEffectDeclinesMalformedVariablePartsWithoutChangingNativeFailure(bool missingVariable)
        {
            ValueExpression malformed = missingVariable
                ? new AssignmentExpression(new DeclarationExpression((VariableExpression)null!), new KeywordExpression("default", null))
                : new VariableExpression(typeof(int), (CodeWriterDeclaration)null!);
            var before = WriteNative(malformed);
            Assert.That(before.Error, Is.Not.Null, "The original native writer must reach its existing malformed-input failure.");
            PreparedPocNativeCallEffect? effect = null;
            Assert.DoesNotThrow(() => effect = missingVariable
                ? PreparedPocNativeCallEffect.TryCaptureStatement(new ExpressionStatement(malformed), false, new PocWorkBudget(65536), out _)
                : PreparedPocNativeCallEffect.TryCapture(malformed, false, new PocWorkBudget(65536), out _),
                "An optional prewriter observation must decline malformed variable data, not move the native exception earlier.");
            Assert.That(effect, Is.Null);
            var after = WriteNative(malformed);
            Assert.That(after.Error, Is.Not.Null);
            Assert.That(after.Error!.GetType(), Is.EqualTo(before.Error!.GetType()));
            Assert.That(after.Error.Message, Is.EqualTo(before.Error.Message));
            Assert.That(after.Text, Is.EqualTo(before.Text));
        }

        [Test]
        public void OptionalNativeEffectPreservesNativeVariableOutputWhenNonEmittingTypeIsMissing()
        {
            var malformed = new VariableExpression((CSharpType)null!, "value");
            var before = WriteNative(malformed);
            Assert.That(before.Error, Is.Null);
            Assert.That(before.Text, Is.EqualTo("value"));
            Assert.DoesNotThrow(() => PreparedPocNativeCallEffect.TryCapture(malformed, false, new PocWorkBudget(65536), out _));
            var after = WriteNative(malformed);
            Assert.That(after.Error, Is.Null);
            Assert.That(after.Text, Is.EqualTo(before.Text));
        }

        private static (Exception? Error, string Text) WriteNative(ValueExpression value)
        {
            using var writer = new CodeWriter();
            Exception? error = null;
            try { value.Write(writer); }
            catch (Exception exception) { error = exception; }
            return (error, writer.ToString(false));
        }
    }
}
