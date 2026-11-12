// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Utilities;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    public class TypeProviderWriter
    {
        protected readonly TypeProvider _provider;
        // Ordinary callers retain their existing ability to call Write repeatedly.
        // A materialized-header pass, however, cannot be replayed after its callback.
        private int _writeMode;
        private readonly object _headerSessionSync = new();
        private PreparedFieldTypes? _preparedFieldTypes;

        internal PreparedFieldTypes? PreparedFieldTypes
        {
            get => Volatile.Read(ref _preparedFieldTypes);
            set
            {
                lock (_headerSessionSync)
                {
                    if (Volatile.Read(ref _writeMode) == 2 && !ReferenceEquals(_preparedFieldTypes, value))
                    {
                        throw new InvalidOperationException("The preparation session cannot change during a materialized-header pass.");
                    }
                    _preparedFieldTypes = value;
                }
            }
        }

        // Explicit generation-scoped header ownership survives a null or
        // disposed name plan. Nested writers do not inherit this physical-file lease.
        internal PreparedPocHeaderFinalization? PreparedPocHeaders { get; set; }

        // Nested writers call WriteType on their parent's buffer without
        // entering Write/WriteWithMaterializedHeader. A borrowed name plan
        // must not turn that nested emission into a physical-file lease.
        internal bool HasPhysicalWriteEntry => Volatile.Read(ref _writeMode) != 0;

        internal bool OwnsPreparation(PreparedFieldTypes preparation, TypeProvider owner) =>
            GetType() == typeof(TypeProviderWriter) && ReferenceEquals(PreparedFieldTypes, preparation) && ReferenceEquals(_provider, owner);

        public TypeProviderWriter(TypeProvider provider)
        {
            _provider = provider;
        }

        public virtual CodeFile Write()
        {
            if (Interlocked.CompareExchange(ref _writeMode, 1, 0) == 2)
            {
                throw new InvalidOperationException("A materialized-header writer cannot be replayed.");
            }
            return WriteCore(null);
        }

        // The callback receives canonical objects, not the private source buffer.
        // Admission and related-file closure must be established by the caller;
        // this boundary alone grants no permission to change emitted names.
        internal CodeFile WriteWithMaterializedHeader(Action<TypeProvider, bool, XmlDocProvider?> prepare)
        {
            ArgumentNullException.ThrowIfNull(prepare);
            if (GetType() != typeof(TypeProviderWriter))
            {
                throw new InvalidOperationException("Materialized-header preparation requires an unused standard writer.");
            }
            lock (_headerSessionSync)
            {
                if (Interlocked.CompareExchange(ref _writeMode, 2, 0) != 0)
                {
                    throw new InvalidOperationException("Materialized-header preparation requires an unused standard writer.");
                }
            }
            return WriteCore(prepare);
        }

        private CodeFile WriteCore(Action<TypeProvider, bool, XmlDocProvider?>? prepare)
        {
            using var writer = new CodeWriter();
            if (PreparedFieldTypes is not null && GetType() == typeof(TypeProviderWriter))
            {
                writer.EnableQualifiedFallback(PreparedFieldTypes, _provider);
                writer.AttachSourceFile(PreparedFieldTypes, _provider, this);
            }
            using var pocHeaderScope = PreparedFieldTypes is { } activePreparation && OwnsPreparation(activePreparation, _provider)
                ? activePreparation.EnterPocHeaderWriter(_provider) : null;
            PreparedFieldTypes?.ObserveSnapshot();
            using var containment = writer.SuppressWarnings(GetExperimentalContextSuppressions(), emitDirectives: false);

            var disabledFileWarnings = _provider.DisabledFileWarnings;
            if (PreparedFieldTypes is { } preparation && OwnsPreparation(preparation, _provider))
            { preparation.ObservePocFileWarnings(_provider, disabledFileWarnings); }
            using (writer.SuppressWarnings(disabledFileWarnings))
            {
                using (var ns = writer.SetNamespace(_provider.Type.Namespace))
                {
                    PreparedFieldTypes?.ObserveSnapshot();
                    WriteType(writer, prepare);
                }
            }
            var content = writer.ToString();
            return new CodeFile(content, _provider.RelativeFilePath, writer.QualifiedContent, writer.CompleteSourceFile(), writer.NameAlternativesSnapshot);
        }

        private bool IsPublicContext(TypeProvider provider)
        {
            return provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Public) &&
                (provider.DeclaringTypeProvider is null || IsPublicContext(provider.DeclaringTypeProvider));
        }

        private void WriteType(CodeWriter writer, Action<TypeProvider, bool, XmlDocProvider?>? prepare = null)
        {
            using var containment = writer.SuppressWarnings(GetExperimentalContextSuppressions(), emitDirectives: false);
            using (writer.BeginSourceRoot(_provider, "type-documentation", 0))
            {
                var writeDocs = _provider.PreserveTypeXmlDocs || _provider.ShouldWriteTypeXmlDocs || IsPublicContext(_provider);
                var docs = writeDocs ? _provider.XmlDocs : null;
                if (PreparedFieldTypes is { } preparation && OwnsPreparation(preparation, _provider))
                { preparation.ObservePocFileDocumentation(_provider, writeDocs, docs); }
                prepare?.Invoke(_provider, writeDocs, docs);
                if (writeDocs)
                {
                    writer.WriteXmlDocsNoScope(docs);
                }
            }
            int attributeIndex = 0;
            var headerOwner = PreparedPocHeaders ?? PreparedFieldTypes?.FinalizedPocHeaders;
            var canonicalAttributes = headerOwner?.CanConsumeCanonicalAttributes(_provider, this) == true;
            foreach (var attribute in canonicalAttributes ? _provider.GetCanonicalAttributesForPocWrite() : _provider.GetAttributesForWrite())
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "type-attribute", attributeIndex++);
                attribute.Write(writer);
                if (attribute is AttributeStatement)
                {
                    writer.WriteLine();
                }
            }
            PreparedFieldTypes?.ObserveSnapshot();
            writer.WriteTypeModifiers(_provider.DeclarationModifiers); // class, struct, enum and interface is written as modifiers in this part
            writer.Append($"{_provider.Type:D}")
                .AppendRawIf(" : ", _provider.Type.BaseType != null || _provider.Implements.Any())
                .AppendIf($"{_provider.Type.BaseType}", _provider.Type.BaseType != null);

            writer.AppendRawIf(", ", _provider.Type.BaseType != null && _provider.Implements.Count > 0);

            for (int i = 0; i < _provider.Implements.Count; i++)
            {
                writer.Append($"{_provider.Implements[i]:D}");
                if (i < _provider.Implements.Count - 1)
                {
                    writer.AppendRaw(", ");
                }
            }

            var hasConstraint = _provider.WhereClause is not null;
            if (PreparedFieldTypes is { } constraintPreparation && OwnsPreparation(constraintPreparation, _provider))
            { constraintPreparation.ObservePocTypeConstraint(_provider, this, _provider.CachedWhereClause); }
            if (hasConstraint)
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "type-constraint", 0);
                using (writer.ScopeRaw(string.Empty, string.Empty, false))
                {
                    _provider.WhereClause!.Write(writer);
                }
            }

            writer.WriteLine();

            if (_provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Enum))
            {
                WriteEnumContent(writer);
            }
            else if (_provider.DeclarationModifiers.HasFlag(TypeSignatureModifiers.Interface))
            {
                WriteInterfaceContent(writer);
            }
            else
            {
                WriteClassOrStructContent(writer);
            }
        }

        private SuppressionStatement[] GetExperimentalContextSuppressions()
        {
            var provider = _provider.SerializationProviderOwner is { } owner && owner.Type.Equals(_provider.Type)
                ? owner
                : _provider;
            return ExperimentalApiHelpers.GetAttributeSuppressions(
                provider.Attributes.Concat(provider.CustomCodeView?.Attributes ?? []));
        }

        private void WriteClassOrStructContent(CodeWriter writer)
        {
            using (writer.Scope())
            {
                bool sectionWritten = _provider.Fields.Any();
                WriteFields(writer);

                if (sectionWritten && _provider.Constructors.Any())
                {
                    writer.WriteLine();
                }

                WriteConstructors(writer);
                sectionWritten |= _provider.Constructors.Any();

                if (sectionWritten && _provider.Properties.Any())
                {
                    writer.WriteLine();
                }

                WriteProperties(writer);
                sectionWritten |= _provider.Properties.Any();

                if (sectionWritten && _provider.Methods.Any())
                {
                    writer.WriteLine();
                }

                WriteMethods(writer);
                sectionWritten |= _provider.Methods.Any();

                if (sectionWritten = _provider.NestedTypes.Any())
                {
                    writer.WriteLine();
                }

                WriteNestedTypes(writer);
            }
        }

        private void WriteEnumContent(CodeWriter writer)
        {
            using (writer.Scope())
            {
                for (int i = 0; i < _provider.Fields.Count; i++)
                {
                    using var occurrence = writer.BeginSourceRoot(_provider, "field", i);
                    using var warnings = writer.SuppressWarnings(writer.ObserveSourceRootProducer(_provider.Fields[i]).Suppressions);
                    writer.WriteXmlDocsNoScope(writer.ObserveSourceRootProducer(_provider.Fields[i]).XmlDocs);
                    foreach (var attr in writer.ObserveSourceRootProducer(_provider.Fields[i]).Attributes)
                    {
                        attr.Write(writer);
                        writer.WriteLine();
                    }
                    writer.Append($"{writer.ObserveSourceRootProducer(_provider.Fields[i]).Name}");
                    if (writer.ObserveSourceRootProducer(_provider.Fields[i]).InitializationValue != null)
                    {
                        writer.AppendRaw(" = ");
                        writer.ObserveSourceRootProducer(_provider.Fields[i]).InitializationValue!.Write(writer);
                    }
                    if (i < _provider.Fields.Count - 1)
                    {
                        writer.WriteRawLine(",");
                    }
                }
                writer.WriteLine();
            }
        }

        private void WriteInterfaceContent(CodeWriter writer)
        {
            using (writer.Scope())
            {
                // temporarily do nothing until we have a requirement for writing interfaces: https://github.com/microsoft/typespec/issues/3442
            }
        }

        private void WriteProperties(CodeWriter writer)
        {
            for (int i = 0; i < _provider.Properties.Count; i++)
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "property", i);
                var property = writer.ObserveSourceRootProducer(_provider.Properties[i]);
                writer.WriteProperty(property);
                if (i < _provider.Properties.Count - 1)
                {
                    writer.WriteLine();
                }
            }
        }

        private void WriteFields(CodeWriter writer)
        {
            for (int i = 0; i < _provider.Fields.Count; i++)
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "field", i);
                var field = writer.ObserveSourceRootProducer(_provider.Fields[i]);
                writer.WriteField(field, GetType() == typeof(TypeProviderWriter) ? PreparedFieldTypes : null, _provider);
            }
        }

        private void WriteConstructors(CodeWriter writer)
        {
            for (int i = 0; i < _provider.Constructors.Count; i++)
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "constructor", i);
                writer.WriteConstructor(writer.ObserveSourceRootProducer(_provider.Constructors[i]));
                if (i < _provider.Constructors.Count - 1)
                {
                    writer.WriteLine();
                }
            }
        }

        private void WriteMethods(CodeWriter writer)
        {
            if (_provider is ModelFactoryProvider { PreserveLeadingMethodSeparator: true } && _provider.Methods.Count > 0)
            {
                writer.WriteLine();
            }

            for (int i = 0; i < _provider.Methods.Count; i++)
            {
                using var occurrence = writer.BeginSourceRoot(_provider, "method", i);
                writer.WriteMethod(writer.ObserveSourceRootProducer(_provider.Methods[i]));
                if (i < _provider.Methods.Count - 1)
                {
                    writer.WriteLine();
                }
            }
        }

        private void WriteNestedTypes(CodeWriter writer)
        {
            PreparedFieldTypes?.ObserveSnapshot();
            for (int i = 0; i < _provider.NestedTypes.Count; i++)
            {
                PreparedFieldTypes?.ObserveSnapshot();
                var nestedWriter = new TypeProviderWriter(_provider.NestedTypes[i])
                {
                    PreparedFieldTypes = PreparedFieldTypes
                };
                using (writer.SuppressWarnings(_provider.NestedTypes[i].DisabledFileWarnings))
                {
                    nestedWriter.WriteType(writer);
                }
                if (i < _provider.NestedTypes.Count - 1)
                {
                    writer.WriteLine();
                }
            }
        }
    }
}
