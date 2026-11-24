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
using Microsoft.TypeSpec.Generator.Snippets;
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
                var nativeMethods = new HashSet<MethodProvider>(ReferenceEqualityComparer.Instance);
                var nativeSignatures = new HashSet<MethodSignature>(ReferenceEqualityComparer.Instance);
                var nativeNodes = new HashSet<SourceOccurrenceId>();
                var choices = new List<PreparedSourceOccurrence>();
                Func<bool>? argumentCurrent = null;
                if (binaryCatalogKnown)
                {
                    CaptureBinaryParentRoles(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    CaptureDirectCasts(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    CapturePrimitiveCastParents(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    CaptureTypedDefaultCasts(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    CaptureIfConditions(file, nativeNodes, choices);
                    CaptureStringCoalescing(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    CaptureUsedLocalInitializers(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    if (files.Length == 1)
                    {
                        argumentCurrent = CaptureOrdinaryArguments(file, nativeMethods, nativeSignatures, nativeNodes, choices) ??
                            CaptureLambdaCast(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                        CaptureMetadataCastReturns(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                        CaptureSourceNumberCasts(file, nativeMethods, nativeSignatures, nativeNodes, choices);
                    }
                }
                var envelope = CapturePocEnvelope(file, nativeMethods, out var finalData);
                if (argumentCurrent is not null && envelope is { } fileEnvelope && finalData is { } fileData)
                {
                    envelope = () => fileEnvelope() && argumentCurrent();
                    finalData = () => fileData() && argumentCurrent();
                }
                var nodes = file.Roots.Where(r => r.Id.RootRole != "type-documentation").SelectMany(r => r.DescendantsAndSelf()).ToArray();
                if (envelope is null || finalData is null || nodes.Any(n => !PocKnown(n, nativeSignatures, nativeNodes)))
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

        private static void CaptureStringCoalescing(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method ||
                    PreparedStringCoalesce.TryGet(method, (object?)method.BodyStatements ?? method.BodyExpression!) is not { } coalesce ||
                    !PocSignature(method.Signature, stringCoalesce: true)) { continue; }
                var matches = methodRoot.DescendantsAndSelf().Where(node => ReferenceEquals(node.Producer, coalesce.Root)).ToArray();
                if (matches.Length != 1 || !HasChildren(matches[0], coalesce.Root)) { continue; }
                var root = matches[0];
                var right = root.Children[1];
                if (!HasChildren(right, coalesce.Right) || !IsBoundString(root.Children[0]) ||
                    !IsBoundString(right.Children[0]) || !IsBoundString(right.Children[1])) { continue; }

                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(root.Id);
                nodes.Add(right.Id);
                choices.Add(right);
            }

            static bool HasChildren(PreparedSourceOccurrence node, BinaryOperatorExpression binary) =>
                !node.IsOpaque && ReferenceEquals(node.Producer, binary) && node.Children.Length == 2 &&
                ReferenceEquals(node.Children[0].Producer, binary.Left) && ReferenceEquals(node.Children[1].Producer, binary.Right);

            static bool IsBoundString(PreparedSourceOccurrence node) => !node.IsOpaque && node.Children.IsEmpty &&
                node.Producer is VariableExpression variable && PreparedStringCoalesce.IsString(node.ResolveParameterType(variable));
        }

        private static void CaptureUsedLocalInitializers(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider { BodyStatements: { } body } method ||
                    PreparedUsedLocalInitializer.TryGet(method, body) is not { } profile ||
                    !PocSignature(method.Signature, arithmeticReturn: true)) { continue; }

                var matches = methodRoot.Children.Where(node => ReferenceEquals(node.Producer, profile.Block)).ToArray();
                if (matches.Length != 1 || matches[0].IsOpaque || matches[0].Children.Length != 2) { continue; }
                var block = matches[0];
                var first = block.Children[0];
                if (!HasChildren(first, profile.Assignment)) { continue; }
                var assignment = first.Children[0];
                if (!HasChildren(assignment, profile.Declaration, profile.Initializer)) { continue; }
                var declaration = assignment.Children[0];
                var initializer = assignment.Children[1];
                if (!HasChildren(declaration, profile.Declaration.Variable) ||
                    !HasChildren(initializer, profile.Initializer.Left, profile.Initializer.Right) ||
                    !IsBoundInt(initializer.Children[0]) || !IsBoundInt(initializer.Children[1])) { continue; }

                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(block.Id);
                nodes.Add(assignment.Id);
                nodes.Add(declaration.Id);
                nodes.Add(initializer.Id);
                choices.Add(initializer);
            }

            static bool HasChildren(PreparedSourceOccurrence node, params object[] producers) => !node.IsOpaque &&
                node.Children.Length == producers.Length &&
                node.Children.Select(child => child.Producer).SequenceEqual(producers, ReferenceEqualityComparer.Instance);

            static bool IsBoundInt(PreparedSourceOccurrence node) => !node.IsOpaque && node.Children.IsEmpty &&
                node.Producer is VariableExpression variable && PreparedUsedLocalInitializer.IsInt(node.ResolveParameterType(variable));
        }

        private Func<bool>? CaptureOrdinaryArguments(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            // First native argument profile: one closed written type with the
            // caller and its audited private member. No global call
            // environment walk or additional host callback at capture or seal.
            var owner = file.FileOwner;
            if (_sourceRootSnapshot.Length != 1 || !ReferenceEquals(_sourceRootSnapshot[0], owner) || _callLookupScopes.Length != 1 ||
                file.Compilation is not CSharpCompilation compilation) { return null; }

            var methodRoots = file.Roots.Where(root => root.Producer is MethodProvider).ToArray();
            if (methodRoots.Length != 2) { return null; }
            foreach (var callerRoot in methodRoots)
            {
                var caller = (MethodProvider)callerRoot.Producer;
                var calleeRoot = methodRoots.Single(root => !ReferenceEquals(root, callerRoot));
                var callee = (MethodProvider)calleeRoot.Producer;
                var calls = callerRoot.Children.Where(node => node.OrdinaryArgumentGrouping.HasValue).ToArray();
                if (calls.Length != 1 || calls[0].IsOpaque || calls[0].OrdinaryArgumentGrouping is not { } profile ||
                    !profile.IsOriginalListCurrent || !PocNativeSignature(caller.Signature) ||
                    !PocNativeSignature(callee.Signature) || !profile.MatchesCallee(callee)) { continue; }
                var call = calls[0];
                var calleeBody = calleeRoot.Children.SingleOrDefault(node => ReferenceEquals(node.Producer, callee.BodyExpression));
                if (calleeBody is null || calleeBody.IsOpaque || call.Children.Length != profile.OrderedArguments.Length ||
                    call.Children.Where((child, index) => child.Id.EdgePath != call.Id.EdgePath + $"/argument[{index}]" ||
                        !ReferenceEquals(child.Producer, profile.OrderedArguments[index])).Any() ||
                    !NativePocContextDataIsCurrent() || owner.CachedType is not { IsFrameworkType: false } type ||
                    !GeneratedNameIsUncontested(compilation, type.Namespace, type.Name) ||
                    compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
                    PreparedGeneratedMethodGroup.TryCaptureForOrdinaryArgumentGrouping(owner, compilation, caller, callee, profile) is not { } group) { continue; }
                // Only the closed identity-cast argument route needs a type
                // resolver. Reuse the existing exact operand/conversion proof.
                INamedTypeSymbol? ResolveArgumentType(CSharpType candidate) => profile.IsIdentityCastArgument &&
                    PreparedUsedLocalInitializer.IsInt(candidate) && compilation.GetSpecialType(SpecialType.System_Int32) is { TypeKind: not TypeKind.Error } resolved
                    ? resolved : null;
                var proof = PreparedCallProofs.Capture(call.Id, profile.Call, compilation, ResolveArgumentType, NativePocContextDataIsCurrent,
                    call.IsCurrent, children: call.Children,
                    resolveGeneratedGroup: emitted => ReferenceEquals(emitted, owner.CachedType) && ReferenceEquals(emitted, type) && group.IsCurrent ? group : null,
                    resolveOwningType: invocation => ReferenceEquals(invocation, profile.Call) && ReferenceEquals(caller.BodyExpression, invocation)
                        && ReferenceEquals(caller.EnclosingType, owner) ? owner.CachedType : null, allowNativeIntAddition: !profile.IsIdentityCastArgument);
                if (proof.Selection is not { GeneratedDeclaration: { } selected } selection || !ReferenceEquals(selected.Method, callee) ||
                    !PreparedSourceObservations.TryCaptureDirectArgumentEdges(call, selection, out var captures) || !captures.IsEmpty)
                { continue; }

                methods.Add(caller);
                methods.Add(callee);
                signatures.Add(caller.Signature);
                signatures.Add(callee.Signature);
                nodes.Add(call.Id);
                if (calleeBody.Producer is BinaryOperatorExpression) { nodes.Add(calleeBody.Id); }
                foreach (var child in call.Children.Take(profile.CandidateCount)) { nodes.Add(child.Id); choices.Add(child); }
                if (profile.HasCallerExpression)
                {
                    // The admitted signature/actuals contain only this checked
                    // observer attribute, default string and explicit string.
                    foreach (var node in calleeRoot.Children.Where(child => ReferenceEquals(child.Producer, callee.Signature))
                        .Concat(call.Children.Skip(profile.CandidateCount)).SelectMany(child => child.DescendantsAndSelf()))
                    {
                        if (node.Producer is AttributeStatement or LiteralExpression ||
                            node.Producer is ScopedApi wrapper && PreparedCallProofs.IsTransparentWrapper(wrapper)) { nodes.Add(node.Id); }
                    }
                }
                // All sibling choices close over the same original list fact,
                // independently of the existing receipt/selection shape walks.
                return () => profile.IsOriginalListCurrent && selection.IsCurrent;
            }
            return null;
        }

        private static void CaptureTypedDefaultCasts(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            if (file.Compilation.GetSpecialType(SpecialType.System_Nullable_T).TypeKind == TypeKind.Error) { return; }
            foreach (var root in file.Roots)
            {
                if (root.Producer is not MethodProvider method || !Identifier(method.Signature.Name) ||
                    PreparedTypedDefaultCast.TryGet(method) is not { } profile ||
                    method.Signature.ReturnType is not { } nullable || method.EnclosingType.CachedType is not { } owner || owner.Name == nullable.Name ||
                    Child(root, "expression-body", profile.Cast) is not { } cast || cast.Children.Length != 1 ||
                    Child(cast, "value", profile.Operand) is not { } operand || !operand.Children.IsEmpty ||
                    Child(root, "signature", method.Signature) is not { } signature ||
                    Child(signature, "constraint[0]", profile.Constraint) is not { Children.Length: 1 } constraint ||
                    constraint.Children[0] is not { IsOpaque: false, Producer: KeywordExpression { Keyword: "struct", Expression: null } } keyword ||
                    keyword.Id.EdgePath != constraint.Id.EdgePath + "/constraint[0]") { continue; }
                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(cast.Id);
                nodes.Add(operand.Id);
                nodes.Add(constraint.Id);
                nodes.Add(keyword.Id);
                choices.Add(cast);
            }

            static PreparedSourceOccurrence? Child(PreparedSourceOccurrence parent, string role, object producer) =>
                parent.Children.FirstOrDefault(node => !node.IsOpaque && ReferenceEquals(node.Producer, producer) &&
                    node.Id.EdgePath == parent.Id.EdgePath + "/" + role);
        }

        private static void CapturePrimitiveCastParents(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method ||
                    PreparedPrimitiveCastParent.TryGet(method, (object?)method.BodyStatements ?? method.BodyExpression!) is not { } profile ||
                    !PocSignature(method.Signature, primitiveCastParent: true)) { continue; }
                var root = Child(methodRoot, profile.Assignment is null ? "expression-body" : "block-body", profile.Root);
                if (root is null) { continue; }
                PreparedSourceOccurrence? cast;
                var context = new List<PreparedSourceOccurrence> { root };
                if (profile.SeparateAssignment && profile.Assignment is { } separateAssignment && profile.Declaration is { } standaloneDeclaration)
                {
                    if (root.Children.Length != 3 || Child(root.Children[0], "expression", standaloneDeclaration) is not { } actualDeclaration ||
                        Child(root.Children[1], "expression", separateAssignment) is not { } actualAssignment ||
                        Child(actualAssignment, "target", separateAssignment.Variable) is null) { continue; }
                    cast = Child(actualAssignment, "value", profile.Cast);
                    context.Add(actualDeclaration);
                    context.Add(actualAssignment);
                }
                else if (profile.Assignment is { } assignment && profile.Declaration is { } declaration)
                {
                    if (root.Children.Length != 2 || Child(root.Children[0], "expression", assignment) is not { } actualAssignment ||
                        Child(actualAssignment, "target", declaration) is not { } actualDeclaration) { continue; }
                    cast = Child(actualAssignment, "value", profile.Cast);
                    context.Add(actualAssignment);
                    context.Add(actualDeclaration);
                }
                else if (profile.Root is CheckedExpression)
                {
                    if (root.Children.Length != 1) { continue; }
                    cast = Child(root, "operand", profile.Cast);
                }
                else if (profile.CheckedBinary is { } checkedBinary)
                {
                    if (root.Children.Length != 1 || Child(root, "value", checkedBinary) is not { } binaryRoot ||
                        binaryRoot.Children.Length != 2 || Child(binaryRoot, "right", checkedBinary.Right) is not { } other ||
                        !IsBound(other, useBool: false)) { continue; }
                    cast = Child(binaryRoot, "left", profile.Cast);
                    context.Add(binaryRoot);
                }
                else if (profile.Root is BinaryOperatorExpression binary)
                {
                    if (root.Children.Length != 2 || Child(root, "right", binary.Right) is not { } other || !IsBound(other, useBool: false))
                    { continue; }
                    cast = Child(root, "left", profile.Cast);
                }
                else if (profile.Root is TernaryConditionalExpression ternary)
                {
                    if (root.Children.Length != 3 || Child(root, "consequent", ternary.Consequent) is not { } consequent ||
                        Child(root, "alternative", ternary.Alternative) is not { } alternative) { continue; }
                    cast = Child(root, "condition", profile.Cast);
                    context.AddRange(consequent.DescendantsAndSelf());
                    context.AddRange(alternative.DescendantsAndSelf());
                }
                else { continue; }
                if (cast is null || cast.Children.Length != 1 || Child(cast, "value", profile.Cast.Inner) is not { } operand ||
                    !IsBound(operand, profile.Root is TernaryConditionalExpression, useLong: profile.CheckedBinary is not null || profile.Root is CheckedExpression)) { continue; }

                methods.Add(method);
                signatures.Add(method.Signature);
                foreach (var node in context) { nodes.Add(node.Id); }
                nodes.Add(cast.Id);
                choices.Add(cast);
            }

            static PreparedSourceOccurrence? Child(PreparedSourceOccurrence parent, string role, object producer) =>
                parent.Children.FirstOrDefault(node => !node.IsOpaque && ReferenceEquals(node.Producer, producer) &&
                    node.Id.EdgePath == parent.Id.EdgePath + "/" + role);

            static bool IsBound(PreparedSourceOccurrence node, bool useBool, bool useLong = false)
            {
                if (node.IsOpaque || !node.Children.IsEmpty || node.Producer is not VariableExpression variable) { return false; }
                var type = node.ResolveParameterType(variable);
                return useBool ? PreparedPrimitiveCastParent.IsBool(type) :
                    useLong ? PreparedPrimitiveCastParent.IsLong(type) : PreparedUsedLocalInitializer.IsInt(type);
            }
        }

        private Func<bool>? CaptureLambdaCast(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            if (_sourceRootSnapshot.Length != 1 || _callLookupScopes.Length != 1 || !ReferenceEquals(_sourceRootSnapshot[0], file.FileOwner) ||
                file.Compilation is not CSharpCompilation compilation || file.FileOwner.CachedType is not { } ownerType) { return null; }
            var roots = file.Roots.Where(root => root.Producer is MethodProvider).ToArray();
            if (roots.Length is not (1 or 2)) { return null; }
            foreach (var root in roots)
            {
                var method = (MethodProvider)root.Producer;
                PreparedSourceOccurrence? calleeRoot = null;
                MethodProvider? callee = null;
                IMethodSymbol? constructor = null;
                INamedTypeSymbol? holder = null;
                PreparedGeneratedMethodGroup? group = null;
                PreparedLambdaCaptureCast profile;
                if (roots.Length == 1)
                {
                    if (!PocSignature(method.Signature, objectReturnIntFormals: true) ||
                        PreparedLambdaCaptureCast.TryGetConstructor(method) is not { } constructorProfile || !constructorProfile.IsListCurrent ||
                        !NativePocContextDataIsCurrent() || PreparedLambdaCaptureCast.FindHolder(compilation, ownerType) is not { } holderType ||
                        PreparedLambdaCaptureCast.FindConstructor(holderType, compilation) is not { } sourceConstructor) { continue; }
                    profile = constructorProfile;
                    holder = holderType;
                    constructor = sourceConstructor;
                }
                else
                {
                    calleeRoot = roots.Single(other => !ReferenceEquals(other, root));
                    callee = (MethodProvider)calleeRoot.Producer;
                    if (PreparedLambdaCaptureCast.TryGetGeneratedMethod(method) is not { } methodProfile || !methodProfile.IsListCurrent ||
                        !PreparedLambdaCaptureCast.MatchesGeneratedCallee(callee) || !NativePocContextDataIsCurrent() ||
                        !GeneratedNameIsUncontested(compilation, ownerType.Namespace, ownerType.Name) ||
                        PreparedLambdaCaptureCast.FindGlobalFunc(compilation, ownerType) is not { } func ||
                        PreparedGeneratedMethodGroup.TryCaptureForLambdaGrouping(file.FileOwner, compilation, method, callee, methodProfile, func) is not { } methodsGroup)
                    { continue; }
                    profile = methodProfile;
                    group = methodsGroup;
                }
                if (compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) { continue; }
                if (CaptureChosenLambdaCast(root, method, calleeRoot, callee, constructor, holder, group, profile, compilation, ownerType,
                    file.FileOwner, methods, signatures, nodes, choices) is { } current) { return current; }
            }
            return null;
        }

        private Func<bool>? CaptureChosenLambdaCast(PreparedSourceOccurrence root, MethodProvider method, PreparedSourceOccurrence? calleeRoot,
            MethodProvider? callee, IMethodSymbol? constructor, INamedTypeSymbol? holder, PreparedGeneratedMethodGroup? group,
            PreparedLambdaCaptureCast profile, CSharpCompilation compilation, CSharpType ownerType, TypeProvider owner,
            HashSet<MethodProvider> methods, HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            string callPath = root.Id.EdgePath + "/expression-body";
            var call = root.Children.SingleOrDefault(child => ReferenceEquals(child.Producer, profile.Call) && child.Id.EdgePath == callPath);
            if (call is null || call.IsOpaque || call.Children.Length != profile.Arguments.Length ||
                call.Children.Where((child, index) => child.Id.EdgePath != callPath + $"/argument[{index}]" || child.IsOpaque ||
                    !ReferenceEquals(child.Producer, profile.Arguments[index])).Any()) { return null; }
            var castPaths = call.Children[0].DescendantsAndSelf().Where(node => ReferenceEquals(node.Producer, profile.Cast)).ToArray();
            if (castPaths.Length != 1 || castPaths[0].Id.EdgePath != callPath + "/argument[0]/block-body/expression/value" ||
                castPaths[0].IsOpaque || castPaths[0].Children.Length != 1) { return null; }
            var cast = castPaths[0];
            var operand = cast.Children[0];
            if (operand.IsOpaque || operand.Id.EdgePath != cast.Id.EdgePath + "/value" ||
                operand.Producer is not VariableExpression variable || !ReferenceEquals(variable, profile.Cast.Inner) ||
                !PreparedUsedLocalInitializer.IsInt(operand.ResolveParameterType(variable))) { return null; }

            var intType = compilation.GetSpecialType(SpecialType.System_Int32);
            if (holder is not null)
            {
                var result = compilation.ClassifyConversion(holder, compilation.GetSpecialType(SpecialType.System_Object));
                if (!result.IsImplicit || !result.IsReference || result.IsUserDefined) { return null; }
            }
            INamedTypeSymbol? ResolveType(CSharpType type) => profile.Call is NewInstanceExpression creation && ReferenceEquals(type, creation.Type) ? holder :
                PreparedUsedLocalInitializer.IsInt(type) && intType.TypeKind != TypeKind.Error ? intType : null;
            var proof = PreparedCallProofs.Capture(call.Id, profile.Call, compilation, ResolveType, NativePocContextDataIsCurrent,
                call.IsCurrent, children: call.Children,
                resolveGeneratedGroup: type => ReferenceEquals(type, ownerType) && ReferenceEquals(type, owner.CachedType) && group?.IsCurrent == true ? group : null,
                resolveOwningType: invocation => group is not null && ReferenceEquals(invocation, profile.Call) &&
                    ReferenceEquals(method.BodyExpression, invocation) && ReferenceEquals(method.EnclosingType, owner) ? owner.CachedType : null);
            if (proof.Selection is not { } selection || (constructor is not null
                    ? !SymbolEqualityComparer.Default.Equals(selection.Contract.DeclarationSymbol, constructor)
                    : callee is null || !ReferenceEquals(selection.GeneratedDeclaration?.Method, callee)) ||
                !PreparedSourceObservations.TryCaptureDirectArgumentEdges(call, selection, out var captures) || !captures.IsEmpty ||
                profile.Arguments.Length != 2) { return null; }

            methods.Add(method);
            signatures.Add(method.Signature);
            nodes.Add(call.Id);
            nodes.Add(call.Children[0].Id);
            nodes.Add(cast.Id);
            foreach (var node in call.Children[1].DescendantsAndSelf()) { nodes.Add(node.Id); }
            choices.Add(cast);
            if (calleeRoot is not null && callee is not null)
            {
                methods.Add(callee);
                signatures.Add(callee.Signature);
                foreach (var node in calleeRoot.Children.Where(child => ReferenceEquals(child.Producer, callee.Signature))
                    .SelectMany(child => child.DescendantsAndSelf())) { nodes.Add(node.Id); }
            }
            return () => profile.IsListCurrent && selection.IsCurrent;
        }

        private void CaptureMetadataCastReturns(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            // The captured input compilation does not contain newly written
            // types. This profile checks the sole emitted owner as well.
            if (_sourceRootSnapshot.Length != 1 || _callLookupScopes.Length != 1 || !ReferenceEquals(_sourceRootSnapshot[0], file.FileOwner) ||
                file.Compilation is not CSharpCompilation compilation || file.FileOwner.CachedType is not { } ownerType) { return; }
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method || !PocSignature(method.Signature, metadataCastReturn: true) ||
                    PreparedMetadataCastReturn.TryGet(method, compilation, ownerType) is not { } profile) { continue; }
                var cast = methodRoot.Children.SingleOrDefault(child => child.Id.EdgePath == methodRoot.Id.EdgePath + "/expression-body" &&
                    ReferenceEquals(child.Producer, profile.Cast));
                if (cast is null || cast.IsOpaque || cast.Children.Length != 1) { continue; }
                var operand = cast.Children[0];
                if (operand.IsOpaque || !operand.Children.IsEmpty || !ReferenceEquals(operand.Producer, profile.Cast.Inner) ||
                    operand.Id.EdgePath != cast.Id.EdgePath + "/value" || profile.NeedsObjectFormal &&
                    (operand.Producer is not VariableExpression variable || !PreparedMetadataCastReturn.IsObject(operand.ResolveParameterType(variable))))
                { continue; }
                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(cast.Id);
                nodes.Add(operand.Id);
                choices.Add(cast);
            }
        }

        private void CaptureSourceNumberCasts(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            if (_sourceRootSnapshot.Length != 1 || _callLookupScopes.Length != 1 || !ReferenceEquals(_sourceRootSnapshot[0], file.FileOwner) ||
                file.Compilation is not CSharpCompilation compilation || file.FileOwner.CachedType is not { } ownerType) { return; }
            foreach (var root in file.Roots)
            {
                if (root.Producer is not MethodProvider method || !PocSignature(method.Signature, objectReturnIntFormals: true) ||
                    PreparedSourceNumberCast.TryGet(method, compilation, ownerType) is not { } profile) { continue; }
                var path = root.Id.EdgePath + (profile.IsStatement ? "/block-body/expression/value" : "/expression-body");
                var matches = root.DescendantsAndSelf().Where(node => ReferenceEquals(node.Producer, profile.Cast)).ToArray();
                if (matches.Length != 1 || matches[0].Id.EdgePath != path || matches[0].IsOpaque || matches[0].Children.Length != 1) { continue; }
                var child = matches[0].Children[0];
                if (child.IsOpaque || !child.Children.IsEmpty || child.Id.EdgePath != path + "/value" ||
                    child.Producer is not VariableExpression variable || !ReferenceEquals(variable, profile.Cast.Inner) ||
                    !PreparedUsedLocalInitializer.IsInt(child.ResolveParameterType(variable))) { continue; }
                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(matches[0].Id);
                choices.Add(matches[0]);
            }
        }

        private static void CaptureDirectCasts(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method || method.GetType() != typeof(MethodProvider))
                { continue; }
                var cast = ((object?)method.BodyStatements ?? method.BodyExpression) switch
                {
                    CastExpression expression => expression,
                    ExpressionStatement statement when statement.GetType() == typeof(ExpressionStatement) &&
                        statement.Expression is KeywordExpression { Keyword: "return", Expression: CastExpression returned } keyword &&
                        keyword.GetType() == typeof(KeywordExpression) => returned,
                    _ => null
                };
                if (cast?.Inner is not VariableExpression variable || variable.GetType() != typeof(VariableExpression) ||
                    !PocIntType(cast.Type) || !PocIntType(method.Signature.ReturnType) || method.Signature.HasUnsupportedBaseContract ||
                    !PocSignature(method.Signature, true) ||
                    !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) || parameters.Length != 1 ||
                    !parameters[0].HasCachedEmittedDeclarationState || parameters[0].HasUnsupportedParameterModifiers || parameters[0].HasUnsupportedDefaultValue)
                { continue; }
                var matches = methodRoot.DescendantsAndSelf().Where(node => ReferenceEquals(node.Producer, cast)).ToArray();
                if (matches.Length != 1 || matches[0].Children.Length != 1 ||
                    !ReferenceEquals(matches[0].Children[0].Producer, variable) || !PocIntType(matches[0].ResolveParameterType(variable)))
                { continue; }
                methods.Add(method);
                signatures.Add(method.Signature);
                nodes.Add(matches[0].Id);
                choices.Add(matches[0]);
            }
        }

        private static bool PocIntType(CSharpType? type) => PocType(type, typeof(int)) && !type!.IsLiteral && !type.HasUnionMetadata;

        private static void CaptureIfConditions(PreparedSourceFile file, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            foreach (var methodRoot in file.Roots)
            {
                if (methodRoot.Producer is not MethodProvider method || method.GetType() != typeof(MethodProvider) ||
                    !PocSignature(method.Signature) || method.Signature.HasUnsupportedBaseContract ||
                    method.Signature.ReturnType!.IsLiteral || method.Signature.ReturnType.HasUnionMetadata ||
                    !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) || parameters.Length != 2 ||
                    parameters.Any(parameter => !PocIntType(parameter.Type) || !parameter.HasCachedEmittedDeclarationState ||
                        parameter.HasUnsupportedParameterModifiers || parameter.HasUnsupportedDefaultValue) ||
                    method.BodyStatements is not MethodBodyStatements body || body.GetType() != typeof(MethodBodyStatements) ||
                    !ContractCollections.TryCopy(body.Statements, out var statements) || statements.Length != 2 ||
                    statements[0] is not IfStatement conditional ||
                    !ContractCollections.TryCopy(conditional.SourceBody, out var whenTrue) || whenTrue.Length != 1 ||
                    !PocReturnBoolean(whenTrue[0], true, out var trueLiteral) ||
                    !PocReturnBoolean(statements[1], false, out var falseLiteral) ||
                    conditional.Condition is not BinaryOperatorExpression { Operator: "==", AddParentheses: true,
                        Left: VariableExpression left, Right: VariableExpression right } binary ||
                    left.GetType() != typeof(VariableExpression) || right.GetType() != typeof(VariableExpression))
                { continue; }
                var occurrences = methodRoot.DescendantsAndSelf().ToArray();
                var matches = occurrences.Where(node => ReferenceEquals(node.Producer, binary)).ToArray();
                if (matches.Length != 1 || matches[0].IsOpaque || matches[0].Children.Length != 2 ||
                    !ReferenceEquals(matches[0].Children[0].Producer, left) || !ReferenceEquals(matches[0].Children[1].Producer, right) ||
                    !PocIntType(matches[0].ResolveParameterType(left)) || !PocIntType(matches[0].ResolveParameterType(right)))
                { continue; }
                // Admit only the four extra context occurrences in this closed
                // native method, never these producer kinds in unrelated roots.
                var context = occurrences.Where(node => ReferenceEquals(node.Producer, body) || ReferenceEquals(node.Producer, conditional) ||
                    ReferenceEquals(node.Producer, trueLiteral) || ReferenceEquals(node.Producer, falseLiteral)).ToArray();
                if (context.Length != 4 || context.Any(node => node.IsOpaque))
                { continue; }
                foreach (var node in context) { nodes.Add(node.Id); }
                choices.Add(matches[0]);
            }
        }

        private static bool PocReturnBoolean(MethodBodyStatement? statement, bool expected, out LiteralExpression literal)
        {
            literal = null!;
            if (statement is not ExpressionStatement expression || expression.GetType() != typeof(ExpressionStatement) ||
                expression.Expression is not KeywordExpression { Keyword: "return", Expression: LiteralExpression value } keyword ||
                keyword.GetType() != typeof(KeywordExpression) || value.GetType() != typeof(LiteralExpression) ||
                value.Literal is not bool actual || actual != expected)
            { return false; }
            literal = value;
            return true;
        }

        private Func<bool>? CapturePocEnvelope(PreparedSourceFile file, HashSet<MethodProvider> nativeMethods, out Func<bool>? finalData)
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
                        !PocSignature(method.Signature) && !(nativeMethods.Contains(method) && PocNativeSignature(method.Signature)))
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

        private static bool PocNativeSignature(MethodSignature signature) =>
            PocSignature(signature, true) || PocSignature(signature, stringCoalesce: true) ||
            PreparedOrdinaryArgumentGrouping.HasCallerExpressionSignature(signature) || PocSignature(signature, primitiveCastParent: true) ||
            PocSignature(signature, metadataCastReturn: true) || PocSignature(signature, objectReturnIntFormals: true) ||
            Identifier(signature.Name) && (PreparedLambdaCaptureCast.HasGeneratedSignature(signature) ||
                PreparedTypedDefaultCast.HasSignature(signature, out _));

        private static bool PocSignature(MethodSignatureBase signature, bool arithmeticReturn = false, bool stringCoalesce = false,
            bool primitiveCastParent = false, bool metadataCastReturn = false, bool objectReturnIntFormals = false)
        {
            if (signature is not MethodSignature method || method.NonDocumentComment is not null || !Identifier(method.Name) ||
                !(stringCoalesce ? PreparedStringCoalesce.IsString(method.ReturnType) :
                    primitiveCastParent ? PreparedPrimitiveCastParent.IsLong(method.ReturnType) || PocIntType(method.ReturnType) :
                    metadataCastReturn || objectReturnIntFormals ? PreparedMetadataCastReturn.IsObject(method.ReturnType) :
                    PocType(method.ReturnType, typeof(bool)) || arithmeticReturn && PocType(method.ReturnType, typeof(int))) ||
                !ContractCollections.TryCopy(method.Attributes, out var attributes) || !attributes.IsEmpty || method.ExplicitInterface is not null ||
                method.GenericArguments is not null || method.GenericParameterConstraints is not null ||
                !ContractCollections.TryCopy(method.Parameters, out var parameters))
            { return false; }
            const MethodSignatureModifiers allowed = MethodSignatureModifiers.Public | MethodSignatureModifiers.Private | MethodSignatureModifiers.Static;
            return (method.Modifiers & ~allowed) == 0 && parameters.All(p => p is not null && p.GetType() == typeof(ParameterProvider) &&
                Identifier(p.Name) && (stringCoalesce ? PreparedStringCoalesce.IsString(p.Type) :
                    metadataCastReturn ? PreparedMetadataCastReturn.IsObject(p.Type) : PocType(p.Type, typeof(int)) || primitiveCastParent &&
                    (PreparedPrimitiveCastParent.IsBool(p.Type) || PreparedPrimitiveCastParent.IsLong(p.Type))) &&
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

        private bool PocKnown(PreparedSourceOccurrence node, HashSet<MethodSignature> nativeSignatures, HashSet<SourceOccurrenceId> nativeNodes)
        {
            if (node.IsOpaque)
            { return false; }
            return node.Producer switch
            {
                BinaryOperatorExpression binary => nativeNodes.Contains(node.Id) ||
                    binary.Operator == "==" && binary.Left?.GetType() == typeof(VariableExpression) && binary.Right?.GetType() == typeof(VariableExpression),
                CastExpression or MethodBodyStatements or IfStatement or LiteralExpression or AssignmentExpression or DeclarationExpression or
                    InvokeMethodExpression or AttributeStatement or ScopedApi or TernaryConditionalExpression or CheckedExpression or FuncExpression or NewInstanceExpression or TypedDefaultExpression or WhereExpression =>
                    nativeNodes.Contains(node.Id),
                KeywordExpression keyword => keyword.GetType() == typeof(KeywordExpression) &&
                    (keyword.Keyword is "return" or "default" || (keyword.Keyword is "null" or "checked" or "struct") && nativeNodes.Contains(node.Id)),
                VariableExpression variable => variable.GetType() == typeof(VariableExpression),
                MethodProvider method => method.GetType() == typeof(MethodProvider),
                MethodSignature signature => PocSignature(signature) || nativeSignatures.Contains(signature) && PocNativeSignature(signature),
                ParameterProvider parameter => parameter.GetType() == typeof(ParameterProvider),
                PropertyProvider property => _pocSeeds.TryGetValue(property, out var projection) && projection.IsCurrent,
                ExpressionPropertyBody body => body.GetType() == typeof(ExpressionPropertyBody),
                ExpressionStatement statement => statement.GetType() == typeof(ExpressionStatement),
                _ => ReferenceEquals(node.Producer, ValueExpression.Empty)
            };
        }

        internal bool TryUsePocGrouping(PreparedSourceFile file, PreparedSourceOccurrence node, ValueExpression expression)
        {
            if (!_pocGrouping.TryGetValue(node.Id, out var choice) || !ReferenceEquals(choice.File, file) ||
                !ReferenceEquals(choice.Node, node) || !ReferenceEquals(node.Producer, expression) ||
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
