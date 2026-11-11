// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator
{
    public class OutputLibrary
    {
        private volatile IReadOnlyList<TypeProvider>? _typeProviders;
        private readonly object _typeProviderSnapshotSync = new();
        private long _typeProviderRevision;

        public IReadOnlyList<TypeProvider> TypeProviders
        {
            get
            {
                var providers = _typeProviders;
                if (providers is null)
                {
                    // Preserve the original lazy/reentrant behavior: never hold
                    // the witness lock while calling the virtual builder, and
                    // retain its result even if the builder set a value itself.
                    providers = BuildTypeProviders();
                    StoreTypeProviders(providers);
                }
                return providers!;
            }
            internal set => StoreTypeProviders(value);
        }

        private void StoreTypeProviders(IReadOnlyList<TypeProvider>? providers)
        {
            lock (_typeProviderSnapshotSync)
            {
                _typeProviders = providers;
                if (_typeProviderRevision != long.MaxValue) { _typeProviderRevision++; }
            }
        }

        // A non-forcing reference witness only. Mutable elements and complete
        // group/source/currentness remain separate proof obligations.
        internal bool TryCaptureMaterializedTypeProviders(out IReadOnlyList<TypeProvider>? providers, out long revision)
        {
            lock (_typeProviderSnapshotSync)
            {
                providers = _typeProviders;
                revision = _typeProviderRevision;
                return providers is not null && revision != long.MaxValue;
            }
        }

        internal bool IsMaterializedTypeProviderSnapshotCurrent(IReadOnlyList<TypeProvider> providers, long revision)
        {
            lock (_typeProviderSnapshotSync)
            {
                return revision != long.MaxValue && _typeProviderRevision == revision && ReferenceEquals(_typeProviders, providers);
            }
        }

        internal Lazy<ModelFactoryProvider> ModelFactory { get; } = new(() => CodeModelGenerator.Instance.TypeFactory.CreateModelFactory(CodeModelGenerator.Instance.InputLibrary.InputNamespace.Models));

        private static TypeProvider[] BuildEnums()
        {
            var input = CodeModelGenerator.Instance.InputLibrary.InputNamespace;
            var enums = new List<TypeProvider>(input.Enums.Count);
            foreach (var inputEnum in input.Enums)
            {
                if (inputEnum.Usage.HasFlag(InputModelTypeUsage.ApiVersionEnum))
                {
                    continue;
                }

                var outputEnum = CodeModelGenerator.Instance.TypeFactory.CreateEnum(inputEnum);

                // If there is a custom code view for a fixed enum, then we should not emit the generated enum as the custom code will have
                // the implementation. We will still need to emit the serialization code.
                if (outputEnum is FixedEnumProvider { CustomCodeView: { IsEnum: true, Type: { IsValueType: true, IsStruct: false } } })
                {
                    enums.AddRange(outputEnum.SerializationProviders);
                }
                else if (outputEnum != null)
                {
                    enums.Add(outputEnum);
                }
            }

            return [.. enums];
        }

        private static TypeProvider[] BuildModels()
        {
            var input = CodeModelGenerator.Instance.InputLibrary.InputNamespace;
            var models = new List<TypeProvider>(input.Models.Count);
            foreach (var inputModel in input.Models)
            {
                var outputModel = CodeModelGenerator.Instance.TypeFactory.CreateModel(inputModel);
                if (outputModel != null && outputModel is not SystemObjectModelProvider)
                {
                    models.Add(outputModel);
                    var unknownVariant = inputModel.DiscriminatedSubtypes.Values.FirstOrDefault(m => m.IsUnknownDiscriminatorModel);
                    if (unknownVariant != null)
                    {
                        var unknownModel = CodeModelGenerator.Instance.TypeFactory.CreateModel(unknownVariant);
                        if (unknownModel != null)
                        {
                            models.Add(unknownModel);
                        }
                    }
                }
            }

            return [.. models];
        }

        protected virtual TypeProvider[] BuildTypeProviders()
        {
            return [
                .. BuildEnums(),
                .. BuildModels(),
                new ChangeTrackingListDefinition(),
                new ChangeTrackingDictionaryDefinition(),
                new ArgumentDefinition(),
                new OptionalDefinition(),
                .. BuildModelFactory(),
                .. CodeModelGenerator.Instance.CustomCodeAttributeProviders
            ];
        }

        private TypeProvider[] BuildModelFactory()
        {
            var modelFactory = ModelFactory.Value;
            return modelFactory.Methods.Count > 0 ? [modelFactory] : [];
        }
    }
}
