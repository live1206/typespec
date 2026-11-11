// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.ClientModel.Providers;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.SourceInput;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Tests.Common;
using NUnit.Framework;

namespace Microsoft.TypeSpec.Generator.ClientModel.Tests.ReferenceMap
{
    public class ClientBodyDependencyReferenceMapTests
    {
        [TearDown]
        public void TearDown()
        {
            ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
        }

        [Test]
        public async Task PublicOperationBodyParameterModelIsRetained()
        {
            var requestModel = InputFactory.Model("RequestBody");
            var parameter = InputFactory.BodyParameter("body", requestModel, isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [parameter], httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [requestModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["RequestBody"]);
        }

        [Test]
        public async Task PublicProtocolOnlyOperationBodyParameterModelIsRetained()
        {
            var requestModel = InputFactory.Model("RequestBody");
            var parameter = InputFactory.BodyParameter("body", requestModel, isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [parameter], httpMethod: "POST", generateConvenienceMethod: false);
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [requestModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["RequestBody"]);
        }

        [Test]
        public async Task OperationResponseBodyModelRemainsPublicAsRootOutputModel()
        {
            var responseModel = InputFactory.Model("ResponseBody");
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(InputPrimitiveType.String, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertPublicModels([responseModel], [client], ["ResponseBody"]);
        }

        [TestCase("ErrorResponse", true, false)]
        [TestCase("ErrorResponse", true, true)]
        [TestCase("ServiceErrorResponse", true, false)]
        [TestCase("ServiceErrorResponse", true, true)]
        [TestCase("ErrorResult", false, false)]
        [TestCase("ErrorResult", false, true)]
        public async Task ErrorResultHelperDoesNotKeepServiceModel(string modelName, bool isError, bool referenced)
        {
            var model = InputFactory.Model(modelName, @namespace: "Sample", access: null!,
                usage: InputModelTypeUsage.Json | (isError ? InputModelTypeUsage.Error : InputModelTypeUsage.None));
            var responseModel = InputFactory.Model("ResponseBody",
                properties: referenced ? [InputFactory.Property("Details", model)] : []);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod("Get", operation,
                response: InputFactory.ServiceMethodResponse(responseModel, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [responseModel, model],
                clients: [client],
                customFiles: [],
                expectedFiles: [Path.Combine("src", "Generated", "Internal", "ErrorResult.cs")],
                publicModelNames: referenced ? ["ResponseBody", modelName] : ["ResponseBody"],
                assertProviders: (session, providers) =>
                {
                    var modelProvider = CodeModelGenerator.Instance.TypeFactory.CreateModel(model)!;
                    Assert.AreEqual(modelName, modelProvider.Name);
                    Assert.AreEqual(referenced, session.ShouldWriteProvider(modelProvider));
                    foreach (var serialization in modelProvider.SerializationProviders)
                    {
                        Assert.AreEqual(referenced, session.ShouldWriteProvider(serialization));
                    }

                    var helper = providers.OfType<ErrorResultDefinition>().Single();
                    Assert.AreEqual(modelProvider.Type.Namespace, helper.Type.Namespace);
                    Assert.AreEqual(1, helper.Type.Arguments.Count);
                    Assert.IsTrue(helper.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Internal));
                    Assert.IsFalse(helper.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public));

                    var factory = providers.OfType<ModelFactoryProvider>().Single();
                    Assert.AreEqual(referenced, factory.Methods.Any(m => m.Signature.ReturnType?.Equals(modelProvider.Type) == true));

                    var context = providers.OfType<ModelReaderWriterContextDefinition>().Single();
                    var buildableTypes = context.GetAttributesForWrite().OfType<AttributeStatement>()
                        .SelectMany(a => a.Arguments).OfType<TypeOfExpression>().Select(e => e.Type);
                    Assert.AreEqual(referenced, buildableTypes.Contains(modelProvider.Type));
                });
        }

        [Test]
        public void FinalizedRetentionFixtureRestoresBothGeneratorSingletonsAndPolicy()
        {
            var scmField = typeof(ScmCodeModelGenerator).GetField("_instance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var policyProperty = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            CodeModelGenerator? original = null;
            try { original = CodeModelGenerator.Instance; }
            catch (InvalidOperationException) { }
            var originalScm = scmField.GetValue(null);
            var originalPolicy = Configuration.UnreferencedTypesHandling;
            var directory = Path.Combine(Path.GetTempPath(), "typespec-context-retention-prior-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var configuration = Configuration.Load(directory,
                    "{\"package-name\":\"PriorSample\",\"unreferenced-types-handling\":\"keepAll\"}");
                var prior = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                CodeModelGenerator.Instance = prior;
                Assert.That(ReferenceEquals(ScmCodeModelGenerator.Instance, prior), Is.True);
                Assert.That(Configuration.UnreferencedTypesHandling, Is.EqualTo(Configuration.UnreferencedTypesHandlingOption.KeepAll));
                FinalizedContextFallsBackForRetainedCustomAndRemovedModels();
                Assert.Multiple(() =>
                {
                    Assert.That(ReferenceEquals(CodeModelGenerator.Instance, prior), Is.True);
                    Assert.That(ReferenceEquals(ScmCodeModelGenerator.Instance, prior), Is.True,
                        "The separate SCM singleton must not retain the fixture generator with a deleted output directory.");
                    Assert.That(Configuration.UnreferencedTypesHandling, Is.EqualTo(Configuration.UnreferencedTypesHandlingOption.KeepAll));
                });
            }
            finally
            {
                CodeModelGenerator.Instance = original!;
                scmField.SetValue(null, originalScm);
                policyProperty.SetValue(null, originalPolicy);
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void FinalizedContextFallsBackForRetainedCustomAndRemovedModels()
        {
            (string Content, string[] BuildableTypes) Run(bool finalized)
            {
                CodeModelGenerator? previous = null;
                try { previous = CodeModelGenerator.Instance; }
                catch (InvalidOperationException) { } // Isolated execution may start with no generator.
                var scmField = typeof(ScmCodeModelGenerator).GetField("_instance",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var previousScm = scmField.GetValue(null);
                var policyProperty = typeof(Configuration).GetProperty(nameof(Configuration.UnreferencedTypesHandling),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var previousPolicy = Configuration.UnreferencedTypesHandling;
                var directory = Path.Combine(Path.GetTempPath(), "typespec-context-retention-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PreparedFieldTypes", "ClientStructureDefault.json"),
                        Path.Combine(directory, "tspCodeModel.json"));
                    var configuration = Configuration.Load(directory,
                        "{\"enable-pre-emission-simplification\":true,\"package-name\":\"Sample\",\"unreferenced-types-handling\":\"removeOrInternalize\"}");
                    var generator = new ScmCodeModelGenerator(new GeneratorContext(configuration));
                    CodeModelGenerator.Instance = generator;
                    ((CodeModelGenerator)generator).Configure();
                    // The general custom-source helper also injects generated
                    // support files containing #if directives, which correctly
                    // veto this preparation profile. This fixture needs only its
                    // actual custom partial, not those unrelated generated files.
                    var compilation = CSharpCompilation.Create("ContextRetention",
                        syntaxTrees: [CSharpSyntaxTree.ParseText("namespace Sample.Models { public partial class CustomizedModel { public ContextDependency Dependency { get; set; } } }", path: "CustomizedModel.cs")],
                        references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }
                            .Concat(generator.AdditionalMetadataReferences),
                        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                    Assert.That(compilation.SyntaxTrees.Count(), Is.EqualTo(1));
                    // Controlled complete-input fixture, not a production host certificate.
                    var snapshot = new CompleteSourceInputSnapshot(directory, "context-retention", compilation, ["Debug|net10.0"], () => true);
                    generator.SourceInputModel = new SourceInputModel(compilation, null) { PreEmissionSourceInputs = snapshot };
                    Assert.That(snapshot.IsCurrentFor(directory), Is.True);
                    var retainedInput = InputFactory.Model("CustomizedModel", access: null!);
                    var dependencyInput = InputFactory.Model("ContextDependency", access: null!);
                    var removedInput = InputFactory.Model("UnusedContextModel", access: null!, usage: InputModelTypeUsage.Json);
                    var retained = generator.TypeFactory.CreateModel(retainedInput)!;
                    var dependency = generator.TypeFactory.CreateModel(dependencyInput)!;
                    var removed = generator.TypeFactory.CreateModel(removedInput)!;
                    var output = generator.OutputLibrary;
                    output.TypeProviders = output.TypeProviders.Concat(new TypeProvider[] { retained, dependency, removed }).ToArray();
                    foreach (var provider in output.TypeProviders) { provider.EnsureBuilt(); }
                    foreach (var visitor in generator.Visitors) { visitor.VisitLibrary(output); }
                    CSharpGen.FilterAllCustomizedMembers(output);
                    foreach (var provider in output.TypeProviders) { provider.ProcessTypeForBackCompatibility(); }
                    var roots = output.TypeProviders;
                    using var map = ProviderReferenceMapAnalyzer.PrepareForGenerationWithRootWitness(output, roots);
                    Assert.That(retained.CustomCodeView, Is.Not.Null, "The retained root must actually bind its pre-analysis custom partial.");
                    Assert.That(map.ShouldWriteProvider(retained), Is.True);
                    Assert.That(map.ShouldWriteProvider(dependency), Is.True, "The custom partial's generated-model dependency must be retained.");
                    Assert.That(map.ShouldWriteProvider(removed), Is.False);
                    var context = roots.OfType<ModelReaderWriterContextDefinition>().Single();
                    var beforeCache = context.CachedWriteAttributes;
                    var beforeOrigin = context.PocNativeAttributeCacheOrigin;
                    Assert.That(PreparedFieldTypes.IsPolicyEnabled(generator), Is.True,
                        $"Controlled native preparation policy: enabled={configuration.EnablePreEmissionSimplification}, disableReduce={configuration.DisableRoslynReduce}, rewriters={generator.Rewriters.Count}.");
                    Assert.That(ReferenceEquals(generator.SourceInputModel.PreEmissionSourceInputs, snapshot), Is.True);
                    Assert.That(generator.SourceInputModel.Customization, Is.Not.Null);
                    Assert.That(snapshot.IsCurrentFor(configuration.OutputDirectory), Is.True);
                    using var preparedHeaders = finalized ? new PreparedPocHeaderFinalization(generator) : null;
                    using var prepared = finalized
                        ? PreparedFieldTypes.CreateForGenerationWithFinalizedPocHeaders(generator, roots, map.ShouldWriteProvider, preparedHeaders!)
                        : PreparedFieldTypes.CreateForGeneration(generator, roots, map.ShouldWriteProvider);
                    Assert.That(prepared, Is.Not.Null);
                    Assert.That(snapshot.IsCurrentFor(directory), Is.True);
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.True);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.True);
                    if (finalized)
                    {
                        Assert.That(prepared!.FinalizedPocHeaders, Is.Not.Null);
                        var header = prepared.FinalizedPocHeaders!.Get(context);
                        Assert.That(header, Is.Not.Null);
                        Assert.That(header!.Attributes, Is.Null, "Model roots are outside the approved context-attribute recipe.");
                    }
                    var writer = generator.GetWriter(context);
                    writer.PreparedFieldTypes = prepared;
                    writer.PreparedPocHeaders = preparedHeaders;
                    var text = writer.Write().Content; // exactly one final native writer invocation
                    Assert.That(ReferenceEquals(context.CachedWriteAttributes, beforeCache), Is.False);
                    Assert.That(ReferenceEquals(context.PocNativeAttributeCacheOrigin, beforeOrigin), Is.False);
                    var buildableTypes = context.CachedWriteAttributes!.OfType<AttributeStatement>()
                        .SelectMany(a => a.Arguments).OfType<TypeOfExpression>()
                        .Select(e => e.Type.FullyQualifiedName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    Assert.That(buildableTypes, Does.Contain(retained.Type.FullyQualifiedName));
                    Assert.That(buildableTypes, Does.Contain(dependency.Type.FullyQualifiedName));
                    Assert.That(buildableTypes, Does.Not.Contain(removed.Type.FullyQualifiedName));
                    Assert.That(text, Does.Contain("CustomizedModel"));
                    Assert.That(text, Does.Contain("ContextDependency"));
                    Assert.That(text, Does.Not.Contain("UnusedContextModel"));
                    return (text, buildableTypes);
                }
                finally
                {
                    ProviderReferenceMapAnalyzer.ResetPreWriteAccessibility();
                    CodeModelGenerator.Instance = previous!;
                    scmField.SetValue(null, previousScm);
                    policyProperty.SetValue(null, previousPolicy);
                    Directory.Delete(directory, recursive: true);
                }
            }

            var native = Run(finalized: false);
            var candidate = Run(finalized: true);
            Assert.That(candidate.Content, Is.EqualTo(native.Content));
            Assert.That(candidate.BuildableTypes, Is.EqualTo(native.BuildableTypes));
        }

        [Test]
        public async Task InternalModelReferencedByPublicModelPropertyIsPublicized()
        {
            var dependencyModel = InputFactory.Model("DependencyModel", access: "internal");
            var responseModel = InputFactory.Model(
                "ResponseBody",
                properties: [InputFactory.Property("Dependency", dependencyModel)]);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(responseModel, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertPublicModels([responseModel, dependencyModel], [client], ["ResponseBody", "DependencyModel"]);
        }

        [Test]
        public async Task InternalModelReferencedByPublicNonRootCollectionPropertyIsPublicized()
        {
            var dependencyModel = InputFactory.Model("DependencyModel", access: "internal");
            var responseModel = InputFactory.Model(
                "ResponseBody",
                properties: [InputFactory.Property("Dependencies", InputFactory.Array(dependencyModel))]);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(responseModel, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [responseModel, dependencyModel],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "Generated", "SampleModelFactory.cs"), """
                        using System.Collections.Generic;
                        using Sample.Models;

                        namespace Sample;

                        public static partial class SampleModelFactory
                        {
                            public static ResponseBody ResponseBody(IEnumerable<DependencyModel> dependencies = default) => null;
                        }
                        """)
                ],
                expectedFiles: [],
                publicModelNames: ["ResponseBody", "DependencyModel"],
                configureGenerator: () =>
                {
                    var responseProvider = CodeModelGenerator.Instance.OutputLibrary.TypeProviders.Single(provider => provider.Name == "ResponseBody");
                    var dependencyProvider = CodeModelGenerator.Instance.OutputLibrary.TypeProviders.Single(provider => provider.Name == "DependencyModel");
                    CodeModelGenerator.Instance.AddTypeToKeep(responseProvider, isRoot: false);
                    CodeModelGenerator.Instance.AddTypeToKeep(dependencyProvider, isRoot: false);
                });
        }

