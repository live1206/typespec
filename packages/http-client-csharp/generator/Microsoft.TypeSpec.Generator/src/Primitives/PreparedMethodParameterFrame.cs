// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>Emitted parameter correspondence in one audited method body, not a lexical binder.</summary>
    internal sealed class PreparedMethodParameterFrame
    {
        private readonly MethodProvider _method;
        private readonly long _revision;
        private readonly MethodSignature _signature;
        private readonly StructuredSourceShape _signatureShape;
        private readonly object _body;
        private readonly IReadOnlyList<ParameterProvider> _parameters;
        private readonly ImmutableArray<ParameterState> _states;
        private readonly ImmutableArray<(object Producer, StructuredSourceShape Shape)> _region;
        private readonly InvokeMethodExpression? _ownedBlockInvocation;
        private readonly PreparedEqualityRegion? _equalityRegion;
        internal bool HasDirectEqualityRegion => _equalityRegion is not null;
        private int _stale;

        private PreparedMethodParameterFrame(MethodProvider method, object body, ImmutableArray<ParameterState> states,
            ImmutableArray<(object Producer, StructuredSourceShape Shape)> region, InvokeMethodExpression? ownedBlockInvocation,
            PreparedEqualityRegion? equalityRegion)
        {
            _method = method;
            _revision = method.DeclarationRevision;
            _signature = method.Signature;
            _signatureShape = StructuredSourceShape.Capture(_signature, true);
            _body = body;
            _parameters = _signature.Parameters;
            _states = states;
            _region = region;
            _ownedBlockInvocation = ownedBlockInvocation;
            _equalityRegion = equalityRegion;
        }

        internal static PreparedMethodParameterFrame? TryCapture(MethodProvider method, object body) =>
            TryCaptureCore(method, body, sourceCapture: false);

        internal static PreparedMethodParameterFrame? TryCaptureForSource(MethodProvider method, object body) =>
            TryCaptureCore(method, body, sourceCapture: true);

        private static PreparedMethodParameterFrame? TryCaptureCore(MethodProvider method, object body, bool sourceCapture)
        {
            if (!ReferenceEquals((object?)method.BodyStatements ?? method.BodyExpression, body) ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters)) { return null; }
            var ownedBlockInvocation = PreparedUnusedDefaultLocalCallBlock.GetInvocation(method);
            bool binaryParentRoles = sourceCapture && PreparedBinaryParentRoles.TryGetNewRoot(method, body, out _);
            if (binaryParentRoles && parameters.Any(parameter => parameter is null || !parameter.HasCachedEmittedDeclarationState)) { return null; }
            var coalesce = sourceCapture && !binaryParentRoles ? PreparedStringCoalesce.TryGet(method, body, parameters) : null;
            var usedLocal = sourceCapture && !binaryParentRoles && coalesce is null && ownedBlockInvocation is null
                ? PreparedUsedLocalInitializer.TryGet(method, body, parameters) : null;
            var region = ImmutableArray.CreateBuilder<(object, StructuredSourceShape)>();
            if (!CaptureRegion(body, region, new HashSet<object>(ReferenceEqualityComparer.Instance), 0,
                allowThis: PreparedGeneratedMethodGroup.HasOwningRootCallProfile(method),
                allowDeclarations: ownedBlockInvocation is not null, binaryParentRoles: binaryParentRoles, coalesce: coalesce, usedLocal: usedLocal)) { return null; }
            var capturedRegion = region.ToImmutable();
            // Binary regions are a new source-capture family. Do not introduce
            // parameter-spelling callbacks where the old grammar returned no frame.
            // Explicit frame capture keeps its original observation order; source
            // capture admits the new family only with actual native cached spellings.
            if (sourceCapture && capturedRegion.Any(entry => entry.Item1 is BinaryOperatorExpression) &&
                parameters.Any(parameter => parameter is null || !parameter.HasCachedEmittedDeclarationState)) { return null; }
            // Capture before parameter spelling observations: an uncached name
            // can invoke ambient culture code and mutate this retained region.
            var equalityRegion = ownedBlockInvocation is null ? PreparedEqualityRegion.TryCapture(body, capturedRegion) : null;
            var states = ImmutableArray.CreateBuilder<ParameterState>();
            foreach (var parameter in parameters)
            {
                if (parameter is null || !DeclaredTypeSnapshot.TryCapture(parameter.Type, out var type)) { return null; }
                states.Add(new(parameter, parameter.DeclarationRevision, parameter.Type, type!, parameter.EmittedDeclarationState, parameter.CachedVariableExpression));
            }
            return new(method, body, states.ToImmutable(), capturedRegion, ownedBlockInvocation, equalityRegion);
        }

        // Block ownership never lends method-parameter types through locals.
        internal CSharpType? Resolve(VariableExpression variable)
        {
            if (_ownedBlockInvocation is not null || variable.GetType() != typeof(VariableExpression) || !IsCurrent()) { return null; }
            ParameterState? match = null;
            foreach (var state in _states)
            {
                if (state.Carrier?.GetType() != typeof(VariableExpression) || !ReferenceEquals(state.Carrier.Declaration, variable.Declaration)) { continue; }
                if (match is not null || state.Parameter.IsRef || state.Parameter.IsOut || state.Parameter.IsIn) { return null; }
                match = state;
            }
            return match?.Type;
        }

        internal CSharpType? Resolve(ParameterProvider parameter, string emittedName)
        {
            if (_ownedBlockInvocation is not null || !IsCurrent()) { return null; }
            var matches = _states.Where(s => ReferenceEquals(s.Parameter, parameter)).ToArray();
            if (matches.Length != 1 || parameter.IsRef || parameter.IsOut || parameter.IsIn ||
                matches[0].EmittedState.Name != emittedName || _states.Count(s => s.EmittedState.Name == emittedName) != 1) { return null; }
            return matches[0].Type;
        }

        // An actual owned expression root, or the exact final call in the
        // finite unused-local block profile, supplies its containing type.
        // CachedType is nonforcing; no hint or retained callee symbol is evidence.
        internal CSharpType? ResolveOwningType(InvokeMethodExpression invocation)
        {
            if (!IsCurrent() ||
                !(_ownedBlockInvocation is null
                    ? ReferenceEquals(_body, invocation) && PreparedGeneratedMethodGroup.HasOwningRootCallProfile(_method)
                    : ReferenceEquals(_ownedBlockInvocation, invocation) && ReferenceEquals(PreparedUnusedDefaultLocalCallBlock.GetInvocation(_method), invocation)) ||
                !ContractCollections.TryCopy(_method.EnclosingType.CachedMethods, out var methods) ||
                methods.Count(m => ReferenceEquals(m, _method)) != 1 || !CodeWriter.HasMethodDeclaration(_method)) { return null; }
            return _method.EnclosingType.CachedType;
        }

        internal bool HasCachedParameterEmittedStates =>
            _states.All(state => state.Parameter.HasCachedEmittedDeclarationState);

        internal bool IsCurrent()
        {
            if (System.Threading.Volatile.Read(ref _stale) != 0) { return false; }
            var current = _method.DeclarationRevision == _revision && ReferenceEquals(_method.Signature, _signature) &&
                ReferenceEquals((object?)_method.BodyStatements ?? _method.BodyExpression, _body) &&
                ReferenceEquals(_signature.Parameters, _parameters) && ContractCollections.TryCopy(_parameters, out var parameters) &&
                parameters.Length == _states.Length && parameters.Where((p, i) => !ReferenceEquals(p, _states[i].Parameter)).Any() == false &&
                _signatureShape.Matches(StructuredSourceShape.Capture(_signature, true)) && _states.All(s => s.IsCurrent()) &&
                (_equalityRegion?.MatchesCurrent() == true ||
                    _region.All(r => r.Shape.Matches(StructuredSourceShape.Capture(r.Producer, true)))) &&
                (_ownedBlockInvocation is null || ReferenceEquals(PreparedUnusedDefaultLocalCallBlock.GetInvocation(_method), _ownedBlockInvocation));
            if (!current) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return current;
        }

        private static bool CaptureRegion(object producer, ImmutableArray<(object, StructuredSourceShape)>.Builder region, HashSet<object> ancestors, int depth, bool returnPosition = false, bool allowThis = false, bool allowDeclarations = false, bool binaryParentRoles = false, PreparedStringCoalesce? coalesce = null, PreparedUsedLocalInitializer? usedLocal = null)
        {
            if (depth >= 32 || (binaryParentRoles || coalesce.HasValue || usedLocal.HasValue) && region.Count >= PreparedBinaryParentRoles.MaximumOccurrences ||
                !ancestors.Add(producer) || usedLocal?.Contains(producer) != true &&
                !Allowed(producer, returnPosition, allowThis, allowDeclarations, binaryParentRoles, coalesce)) { return false; }
            var shape = StructuredSourceShape.Capture(producer, true);
            if (shape.Opaque) { return false; }
            region.Add((producer, shape));
            foreach (var child in shape.Children)
            {
                if (!CaptureRegion(child.Producer, region, ancestors, depth + 1, producer.GetType() == typeof(ExpressionStatement), allowThis, allowDeclarations, binaryParentRoles, coalesce, usedLocal)) { return false; }
            }
            ancestors.Remove(producer);
            return true;
        }

        private static bool Allowed(object producer, bool returnPosition, bool allowThis, bool allowDeclarations, bool binaryParentRoles, PreparedStringCoalesce? coalesce) => producer switch
        {
            ScopedApi wrapper => PreparedCallProofs.IsTransparentWrapper(wrapper),
            FuncExpression closure => ContractCollections.TryCopy(closure.Parameters, out var parameters) && parameters.Length == 0,
            KeywordExpression keyword => keyword.GetType() == typeof(KeywordExpression) &&
                (keyword.Expression is null && (keyword.Keyword is "null" or "default" || allowThis && keyword.Keyword == "this") ||
                    returnPosition && keyword.Keyword == "return" && keyword.Expression is not null),
            MemberExpression member => member.GetType() == typeof(MemberExpression) && member.Declaration is null,
            BinaryOperatorExpression binary => coalesce?.Contains(binary) == true ||
                binaryParentRoles && PreparedBinaryParentRoles.IsBinaryOperator(binary.Operator) ||
                binary.Operator == "==" && binary.Left?.GetType() == typeof(VariableExpression) && binary.Right?.GetType() == typeof(VariableExpression),
            _ => allowDeclarations && (producer.GetType() == typeof(DeclarationExpression) || producer.GetType() == typeof(AssignmentExpression)) ||
                producer.GetType() == typeof(MethodBodyStatements) || producer.GetType() == typeof(ExpressionStatement) ||
                producer.GetType() == typeof(IfStatement) || producer.GetType() == typeof(IfElseStatement) ||
                producer.GetType() == typeof(WhileStatement) || producer.GetType() == typeof(DoWhileStatement) ||
                producer.GetType() == typeof(VariableExpression) || producer.GetType() == typeof(TypeOfExpression) ||
                producer.GetType() == typeof(NewArrayExpression) || producer.GetType() == typeof(ArrayInitializerExpression) || producer.GetType() == typeof(IndexableExpression) ||
                producer.GetType() == typeof(TypeReferenceExpression) || producer.GetType() == typeof(InvokeMethodExpression) ||
                producer is LiteralExpression or CastExpression or NewInstanceExpression or ArgumentExpression or PositionalParameterReferenceExpression
        };

        private sealed record ParameterState(ParameterProvider Parameter, long Revision, CSharpType Type, DeclaredTypeSnapshot TypeSnapshot,
            (long Variable, long Declaration, string Name) EmittedState, VariableExpression? Carrier)
        {
            internal bool IsCurrent() => Parameter.DeclarationRevision == Revision && ReferenceEquals(Parameter.Type, Type) && TypeSnapshot.IsCurrent() &&
                Parameter.EmittedDeclarationState == EmittedState && (Carrier is null || ReferenceEquals(Parameter.CachedVariableExpression, Carrier));
        }
    }
}
