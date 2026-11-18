// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Task05 first checkpoint: finite structured grouping under the existing
// opt-in pre-emission policy. Unsupported profiles retain native spelling.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Primitives;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        private bool _pocBinaryWriteSetComplete;
        private readonly Dictionary<SourceOccurrenceId, (PreparedSourceFile File, PreparedSourceOccurrence Node, Func<bool> Envelope, Func<bool> FinalData)> _pocGrouping = [];
        private readonly Dictionary<(Guid Generation, string File), (PreparedSourceFile File, HashSet<SourceOccurrenceId> Used)> _pocUsedByFile = [];
        internal int PocGroupingCount => _pocGrouping.Count;
        internal int PocGroupingUses { get; private set; }
        internal Action? PocAfterOpening { get; set; } // instrumentation, not authority
        internal Action<object>? PocNativeWriteObserver { get; set; }

        internal static PreparedFieldTypes CreatePocGrouping(TypeProvider[] providers, Compilation customization,
            Func<TypeProvider, bool> shouldWrite) => CreateCore(providers, customization, shouldWrite, null, null, null, true);

        private void CapturePocGrouping()
        {
            var files = SourceFiles;
            // Live paths were checked earlier, but an observed cache can change
            // before these descriptors are captured. Ambiguous new source IDs
            // cannot authorize either file; never replay their path getters.
            var ambiguousDescriptors = files.GroupBy(file => (file.Generation, file.FilePath))
                .Where(group => group.Skip(1).Any()).Select(group => group.Key).ToHashSet();
            bool binaryCatalogKnown = _pocBinaryWriteSetComplete && _callLookupScopes.All(scope => scope.HasCapturedNestedTypes);
            foreach (var file in files)
            {
                var binaryMethods = new HashSet<MethodProvider>(ReferenceEqualityComparer.Instance);
                var binarySignatures = new HashSet<MethodSignature>(ReferenceEqualityComparer.Instance);
                var binaryNodes = new HashSet<SourceOccurrenceId>();
                var choices = new List<PreparedSourceOccurrence>();
                if (binaryCatalogKnown)
                { CaptureBinaryParentRoles(file, binaryMethods, binarySignatures, binaryNodes, choices); }
                var envelope = CapturePocEnvelope(file, binaryMethods, out var finalData);
                var nodes = file.Roots.Where(r => r.Id.RootRole != "type-documentation").SelectMany(r => r.DescendantsAndSelf()).ToArray();
                if (envelope is null || finalData is null || nodes.Any(n => !PocKnown(n, binarySignatures, binaryNodes)))
                { continue; }
                // Compatibility is the OLD whole-file admission, not merely the
                // shape of one equality inside a newly admitted arithmetic file.
                bool legacyFile = nodes.All(n => PocKnown(n, [], []));
                if (!legacyFile && ambiguousDescriptors.Contains((file.Generation, file.FilePath)))
                { continue; }
                foreach (var root in file.Roots)
                {
                    if (root.Producer is not MethodProvider method || !PocSignature(method.Signature))
                    { continue; }
                    var binary = method.BodyExpression as BinaryOperatorExpression;
                    if (method.BodyStatements is ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: BinaryOperatorExpression returned } })
                    { binary = returned; }
                    if (binary is not { Operator: "==", AddParentheses: true, Left: VariableExpression left, Right: VariableExpression right })
                    { continue; }
                    var matches = root.DescendantsAndSelf().Where(n => ReferenceEquals(n.Producer, binary)).ToArray();
                    if (matches.Length != 1)
                    { continue; }
                    var node = matches[0];
                    if (PocType(node.ResolveParameterType(left), typeof(int)) && PocType(node.ResolveParameterType(right), typeof(int)))
                    { choices.Add(node); }
                }
                if (choices.Count == 0)
                { continue; }
                if (!legacyFile)
                {
                    file.UsesNativeWrittenIdentity = true;
                    _nativePocFiles.Add(file);
                }
                foreach (var choice in choices)
                { _pocGrouping.Add(choice.Id, (file, choice, envelope, finalData)); }
            }
        }

        private static void CaptureBinaryParentRoles(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method ||
                    !PreparedBinaryParentRoles.TryGetNewRoot(method, (object?)method.BodyStatements ?? method.BodyExpression!, out var binary) ||
                    !PocSignature(method.Signature, true))
                { continue; }
                var roots = methodRoot.DescendantsAndSelf().Where(node => ReferenceEquals(node.Producer, binary)).ToArray();
                if (roots.Length != 1)
                { continue; }
                var known = new List<PreparedSourceOccurrence>();
                var omitted = new List<PreparedSourceOccurrence>();
                bool Visit(PreparedSourceOccurrence node, BinaryOperatorExpression? parent, bool left)
                {
                    if (node.IsOpaque)
                    { return false; }
                    if (node.Producer is VariableExpression variable)
                    {
                        return variable.GetType() == typeof(VariableExpression) && node.Children.IsEmpty &&
                            PocType(node.ResolveParameterType(variable), typeof(int));
                    }
                    if (node.Producer is not BinaryOperatorExpression current || node.Children.Length != 2 ||
                        !ReferenceEquals(node.Children[0].Producer, current.Left) ||
                        !ReferenceEquals(node.Children[1].Producer, current.Right))
                    { return false; }
                    known.Add(node);
                    if (parent is null ? current.Operator == "==" : PreparedBinaryParentRoles.OmitChild(parent.Operator, current.Operator, left))
                    {
                        omitted.Add(node);
                    }
                    return Visit(node.Children[0], current, true) && Visit(node.Children[1], current, false);
                }
                if (!Visit(roots[0], null, false))
                { continue; }
                methods.Add(method);
                signatures.Add(method.Signature);
                foreach (var node in known)
                { nodes.Add(node.Id); }
                choices.AddRange(omitted);
            }
        }

        private Func<bool>? CapturePocEnvelope(PreparedSourceFile file, HashSet<MethodProvider> binaryMethods, out Func<bool>? finalData)
        {
            finalData = null;
            var owner = file.FileOwner;
            var generator = CodeModelGenerator.Instance;
            var factory = PreparedGeneratedMethodGroup.AuditedTypeFactory(generator);
            var fileWarnings = PreparedNativeFileWarningFacts.TryCapture(owner);
            if (fileWarnings is null || !_callEmissionKnown || !file.DisableXmlDocs || factory is null ||
                factory.CSharpTypeMap is not Dictionary<CSharpType, TypeProvider?> map || map.Count != 0 ||
                !PreparedSourceFile.HasCommentOnlyLicenseHeader(generator.LicenseHeader) ||
                !DefaultEmissionMethod(owner, "BuildImplements") || !NativeHeader(owner, fileWarnings))
            { return null; }
            var checks = new List<Func<bool>>();
            foreach (var root in file.Roots)
            {
                if (root.Producer is MethodProvider method)
                {
                    if (method.GetType() != typeof(MethodProvider) ||
                        !PocSignature(method.Signature) && !(binaryMethods.Contains(method) && PocSignature(method.Signature, true)))
                    { return null; }
                    var suppressions = method.Suppressions;
                    if (!ContractCollections.TryCopy(suppressions, out var ownedSuppressions) ||
                        ownedSuppressions.Any(s => s is not null && s.Justification is null))
                    { return null; }
                    var suppression = PreparedSuppressionEnvelope.TryCapture(suppressions);
                    if (suppression is null)
                    { return null; }
                    checks.Add(() => ReferenceEquals(method.Suppressions, suppressions) && suppression.IsCurrent);
                }
                else if (root.Producer is PropertyProvider property)
                {
                    var projection = PocSeedProjection.TryCapture(property, owner);
                    if (projection is null || !_pocSeeds.TryAdd(property, projection))
                    { return null; }
                    checks.Add(() => projection.IsCurrent);
                }
                else if (root.Id.RootRole != "type-documentation")
                { return null; }
            }
            bool PrefixCurrent() => ReferenceEquals(CodeModelGenerator.Instance, generator) &&
                ReferenceEquals(PreparedGeneratedMethodGroup.AuditedTypeFactory(generator), factory) && map.Count == 0 &&
                NativeHeader(owner, fileWarnings);
            // Prefix and checks read only audited native caches/projections. The
            // final predicate deliberately does not call the host/policy walk.
            finalData = () => PrefixCurrent() &&
                (file.UsesNativeWrittenIdentity ? NativePocDataIsCurrent(file) : CallEmissionDataIsCurrentWithoutCallbacks()) && checks.All(check => check());
            return () => PrefixCurrent() &&
                (file.UsesNativeWrittenIdentity ? ObserveNativePocSource(file) : CallEnvironmentIsCurrent()) && checks.All(check => check());
        }

        private static bool NativeHeader(TypeProvider owner, PreparedNativeFileWarningFacts fileWarnings) =>
            PreparedGeneratedTypeHeader.HasKnownNativeEmissionHeader(owner, fileWarnings) &&
            owner.CachedType is { } type && Identifier(type.Name);

        private static bool PocSignature(MethodSignatureBase signature, bool arithmeticReturn = false)
        {
            if (signature is not MethodSignature method || method.NonDocumentComment is not null || !Identifier(method.Name) ||
                !(PocType(method.ReturnType, typeof(bool)) || arithmeticReturn && PocType(method.ReturnType, typeof(int))) ||
                !ContractCollections.TryCopy(method.Attributes, out var attributes) || !attributes.IsEmpty || method.ExplicitInterface is not null ||
                method.GenericArguments is not null || method.GenericParameterConstraints is not null ||
                !ContractCollections.TryCopy(method.Parameters, out var parameters))
            { return false; }
            const MethodSignatureModifiers allowed = MethodSignatureModifiers.Public | MethodSignatureModifiers.Private | MethodSignatureModifiers.Static;
            return (method.Modifiers & ~allowed) == 0 && parameters.All(p => p is not null && p.GetType() == typeof(ParameterProvider) &&
                Identifier(p.Name) && PocType(p.Type, typeof(int)) &&
                ContractCollections.TryCopy(p.Attributes, out var parameterAttributes) && parameterAttributes.IsEmpty && p.DefaultValue is null &&
                !p.IsRef && !p.IsOut && !p.IsIn && !p.IsParams && p.Field is null && p.Property is null && p.InputParameter is null);
        }

        private static bool Identifier(string text) => text.Length > 0 && (text[0] == '_' || char.IsAsciiLetter(text[0])) &&
            text.All(c => c == '_' || char.IsAsciiLetterOrDigit(c)) && SyntaxFacts.GetKeywordKind(text) == SyntaxKind.None;

        private static bool PocType(CSharpType? type, Type expected) => type?.GetType() == typeof(CSharpType) &&
            type.IsFrameworkType && type.FrameworkType.GetType() == typeof(int).GetType() && ReferenceEquals(type.FrameworkType, expected) &&
            type.IsValueType && type.IsStruct && !type.IsNullable && type.DeclaringType is null &&
            type.Name == expected.Name && type.Namespace == expected.Namespace &&
            ContractCollections.TryCopy(type.Arguments, out var arguments) && arguments.IsEmpty;

        private bool PocKnown(PreparedSourceOccurrence node, HashSet<MethodSignature> binarySignatures, HashSet<SourceOccurrenceId> binaryNodes)
        {
            if (node.IsOpaque)
            { return false; }
            return node.Producer switch
            {
                BinaryOperatorExpression binary => binaryNodes.Contains(node.Id) ||
                    binary.Operator == "==" && binary.Left?.GetType() == typeof(VariableExpression) && binary.Right?.GetType() == typeof(VariableExpression),
                KeywordExpression keyword => keyword.GetType() == typeof(KeywordExpression) && keyword.Keyword is "return" or "default",
                VariableExpression variable => variable.GetType() == typeof(VariableExpression),
                MethodProvider method => method.GetType() == typeof(MethodProvider),
                MethodSignature signature => PocSignature(signature) || binarySignatures.Contains(signature) && PocSignature(signature, true),
                ParameterProvider parameter => parameter.GetType() == typeof(ParameterProvider),
                PropertyProvider property => _pocSeeds.TryGetValue(property, out var projection) && projection.IsCurrent,
                ExpressionPropertyBody body => body.GetType() == typeof(ExpressionPropertyBody),
                ExpressionStatement statement => statement.GetType() == typeof(ExpressionStatement),
                _ => ReferenceEquals(node.Producer, ValueExpression.Empty)
            };
        }

        internal bool TryUsePocGrouping(PreparedSourceFile file, PreparedSourceOccurrence node, BinaryOperatorExpression binary)
        {
            if (!_pocGrouping.TryGetValue(node.Id, out var choice) || !ReferenceEquals(choice.File, file) ||
                !ReferenceEquals(choice.Node, node) || !ReferenceEquals(node.Producer, binary) ||
                !(file.UsesNativeWrittenIdentity ? NativePocUseIsCurrent(file, choice.Envelope, choice.FinalData) :
                    ValidateSnapshot() && file.IsCurrent() && choice.Envelope()))
            { return false; }
            var key = (file.Generation, file.FilePath);
            if (!_pocUsedByFile.TryGetValue(key, out var use))
            {
                use = (file, new HashSet<SourceOccurrenceId>());
                _pocUsedByFile.Add(key, use);
            }
            if (!ReferenceEquals(use.File, file))
            { file.Invalidate(); return false; }
            PocGroupingUses++;
            use.Used.Add(node.Id);
            return true;
        }

        internal bool CanCommitPocGrouping(CodeFile output)
        {
            if (output.PreparedGroupingCount == 0)
            { return true; }
            if (IsNativePocFile(output))
            { return CanCommitNativePocFile(output); }
            ObserveSourceSnapshot();
            if (!CanCommitSharedSnapshot() || !TryMatchPocFileUse(output, out var file, out var envelope, out _))
            { return false; }
            // Every used occurrence was checked above. This retained producer
            // describes the FILE, so evaluate it once here, not once per ID.
            // Its callbackful result is still subject to the later data seal.
            return file.IsCurrent() && envelope();
        }

        // Veto only, after every callbackful permission check. This does not
        // replace those checks or create permission from an unobserved state.
        internal bool SealPocGroupingWithoutCallbacks(CodeFile output)
        {
            if (output.PreparedGroupingCount == 0)
            { return true; }
            if (IsNativePocFile(output))
            { return SealNativePocFile(output); }
            if (_sourceDisposed || System.Threading.Volatile.Read(ref _sharedSnapshotStale) != 0 ||
                !TryMatchPocFileUse(output, out var file, out _, out var finalData))
            { return false; }
            // Every file is inspected by the final data predicate. Establish
            // non-callbackful parameter spelling for ALL captured roots first,
            // before any currentness walk can observe an unwritten formal.
            // Missing cached names veto grouping only; native materialization
            // may make a later check eligible without invalidating shared facts.
            if (!_sourceFiles.Values.All(captured => captured.Roots.All(root => root.HasCachedParameterEmittedStates)))
            { return false; }
            return file.IsCurrent() && finalData();
        }

        private bool TryMatchPocFileUse(CodeFile output, out PreparedSourceFile file,
            out Func<bool> envelope, out Func<bool> finalData)
        {
            file = null!;
            envelope = null!;
            finalData = null!;
            if (output.SourceConsumption is not { WasCurrentAtEndOfWrite: true } receipt || output.Name != receipt.File ||
                !_pocUsedByFile.TryGetValue((receipt.Generation, receipt.File), out var use) ||
                use.File.Generation != receipt.Generation || use.File.FilePath != receipt.File ||
                use.File.Roots.Length != receipt.RootCount || use.Used.Count != output.PreparedGroupingCount || use.Used.Count == 0)
            { return false; }
            Func<bool>? heldEnvelope = null;
            Func<bool>? heldFinalData = null;
            foreach (var id in use.Used)
            {
                if (!_pocGrouping.TryGetValue(id, out var choice) || !ReferenceEquals(choice.File, use.File) ||
                    choice.Node.Id != id || !receipt.Consumed.Contains(id))
                { return false; }
                if (heldEnvelope is null)
                {
                    heldEnvelope = choice.Envelope;
                    heldFinalData = choice.FinalData;
                }
                else if (!ReferenceEquals(heldEnvelope, choice.Envelope) || !ReferenceEquals(heldFinalData, choice.FinalData))
                { return false; }
            }
            file = use.File;
            envelope = heldEnvelope!;
            finalData = heldFinalData!;
            return true;
        }

        private bool CallEmissionDataIsCurrentWithoutCallbacks()
        {
            // Grouping's profile is globally XML-disabled. The captured call
            // emission checks here are native file-warning facts; neither they
            // nor owner/occurrence snapshots call builders, hosts or policies.
            return !_sourceDisposed && !_callEmissionInvalid && _callEmissionKnown && _sourceDisableXmlDocs &&
                _sourceConfiguration is not null && ReferenceEquals(_sourceConfiguration, CodeModelGenerator.Instance.Configuration) &&
                _sourceConfiguration.DisableXmlDocs == _sourceDisableXmlDocs &&
                ContractCollections.SameReferences(_sourceRootList, _sourceRootSnapshot) &&
                (_sourceInputs is null || _sourceModel is not null && ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs)) &&
                IsPolicyEnabled(CodeModelGenerator.Instance) && _callLookupScopes.All(scope => scope.IsCurrent()) &&
                _sourceFiles.Values.All(file => file.IsCurrent()) && _callEmissionChecks.All(check => check());
        }

        internal CodeFile SelectPocGrouping(CodeFile output) =>
            CanCommitPocGrouping(output) && SealPocGroupingWithoutCallbacks(output) ? output : output.WithoutPreparedNames();
    }
}