        [Test]
        public async Task PublicInputRootsOverrideCustomInternalBoundary()
        {
            var customInternalModel = InputFactory.Model("CustomInternalModel");
            var publicWrapper = InputFactory.Model(
                "PublicWrapper",
                properties: [InputFactory.Property("CustomInternal", customInternalModel)]);

            await MockHelpers.LoadMockGeneratorAsync(
                inputModels: () => [publicWrapper, customInternalModel],
                configuration: """{ "package-name": "Sample", "disable-xml-docs": true }""");

            var publicWrapperProvider = CodeModelGenerator.Instance.OutputLibrary.TypeProviders.Single(provider => provider.Name == "PublicWrapper");
            var customInternalProvider = CodeModelGenerator.Instance.OutputLibrary.TypeProviders.Single(provider => provider.Name == "CustomInternalModel");
            CodeModelGenerator.Instance.AddTypeToKeep(publicWrapperProvider, isRoot: false);
            CodeModelGenerator.Instance.AddTypeToKeep(customInternalProvider, isRoot: false);

            ProviderReferenceMapAnalyzer.ApplyPreWriteAccessibility(CodeModelGenerator.Instance.OutputLibrary.TypeProviders);

            Assert.That(publicWrapperProvider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public), Is.True, "PublicWrapper should remain public because it is an explicit input root.");
            Assert.That(customInternalProvider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public), Is.True, "Public input roots should preserve their declared accessibility.");
        }

        [Test]
        public async Task PublicOperationResponseBodyModelIsRetained()
        {
            var metadataOnlyModel = InputFactory.Model("MetadataOnlyResponse");
            var operation = InputFactory.Operation(
                "Get",
                responses: [
                    InputFactory.OperationResponse(bodytype: InputPrimitiveType.String),
                    new InputOperationResponse([202], metadataOnlyModel, [], isErrorResponse: false, ["application/json"])
                ]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(InputPrimitiveType.String, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [metadataOnlyModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["MetadataOnlyResult"]);
        }

        [Test]
        public async Task EmptyCustomPartialModelIsKept()
        {
            var customizedModel = InputFactory.Model("CustomizedModel");

            await GenerateAndAssertFiles(
                enums: [],
                models: [customizedModel],
                clients: [],
                customFiles: [(
                    Path.Combine("src", "CustomizedModel.cs"),
                    """
                    namespace Sample.Models
                    {
                        public partial class CustomizedModel
                        {
                        }
                    }
                    """)],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "CustomizedModel.cs"),
                    Path.Combine("src", "Generated", "Models", "CustomizedModel.Serialization.cs")
                ]);
        }

        [Test]
        public async Task InternalAdditionalRootModelIsRetainedAsInternal()
        {
            var metadataOnlyModel = InputFactory.Model("MetadataOnlyResponse", access: "internal");
            var operation = InputFactory.Operation(
                "Get",
                responses: [
                    InputFactory.OperationResponse(bodytype: InputPrimitiveType.String),
                    new InputOperationResponse([202], metadataOnlyModel, [], isErrorResponse: false, ["application/json"])
                ]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(InputPrimitiveType.String, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [metadataOnlyModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyResult.cs"),
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyResult.Serialization.cs")
                ],
                internalModelNames: ["MetadataOnlyResult"],
                configureGenerator: () =>
                {
                    var provider = CodeModelGenerator.Instance.OutputLibrary.TypeProviders.Single(provider => provider.Name == "MetadataOnlyResult");
                    CodeModelGenerator.Instance.AddTypeToKeep(provider);
                });
        }

        [Test]
        public async Task PublicAdditionalRootEnumIsRetained()
        {
            var metadataOnlyEnum = InputFactory.StringEnum(
                "MetadataOnlyResponseKind",
                [("Accepted", "accepted")]);
            var operation = InputFactory.Operation(
                "Get",
                responses: [
                    InputFactory.OperationResponse(bodytype: InputPrimitiveType.String),
                    new InputOperationResponse([202], metadataOnlyEnum, [], isErrorResponse: false, ["application/json"])
                ]);
            var method = InputFactory.BasicServiceMethod(
                "Get",
                operation,
                response: InputFactory.ServiceMethodResponse(InputPrimitiveType.String, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [metadataOnlyEnum],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyResponseKind.cs"),
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyResponseKind.Serialization.cs")
                ]);
        }

        [Test]
        public async Task PublicContentTypeHeaderEnumIsRetained()
        {
            var contentTypeEnum = InputFactory.StringEnum(
                "UpdateSnapshotRequestContentType",
                [
                    ("ApplicationMergePatchJson", "application/merge-patch+json"),
                    ("ApplicationJson", "application/json")
                ]);
            var contentTypeParameter = InputFactory.MethodParameter(
                "contentType",
                InputFactory.Union([contentTypeEnum], "contentType"),
                isRequired: true,
                location: InputRequestLocation.Header,
                serializedName: "Content-Type");
            var operation = InputFactory.Operation(
                "UpdateSnapshot",
                parameters: [contentTypeParameter],
                httpMethod: "PATCH",
                generateConvenienceMethod: false);
            var method = InputFactory.BasicServiceMethod(
                "UpdateSnapshot",
                operation,
                parameters: [
                    InputFactory.MethodParameter("name", InputPrimitiveType.String, isRequired: true, location: InputRequestLocation.Path),
                    contentTypeParameter,
                    InputFactory.MethodParameter("content", InputPrimitiveType.Base64, isRequired: true)
                ]);
            var client = InputFactory.Client("ConfigurationClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [contentTypeEnum],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "UpdateSnapshotRequestContentType.cs"),
                    Path.Combine("src", "Generated", "Models", "UpdateSnapshotRequestContentType.Serialization.cs")
                ],
                configureGenerator: () =>
                    CodeModelGenerator.Instance.TypeFactory.CreateCSharpType(InputFactory.Union([contentTypeEnum], "contentType")));
        }

        [Test]
        public async Task PublicEnumIsRetainedWhenNotOtherwiseReferenced()
        {
            var metadataOnlyEnum = InputFactory.StringEnum(
                "MetadataOnlyKind",
                [("One", "one")]);

            await GenerateAndAssertFiles(
                enums: [metadataOnlyEnum],
                models: [],
                clients: [],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyKind.cs"),
                    Path.Combine("src", "Generated", "Models", "MetadataOnlyKind.Serialization.cs")
                ]);
        }

        [Test]
        public async Task ContentTypeHeaderEnumReferencedByCustomSuppressionIsKept()
        {
            var contentTypeEnum = InputFactory.StringEnum(
                "PutKeyValueRequestContentType",
                [("ApplicationJson", "application/json")],
                isExtensible: true);
            var contentTypeParameter = InputFactory.HeaderParameter(
                "contentType",
                InputFactory.Union([contentTypeEnum], "contentType"),
                isRequired: true,
                isContentType: true,
                serializedName: "Content-Type");
            var operation = InputFactory.Operation(
                "SetConfigurationSettingInternal",
                parameters: [contentTypeParameter],
                httpMethod: "PUT");
            var method = InputFactory.BasicServiceMethod("SetConfigurationSettingInternal", operation);
            var client = InputFactory.Client("ConfigurationClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [contentTypeEnum],
                models: [],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "PutKeyValueRequestContentType.cs"), """
                        namespace Sample.Models;

                        internal readonly partial struct PutKeyValueRequestContentType
                        {
                            public static PutKeyValueRequestContentType ApplicationJson { get; } = new PutKeyValueRequestContentType("application/json");
                        }
                        """)
                ],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "PutKeyValueRequestContentType.cs")
                ]);
        }

        [Test]
        public async Task ContentTypeHeaderEnumReferencedOnlyByCustomSuppressionAttributeIsKept()
        {
            var contentTypeEnum = InputFactory.StringEnum(
                "CreateSnapshotRequestContentType",
                [("ApplicationJson", "application/json")],
                isExtensible: true);
            var contentTypeParameter = InputFactory.MethodParameter(
                "contentType",
                InputFactory.Union([contentTypeEnum], "contentType"),
                isRequired: true,
                location: InputRequestLocation.Header,
                serializedName: "Content-Type");
            var operation = InputFactory.Operation(
                "CreateSnapshot",
                parameters: [contentTypeParameter],
                httpMethod: "PUT",
                generateConvenienceMethod: false);
            var method = InputFactory.BasicServiceMethod(
                "CreateSnapshot",
                operation,
                parameters: [
                    InputFactory.MethodParameter("name", InputPrimitiveType.String, isRequired: true, location: InputRequestLocation.Path),
                    contentTypeParameter,
                    InputFactory.MethodParameter("content", InputPrimitiveType.Base64, isRequired: true)
                ]);
            var client = InputFactory.Client("ConfigurationClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [contentTypeEnum],
                models: [],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "ConfigurationClient.cs"), """
                        namespace Sample;

                        [Microsoft.TypeSpec.Generator.Customizations.CodeGenType("ConfigurationClient")]
                        [Microsoft.TypeSpec.Generator.Customizations.CodeGenSuppress("CreateSnapshot", typeof(string), typeof(CreateSnapshotRequestContentType))]
                        public partial class ConfigurationClient
                        {
                        }
                        """)
                ],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Models", "CreateSnapshotRequestContentType.cs")
                ]);
        }

        [Test]
        public async Task PublicNestedBodyModelGraphRemainsPublic()
        {
            var nestedModel = InputFactory.Model("NestedToolParameter");
            var toolModel = InputFactory.Model(
                "ToolConfig",
                properties: [InputFactory.Property("Parameter", nestedModel)]);
            var parameter = InputFactory.BodyParameter("tool", toolModel, isRequired: true);
            var operation = InputFactory.Operation("Configure", parameters: [parameter], httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod("Configure", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [toolModel, nestedModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["ToolConfig", "NestedToolParameter"]);
        }

        [Test]
        public async Task PublicNonDiscriminatorDerivedBodyModelRemainsPublic()
        {
            var baseTool = InputFactory.Model("BaseTool");
            var concreteTool = InputFactory.Model(
                "ConcreteTool",
                properties: [InputFactory.Property("Name", InputPrimitiveType.String)],
                baseModel: baseTool);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: baseTool)]);
            var method = InputFactory.BasicServiceMethod("Get", operation, response: InputFactory.ServiceMethodResponse(baseTool, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertMixedModels(
                [baseTool, concreteTool],
                [client],
                publicModelNames: ["BaseTool", "ConcreteTool"],
                internalModelNames: []);
        }

        [Test]
        public async Task PublicModelSignatureDependencyIsPromotedToPublic()
        {
            var internalDependency = InputFactory.Model("InternalDependency", access: "internal");
            var responseModel = InputFactory.Model(
                "ResponseBody",
                properties: [InputFactory.Property("Dependency", internalDependency)]);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod("Get", operation, response: InputFactory.ServiceMethodResponse(responseModel, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertPublicModels([responseModel, internalDependency], [client], ["ResponseBody", "InternalDependency"]);
        }

        [Test]
        public async Task AzureClientPublicMethodSignatureReferencesStayPublic()
        {
            var signatureModel = InputFactory.Model("SignatureModel", @namespace: "Azure.Sample.Models");
            var methodParameter = InputFactory.MethodParameter("signature", signatureModel, isRequired: true);
            var operation = InputFactory.Operation(
                "Create",
                parameters: [InputFactory.BodyParameter("signature", signatureModel, isRequired: true)],
                httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod("Create", operation, parameters: [methodParameter]);
            var client = InputFactory.Client("SampleClient", clientNamespace: "Azure.Sample", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [signatureModel],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["SignatureModel"],
                packageName: "Azure.Sample");
        }

        [Test]
        public async Task BasePreservedDerivedModelTraversesTransitiveDependencies()
        {
            var transitiveDependency = InputFactory.Model("TransitiveDependency");
            var dependency = InputFactory.Model(
                "DerivedDependency",
                properties: [InputFactory.Property("Transitive", transitiveDependency)]);
            var baseModel = InputFactory.Model("BaseResult");
            var derivedModel = InputFactory.Model(
                "DerivedResult",
                properties: [InputFactory.Property("Dependency", dependency)],
                baseModel: baseModel);
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: baseModel)]);
            var method = InputFactory.BasicServiceMethod("Get", operation, response: InputFactory.ServiceMethodResponse(baseModel, []));
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [baseModel, derivedModel, dependency, transitiveDependency],
                clients: [client],
                customFiles: [],
                expectedFiles: [],
                publicModelNames: ["BaseResult", "DerivedResult", "DerivedDependency", "TransitiveDependency"]);
        }

        [Test]
        public async Task PublicCustomCodeArraySignatureReferencesStayPublic()
        {
            var generatedModel = InputFactory.Model("GeneratedModel");

            await GenerateAndAssertFiles(
                enums: [],
                models: [generatedModel],
                clients: [],
                customFiles: [
                    (Path.Combine("src", "PublicCustomApi.cs"), """
                        using Sample.Models;

                        namespace Sample;

                        public partial class PublicCustomApi
                        {
                            public GeneratedModel[] Items { get; } = System.Array.Empty<GeneratedModel>();
                        }
                        """)
                ],
                expectedFiles: [],
                publicModelNames: ["GeneratedModel"]);
        }

        [Test]
        public async Task PublicCustomCodeTypeConstraintsStayPublic()
        {
            var generatedModel = InputFactory.Model("GeneratedModel", access: "internal");

            await GenerateAndAssertFiles(
                enums: [],
                models: [generatedModel],
                clients: [],
                customFiles: [
                    (Path.Combine("src", "PublicCustomApi.cs"), """
                        using Sample.Models;

                        namespace Sample;

                        public partial class PublicCustomApi<T> where T : GeneratedModel
                        {
                        }
                        """)
                ],
                expectedFiles: [],
                publicModelNames: ["GeneratedModel"]);
        }

        [Test]
        public async Task GeneratedRequestHeaderSetDelimitedReferenceKeepsExtensions()
        {
            var header = InputFactory.HeaderParameter("x-ms-custom", InputFactory.Array(InputPrimitiveType.String), isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [header]);
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [Path.Combine("src", "Generated", "Internal", "PipelineRequestHeadersExtensions.cs")]);
        }

        [Test]
        public async Task GeneratedPrefixedDictionaryHeaderKeepsExtensions()
        {
            var header = InputFactory.HeaderParameter(
                "x-ms-meta",
                InputFactory.Dictionary(InputPrimitiveType.String),
                isRequired: true,
                collectionHeaderPrefix: "x-ms-meta-");
            var operation = InputFactory.Operation("Create", parameters: [header]);
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [Path.Combine("src", "Generated", "Internal", "PipelineRequestHeadersExtensions.cs")]);
        }

        [Test]
        public async Task GeneratedScalarHeaderKeepsExtensions()
        {
            var header = InputFactory.HeaderParameter("x-ms-custom", InputPrimitiveType.String, isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [header]);
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [Path.Combine("src", "Generated", "Internal", "PipelineRequestHeadersExtensions.cs")]);
        }

        [Test]
        public async Task BinaryDataBodyParameterKeepsBinaryContentHelpers()
        {
            var parameter = InputFactory.BodyParameter(
                "content",
                InputPrimitiveType.Base64,
                isRequired: true,
                contentTypes: ["application/octet-stream"],
                defaultContentType: "application/octet-stream");
            var operation = InputFactory.Operation("Upload", parameters: [parameter], httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod(
                "Upload",
                operation,
                parameters: [InputFactory.MethodParameter("content", InputPrimitiveType.Base64, isRequired: true)]);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Internal", "BinaryContentHelper.cs"),
                    Path.Combine("src", "Generated", "Internal", "Utf8JsonBinaryContent.cs")
                ]);
        }

        [Test]
        public async Task CollectionBodyParameterKeepsBinaryContentHelpers()
        {
            var parameter = InputFactory.BodyParameter("items", InputFactory.Array(InputPrimitiveType.String), isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [parameter], httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod(
                "Create",
                operation,
                parameters: [InputFactory.MethodParameter("items", InputFactory.Array(InputPrimitiveType.String), isRequired: true)]);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [client],
                customFiles: [],
                expectedFiles: [
                    Path.Combine("src", "Generated", "Internal", "BinaryContentHelper.cs"),
                    Path.Combine("src", "Generated", "Internal", "Utf8JsonBinaryContent.cs")
                ]);
        }

        [Test]
        public async Task CustomOnlyRequestHeaderSetDelimitedReferenceKeepsExtensions()
        {
            await GenerateAndAssertFiles(
                enums: [],
                models: [],
                clients: [],
                customFiles: [
                    (Path.Combine("src", "CustomHeaders.cs"), """
                        using System.ClientModel.Primitives;

                        namespace Sample;

                        public static class CustomHeaders
                        {
                            public static void Add(PipelineRequestHeaders headers, string[] values)
                                => headers.SetDelimited("x-ms-custom", values, ",");
                        }
                        """)
                ],
                expectedFiles: [Path.Combine("src", "Generated", "Internal", "PipelineRequestHeadersExtensions.cs")]);
        }

        [Test]
        public async Task CustomizedEnumSerializationProviderIsKeptWhenModelSerializationUsesEnum()
        {
            var statusEnum = InputFactory.StringEnum(
                "Status",
                [("Succeeded", "succeeded"), ("Failed", "failed")],
                clientNamespace: "Sample");
            var resultModel = InputFactory.Model(
                "OperationResult",
                properties: [InputFactory.Property("Status", statusEnum, isRequired: true)],
                @namespace: "Sample");
            var operation = InputFactory.Operation("Get", responses: [InputFactory.OperationResponse(bodytype: resultModel)]);
            var method = InputFactory.BasicServiceMethod("Get", operation, response: InputFactory.ServiceMethodResponse(resultModel, []));
            var client = InputFactory.Client("TestClient", methods: [method], clientNamespace: "Sample");

            await GenerateAndAssertFiles(
                enums: [statusEnum],
                models: [resultModel],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "Custom", "Status.cs"), """
                        namespace Sample;

                        [CodeGenType("Status")]
                        public enum Status
                        {
                            Succeeded,
                            Failed
                        }
                        """)
                ],
                expectedFiles: [Path.Combine("src", "Generated", "Models", "Status.Serialization.cs")]);
        }

        [Test]
        public async Task PublicBodyOnlyModelRemainsPublicWithCustomModelFactory()
        {
            var requestModel = InputFactory.Model("RequestBody");
            var parameter = InputFactory.BodyParameter("body", requestModel, isRequired: true);
            var operation = InputFactory.Operation("Create", parameters: [parameter], httpMethod: "POST");
            var method = InputFactory.BasicServiceMethod("Create", operation);
            var client = InputFactory.Client("TestClient", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [requestModel],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "SampleModelFactory.cs"), """
                        namespace Sample;

                        [Microsoft.TypeSpec.Generator.Customizations.CodeGenType("SampleModelFactory")]
                        public static partial class SampleModelFactory
                        {
                        }
                        """)
                ],
                expectedFiles: [],
                publicModelNames: ["RequestBody"]);
        }

        [Test]
        public async Task InternalCustomClientPartialOverridesLastContractPublicClient()
        {
            var responseModel = InputFactory.Model("CompactResource");
            var operation = InputFactory.Operation("Compact", responses: [InputFactory.OperationResponse(bodytype: responseModel)]);
            var method = InputFactory.BasicServiceMethod("Compact", operation, response: InputFactory.ServiceMethodResponse(responseModel, []));
            var client = InputFactory.Client("Responses", methods: [method]);

            await GenerateAndAssertFiles(
                enums: [],
                models: [responseModel],
                clients: [client],
                customFiles: [
                    (Path.Combine("src", "Generated", "Responses.cs"), """
                        namespace Sample;

                        public partial class Responses
                        {
                        }
                        """),
                    (Path.Combine("src", "Custom", "Internal", "Responses.cs"), """
                        namespace Sample;

                        internal partial class Responses
                        {
                        }
                        """)
                ],
                expectedFiles: [],
                publicModelNames: ["CompactResource"],
                internalClientNames: ["Responses"]);
        }

        private static async Task GenerateAndAssertPublicModels(
            InputModelType[] models,
            InputClient[] clients,
            string[] modelNames)
            => await GenerateAndAssertModels(models, clients, modelNames, shouldBePublic: true);

        private static async Task GenerateAndAssertMixedModels(
            InputModelType[] models,
            InputClient[] clients,
            string[] publicModelNames,
            string[] internalModelNames)
            => await GenerateAndAssertModels(models, clients, publicModelNames, internalModelNames);

        private static async Task GenerateAndAssertModels(
            InputModelType[] models,
            InputClient[] clients,
            string[] modelNames,
            bool shouldBePublic)
            => await GenerateAndAssertModels(
                models,
                clients,
                shouldBePublic ? modelNames : [],
                shouldBePublic ? [] : modelNames);

        private static async Task GenerateAndAssertModels(
            InputModelType[] models,
            InputClient[] clients,
            string[] publicModelNames,
            string[] internalModelNames)
        {
            await GenerateAndAssertFiles(
                enums: [],
                models: models,
                clients: clients,
                customFiles: [],
                publicModelNames: publicModelNames,
                internalModelNames: internalModelNames,
                expectedFiles: []);
        }

        private static async Task GenerateAndAssertFiles(
            InputEnumType[] enums,
            InputModelType[] models,
            InputClient[] clients,
            (string Path, string Content)[] customFiles,
            string[] expectedFiles,
            string[] unexpectedFiles = null!,
            string[] publicModelNames = null!,
            string[] internalModelNames = null!,
            string[] internalClientNames = null!,
            string packageName = "Sample",
            Action? configureGenerator = null,
            Action<ProviderReferenceMapSession, IReadOnlyList<TypeProvider>>? assertProviders = null)
        {
            publicModelNames ??= [];
            internalModelNames ??= [];
            internalClientNames ??= [];
            unexpectedFiles ??= [];

            // Build the custom-code compilation entirely in memory (no disk writes). Files under the generated
            // folder are excluded to mirror how generation loads custom code, so custom partial declarations
            // participate in the reference-map accessibility analysis.
            var customSources = customFiles
                .Where(f => !f.Path.Replace('\\', '/').StartsWith("src/Generated/", StringComparison.OrdinalIgnoreCase))
                .Select(f => (Name: f.Path, f.Content))
                .ToList();
            Func<Task<Compilation>>? compilation = customSources.Count > 0
                ? () => Helpers.GetCompilationFromSourceFilesAsync(customSources)
                : null;

            await MockHelpers.LoadMockGeneratorAsync(
                inputEnums: () => enums,
                inputModels: () => models,
                clients: () => clients,
                configuration: $$"""{ "package-name": "{{packageName}}", "disable-xml-docs": true }""",
                compilation: compilation);
            configureGenerator?.Invoke();

            // Run the same pre-write pipeline as CSharpGen.ExecuteAsync, then analyze the reference map
            // in memory. This validates the generated types without the flaky Roslyn write/simplify phase
            // (which races on shared compilation metadata) and without touching disk.
            var output = CodeModelGenerator.Instance.OutputLibrary;
            foreach (var type in output.TypeProviders)
            {
                type.EnsureBuilt();
            }
            foreach (var visitor in CodeModelGenerator.Instance.Visitors)
            {
                visitor.VisitLibrary(output);
            }
            CSharpGen.FilterAllCustomizedMembers(output);
            foreach (var outputType in output.TypeProviders)
            {
                outputType.ProcessTypeForBackCompatibility();
            }

            var providers = output.TypeProviders;
            using var session = ProviderReferenceMapAnalyzer.PrepareForGeneration(providers);
            var allProviders = EnumerateAllProviders(providers).ToList();

            foreach (var modelName in publicModelNames)
            {
                var provider = providers.FirstOrDefault(p => p.Name == modelName);
                Assert.That(provider, Is.Not.Null, $"Expected generated type '{modelName}'.");
                Assert.That(session.ShouldWriteProvider(provider!), Is.True, $"{modelName} should be generated.");
                Assert.That(provider!.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public), Is.True, $"{modelName} should be public.");
            }

            foreach (var modelName in internalModelNames)
            {
                var provider = providers.FirstOrDefault(p => p.Name == modelName);
                Assert.That(provider, Is.Not.Null, $"Expected generated type '{modelName}'.");
                Assert.That(session.ShouldWriteProvider(provider!), Is.True, $"{modelName} should be generated.");
                Assert.That(provider!.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Internal), Is.True, $"{modelName} should be internal.");
                Assert.That(provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public), Is.False, $"{modelName} should not be public.");
            }

            foreach (var clientName in internalClientNames)
            {
                var provider = providers.FirstOrDefault(p => p.Name == clientName);
                Assert.That(provider, Is.Not.Null, $"Expected generated client '{clientName}'.");
                Assert.That(provider!.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Internal), Is.True, $"{clientName} should be internal.");
                Assert.That(provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public), Is.False, $"{clientName} should not be public.");
            }

            var modelFactory = providers.OfType<ModelFactoryProvider>().FirstOrDefault();
            if (modelFactory is not null && session.ShouldWriteProvider(modelFactory) && modelFactory.Methods.Count > 0)
            {
                foreach (var modelName in publicModelNames)
                {
                    Assert.That(modelFactory.Methods.Any(m => m.Signature.Name == modelName), Is.True, $"Model factory method for {modelName} should be generated.");
                }

                foreach (var modelName in internalModelNames)
                {
                    Assert.That(modelFactory.Methods.Any(m => m.Signature.Name == modelName), Is.False, $"Model factory method for {modelName} should not be generated.");
                }
            }

            foreach (var expectedFile in expectedFiles)
            {
                AssertProviderWritten(session, allProviders, expectedFile, expected: true);
            }

            foreach (var unexpectedFile in unexpectedFiles)
            {
                AssertProviderWritten(session, allProviders, unexpectedFile, expected: false);
            }
            assertProviders?.Invoke(session, providers);
        }

        private static IEnumerable<TypeProvider> EnumerateAllProviders(IEnumerable<TypeProvider> providers)
        {
            foreach (var provider in providers)
            {
                yield return provider;
                foreach (var serialization in EnumerateAllProviders(provider.SerializationProviders))
                {
                    yield return serialization;
                }
                foreach (var nested in EnumerateAllProviders(provider.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static void AssertProviderWritten(
            ProviderReferenceMapSession session,
            IReadOnlyList<TypeProvider> allProviders,
            string relativeFilePath,
            bool expected)
        {
            var provider = allProviders.FirstOrDefault(p => string.Equals(p.RelativeFilePath, relativeFilePath, StringComparison.OrdinalIgnoreCase));
            if (expected)
            {
                Assert.That(provider, Is.Not.Null, $"Expected generated file '{relativeFilePath}'.");
                Assert.That(session.ShouldWriteProvider(provider!), Is.True, $"Expected generated file '{relativeFilePath}'.");
            }
            else
            {
                Assert.That(provider is null || !session.ShouldWriteProvider(provider), Is.True, $"Did not expect generated file '{relativeFilePath}'.");
            }
        }
    }
}
