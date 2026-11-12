// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Providers
{
    // Bounded arity-zero field lookup over existing symbols and finalized
    // providers. It does not construct or bind generated syntax.
    internal sealed class PreparedFieldImportLookup
    {
        private readonly Compilation _compilation;
        private readonly IReadOnlySet<string> _generatedNamespaces;
        private readonly IReadOnlyDictionary<(string Namespace, string Name), TypeProvider[]> _generated;
        private readonly Dictionary<TypeProvider, HashSet<string>> _signatureImports = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, HashSet<string>> _competitorNamespaces = new(StringComparer.Ordinal);
        private readonly bool _unsupported;

        internal PreparedFieldImportLookup(Compilation compilation,
            IReadOnlyDictionary<(string Namespace, string Name), TypeProvider[]> generated, IReadOnlySet<string> generatedNamespaces)
        {
            _compilation = compilation;
            _generated = generated;
            _generatedNamespaces = generatedNamespaces;
            _unsupported = compilation.Options.SyntaxTreeOptionsProvider is not null || compilation.SyntaxTrees.Any(tree => tree.GetRoot().DescendantNodes().Any(node =>
                node is ExternAliasDirectiveSyntax || node is UsingDirectiveSyntax directive &&
                    (directive.Alias is not null || directive.StaticKeyword.RawKind != 0 || directive.GlobalKeyword.RawKind != 0))) ||
                compilation.Options.SpecificDiagnosticOptions.TryGetValue("CS8019", out var reporting) && reporting != ReportDiagnostic.Default;
        }

        internal bool TryPrepare(TypeProvider owner, CSharpType target, INamedTypeSymbol? referencedTarget,
            out HashSet<string> forbiddenImports)
        {
            // Preserve the original non-dispatch early rejection before owner.Type.
            if (_unsupported)
            {
                forbiddenImports = new HashSet<string>(StringComparer.Ordinal);
                return false;
            }
            if (!TryPrepareNamespaceLookup(owner.Type.Namespace, target, referencedTarget, out forbiddenImports, out var needsImport))
            {
                return false;
            }
            if (!needsImport)
            {
                return true;
            }
            if (!_signatureImports.TryGetValue(owner, out var imports))
            {
                imports = CaptureSignatureImports(owner);
                _signatureImports.Add(owner, imports);
            }
            return !imports.Overlaps(forbiddenImports);
        }

        // Namespace lookup ONLY. The containing type/method's type parameters,
        // nested types, actual imports and emission policy require separate proof.
        // In particular, do not call the field adapter to authorize a body edit:
        // its signature-import collector can evaluate unaudited formatting inputs.
        internal bool TryPrepareTypeOnlyNamespaceLookup(string ownerNamespace, CSharpType target, INamedTypeSymbol referencedTarget,
            out ImmutableHashSet<string> forbiddenImports)
        {
            var supported = TryPrepareNamespaceLookup(ownerNamespace, target, referencedTarget, out var competitors, out _);
            forbiddenImports = competitors.ToImmutableHashSet(StringComparer.Ordinal);
            return supported;
        }

        internal readonly record struct PocBareNamespaceCandidate(PreparedPocNameForms.Form Form, string Spelling)
        {
            internal PreparedPocNameForms.RootKind Root => Form.Root;
            internal int Start => Form.Start;
        }

        // First POC candidate for a plain runtime type with no aliases: the
        // shortest ordinary suffix is its bare name. This supplies namespace
        // lookup and ranking only; lexical scope, source observation, imports,
        // emission confinement and the writer's authority remain separate.
        internal bool TryPreparePocBareNamespaceCandidate(string ownerNamespace, CSharpType target, INamedTypeSymbol referencedTarget,
            out PocBareNamespaceCandidate candidate, out ImmutableHashSet<string> forbiddenImports)
        {
            candidate = default;
            forbiddenImports = ImmutableHashSet<string>.Empty;
            if (target is null || referencedTarget is null || ownerNamespace is null ||
                target.GetType() != typeof(CSharpType) || !target.IsFrameworkType || target.IsNullable || target.IsLiteral || target.HasUnionMetadata ||
                target.DeclaringType is not null || !ContractCollections.TryCopy(target.Arguments, out var arguments) || !arguments.IsEmpty) { return false; }
            var runtime = target.FrameworkType;
            if (runtime.GetType() != typeof(object).GetType() || runtime.IsArray || runtime.IsGenericType || runtime.IsGenericParameter ||
                runtime.DeclaringType is not null || target.Name != runtime.Name || target.Namespace != runtime.Namespace ||
                target.TryGetCSharpFriendlyName(out _) || runtime.FullName is not { } metadataName ||
                PreparedPocNameForms.RenderIdentifier(target.Name) is not { } spelling || !HasMaterializedPocInventory() ||
                _compilation is not CSharpCompilation csharp || _generated.ContainsKey((target.Namespace, target.Name)) ||
                _generatedNamespaces.Contains(target.Namespace + "." + target.Name) ||
                !SymbolEqualityComparer.Default.Equals(referencedTarget, PreparedFieldTypes.ResolveReferencedTarget(csharp, target, _generated)) ||
                !SymbolEqualityComparer.Default.Equals(referencedTarget, _compilation.GetTypeByMetadataName(metadataName)) || referencedTarget.HasUnsupportedMetadata ||
                referencedTarget.TypeKind == TypeKind.Error || referencedTarget.ContainingAssembly is null ||
                !string.Equals(referencedTarget.ContainingAssembly.Identity.ToString(), runtime.Assembly.FullName, StringComparison.Ordinal) ||
                !TryPrepareTypeOnlyNamespaceLookup(ownerNamespace, target, referencedTarget, out forbiddenImports)) { return false; }
            var partCount = target.Namespace.Length == 0 ? 1 : target.Namespace.Count(character => character == '.') + 2;
            var forms = PreparedPocNameForms.Rank(partCount, ImmutableArray<PreparedPocNameForms.Alias>.Empty);
            if (forms.IsEmpty || forms[0] is not { Root: PreparedPocNameForms.RootKind.Ordinary } first || first.Start != partCount - 1) { return false; }
            candidate = new PocBareNamespaceCandidate(first, spelling);
            return true;
        }

        private bool HasMaterializedPocInventory()
        {
            if (_generated.GetType() != typeof(Dictionary<(string Namespace, string Name), TypeProvider[]>) ||
                _generatedNamespaces.GetType() != typeof(HashSet<string>)) { return false; }
            var generated = (Dictionary<(string Namespace, string Name), TypeProvider[]>)_generated;
            var namespaces = (HashSet<string>)_generatedNamespaces;
            if (!ReferenceEquals(generated.Comparer, EqualityComparer<(string Namespace, string Name)>.Default) ||
                !ReferenceEquals(namespaces.Comparer, StringComparer.Ordinal)) { return false; }
            foreach (var (key, providers) in generated)
            {
                if (providers is null) { return false; }
                foreach (var provider in providers)
                {
                    if (provider?.CachedType is not { } type || type.GetType() != typeof(CSharpType) ||
                        type.Name != key.Name || type.Namespace != key.Namespace || type.DeclaringType is not null ||
                        !ContractCollections.TryCopy(type.Arguments, out _)) { return false; }
                }
            }
            return true;
        }

        private bool TryPrepareNamespaceLookup(string ownerNamespace, CSharpType target, INamedTypeSymbol? referencedTarget,
            out HashSet<string> forbiddenImports, out bool needsImport)
        {
            needsImport = false;
            forbiddenImports = new HashSet<string>(StringComparer.Ordinal);
            if (_unsupported)
            {
                return false;
            }

            // Namespace members outrank normal imports. Include ancestor and
            // global namespaces, not only the declaration's immediate namespace.
            while (true)
            {
                var nestedNamespace = ownerNamespace.Length == 0 ? target.Name : ownerNamespace + "." + target.Name;
                if (_generatedNamespaces.Contains(nestedNamespace))
                {
                    return false;
                }
                var symbolNamespace = FindNamespace(ownerNamespace);
                if (_generated.TryGetValue((ownerNamespace, target.Name), out var generated) && generated.Any(provider => provider.Type.Arguments.Count == 0))
                {
                    return referencedTarget is null && generated.Any(provider => ReferenceEquals(provider.Type, target)) &&
                        ownerNamespace == target.Namespace;
                }
                var members = symbolNamespace?.GetMembers(target.Name)
                    .Where(member => member is INamespaceSymbol || member is INamedTypeSymbol { Arity: 0 }).ToArray() ?? [];
                if (members.Length != 0)
                {
                    return members.Length == 1 && referencedTarget is not null &&
                        SymbolEqualityComparer.Default.Equals(members[0], referencedTarget);
                }
                if (ownerNamespace.Length == 0)
                {
                    break;
                }
                var dot = ownerNamespace.LastIndexOf('.');
                ownerNamespace = dot < 0 ? string.Empty : ownerNamespace[..dot];
            }

            needsImport = true;
            if (!_competitorNamespaces.TryGetValue(target.Name, out var namespaces))
            {
                namespaces = new HashSet<string>(StringComparer.Ordinal);
                VisitNamespace(_compilation.GlobalNamespace);
                foreach (var (key, declarations) in _generated)
                {
                    if (key.Name == target.Name && declarations.Any(provider => provider.Type.Arguments.Count == 0))
                    {
                        namespaces.Add(key.Namespace);
                    }
                }
                _competitorNamespaces.Add(target.Name, namespaces);

                void VisitNamespace(INamespaceSymbol current)
                {
                    if (current.GetTypeMembers(target.Name, 0).Any(type => _compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly)))
                    {
                        namespaces.Add(current.IsGlobalNamespace ? string.Empty : current.ToDisplayString());
                    }
                    foreach (var child in current.GetNamespaceMembers())
                    {
                        VisitNamespace(child);
                    }
                }
            }
            forbiddenImports.UnionWith(namespaces.Where(ns => ns != target.Namespace));
            return true;
        }

        private INamespaceSymbol? FindNamespace(string name)
        {
            var current = _compilation.GlobalNamespace;
            if (name.Length == 0)
            {
                return current;
            }
            foreach (var part in name.Split('.'))
            {
                var next = current.GetNamespaceMembers().FirstOrDefault(ns => ns.Name == part);
                if (next is null)
                {
                    return null;
                }
                current = next;
            }
            return current;
        }

        private static HashSet<string> CaptureSignatureImports(TypeProvider owner)
        {
            var imports = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<CSharpType>(ReferenceEqualityComparer.Instance);
            Visit(owner);
            imports.Remove(owner.Type.Namespace);
            return imports;

            void AddType(CSharpType? type)
            {
                if (type is null || !visited.Add(type))
                {
                    return;
                }
                if (type.IsArray)
                {
                    AddType(type.ElementType);
                }
                else if (!type.TryGetCSharpFriendlyName(out _))
                {
                    imports.Add(type.Namespace);
                }
                foreach (var argument in type.Arguments)
                {
                    AddType(argument);
                }
            }

            void Visit(TypeProvider provider)
            {
                AddType(provider.Type.BaseType);
                foreach (var implemented in provider.Implements)
                {
                    AddType(implemented);
                }
                foreach (var field in provider.Fields)
                {
                    AddType(field.Type);
                }
                foreach (var property in provider.Properties)
                {
                    AddType(property.Type);
                }
                foreach (var method in provider.Methods)
                {
                    AddType(method.Signature.ReturnType);
                    foreach (var parameter in method.Signature.Parameters)
                    {
                        AddType(parameter.Type);
                    }
                }
                foreach (var constructor in provider.Constructors)
                {
                    foreach (var parameter in constructor.Signature.Parameters)
                    {
                        AddType(parameter.Type);
                    }
                }
                foreach (var nested in provider.NestedTypes)
                {
                    Visit(nested);
                }
            }
        }
    }
}
