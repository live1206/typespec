// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
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
        public void PocSupplementalParameterDoesNotReplayCustomCulture(bool materialized)
        {
            var previous = CultureInfo.CurrentCulture;
            var culture = new PocParameterTextInfoProbe();
            try
            {
                CultureInfo.CurrentCulture = culture;
                var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
                if (materialized)
                {
                    _ = (VariableExpression)parameter;
                    Assert.That(culture.Calls, Is.GreaterThan(0), "The native lazy normalizer must actually dispatch the probe.");
                    var beforeWrite = culture.Calls;
                    var owner = new TestTypeProvider("PocCultureOwner");
                    var signature = new MethodSignature("Probe", null, MethodSignatureModifiers.Public, null, null, [parameter]);
                    using var writer = new CodeWriter();
                    writer.WriteMethod(new MethodProvider(signature, new MethodBodyStatements([]), owner));
                    Assert.That(writer.ToString(false), Does.Contain("unused"));
                    Assert.That(culture.Calls, Is.EqualTo(beforeWrite), "Native writing reuses the already-materialized variable.");
                }
                var beforeCapture = culture.Calls;
                var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, disableXmlDocs: true, new PocWorkBudget(4096));
                Assert.That(culture.Calls, Is.EqualTo(beforeCapture), "POC capture must not replay a virtual culture getter.");
                Assert.That(shape.Opaque, Is.True);
                Assert.That(parameter.CachedVariableExpression is not null, Is.EqualTo(materialized));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void PocSupplementalParameterKeepsBuiltInCulturePositive()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var parameter = new ParameterProvider("unused", $"parameter", typeof(int));
                _ = (VariableExpression)parameter;
                var shape = StructuredSourceShape.CaptureForPocSupplement(parameter, disableXmlDocs: true, new PocWorkBudget(4096));
                Assert.That(shape.Opaque, Is.False);
                Assert.That(shape.IntrinsicTokens, Is.True);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
