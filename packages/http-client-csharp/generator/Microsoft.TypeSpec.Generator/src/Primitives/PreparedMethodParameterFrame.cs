// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
        private int _stale;

        private PreparedMethodParameterFrame(MethodProvider method, object body, ImmutableArray<ParameterState> states,
            ImmutableArray<(object Producer, StructuredSourceShape Shape)> region, InvokeMethodExpression? ownedBlockInvocation)
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
        }

        internal static PreparedMethodParameterFrame? TryCapture(MethodProvider method, object body)
        {
            if (!ReferenceEquals((object?)method.BodyStatements ?? method.BodyExpression, body) ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters)) { return null; }
            var ownedBlockInvocation = PreparedUnusedDefaultLocalCallBlock.GetInvocation(method);
            var region = ImmutableArray.CreateBuilder<(object, StructuredSourceShape)>();
            if (!CaptureRegion(body, region, new HashSet<object>(ReferenceEqualityComparer.Instance), 0,
                allowThis: PreparedGeneratedMethodGroup.HasOwningRootCallProfile(method),
                allowDeclarations: ownedBlockInvocation is not null)) { return null; }
            var states = ImmutableArray.CreateBuilder<ParameterState>();
            foreach (var parameter in parameters)
            {
                if (parameter is null || !DeclaredTypeSnapshot.TryCapture(parameter.Type, out var type)) { return null; }
                states.Add(new(parameter, parameter.DeclarationRevision, parameter.Type, type!, parameter.Name,
                    parameter.InputParameter?.IsExactName == true, CultureInfo.CurrentCulture,
                    parameter.EmittedDeclarationState, parameter.CachedVariableExpression));
            }
            return new(method, body, states.ToImmutable(), region.ToImmutable(), ownedBlockInvocation);
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

        internal bool IsCurrent() => IsCurrentCore(invalidate: true);
        internal bool IsCurrentWithoutInvalidating(PocWorkBudget? budget = null) => IsCurrentCore(invalidate: false, budget);
        internal bool IsCurrentWithoutInvalidatingWithTextMemo(PocWorkBudget? budget, PocTypeTextMemo? textMemo) => IsCurrentCore(invalidate: false, budget, textMemo);

        private bool IsCurrentCore(bool invalidate, PocWorkBudget? budget = null, PocTypeTextMemo? textMemo = null)
        {
            if (!SourceCurrentnessChecks.Spend(budget) || System.Threading.Volatile.Read(ref _stale) != 0) { return false; }
            ImmutableArray<ParameterProvider> parameters = default;
            var current = _method.DeclarationRevision == _revision && ReferenceEquals(_method.Signature, _signature) &&
                ReferenceEquals((object?)_method.BodyStatements ?? _method.BodyExpression, _body) &&
                ReferenceEquals(_signature.Parameters, _parameters) && SourceCurrentnessChecks.Copy(_parameters, budget, out parameters) &&
                parameters.Length == _states.Length && SourceCurrentnessChecks.Spend(budget, parameters.Length);
            if (current)
            {
                for (int index = 0; index < parameters.Length; index++)
                {
                    if (!ReferenceEquals(parameters[index], _states[index].Parameter)) { current = false; break; }
                }
            }
            if (current) { current = SourceCurrentnessChecks.Shape(_signatureShape, _signature, true, budget, textMemo); }
            if (current)
            {
                foreach (var state in _states) { if (!state.IsCurrent(budget)) { current = false; break; } }
            }
            if (current)
            {
                foreach (var region in _region)
                {
                    if (!SourceCurrentnessChecks.Spend(budget) || !SourceCurrentnessChecks.Shape(region.Shape, region.Producer, true, budget, textMemo))
                    { current = false; break; }
                }
            }
            if (current && _ownedBlockInvocation is not null)
            {
                current = ReferenceEquals(budget is null ? PreparedUnusedDefaultLocalCallBlock.GetInvocation(_method) :
                    PreparedUnusedDefaultLocalCallBlock.GetInvocationForPoc(_method, budget, _states.Length == 1 ? _states[0].EmittedState.Name : null), _ownedBlockInvocation);
            }
            if (!current && invalidate) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return current;
        }

        private static bool CaptureRegion(object producer, ImmutableArray<(object, StructuredSourceShape)>.Builder region, HashSet<object> ancestors, int depth, bool returnPosition = false, bool allowThis = false, bool allowDeclarations = false)
        {
            if (depth >= 32 || !ancestors.Add(producer) || !Allowed(producer, returnPosition, allowThis, allowDeclarations)) { return false; }
            var shape = StructuredSourceShape.Capture(producer, true);
            if (shape.Opaque) { return false; }
            region.Add((producer, shape));
            foreach (var child in shape.Children)
            {
                if (!CaptureRegion(child.Producer, region, ancestors, depth + 1, producer.GetType() == typeof(ExpressionStatement), allowThis, allowDeclarations)) { return false; }
            }
            ancestors.Remove(producer);
            return true;
        }

        private static bool Allowed(object producer, bool returnPosition, bool allowThis, bool allowDeclarations) => producer switch
        {
            ScopedApi wrapper => PreparedCallProofs.IsTransparentWrapper(wrapper),
            FuncExpression closure => ContractCollections.TryCopy(closure.Parameters, out var parameters) && parameters.Length == 0,
            KeywordExpression keyword => keyword.GetType() == typeof(KeywordExpression) &&
                (keyword.Expression is null && (keyword.Keyword is "null" or "default" || allowThis && keyword.Keyword == "this") ||
                    returnPosition && keyword.Keyword == "return" && keyword.Expression is not null),
            MemberExpression member => member.GetType() == typeof(MemberExpression) && member.Declaration is null,
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
            string RawName, bool ExactName, CultureInfo Culture,
            (long Variable, long Declaration, string Name) EmittedState, VariableExpression? Carrier)
        {
            internal bool IsCurrent(PocWorkBudget? budget = null)
            {
                if (!SourceCurrentnessChecks.Spend(budget) || Parameter.DeclarationRevision != Revision ||
                    !ReferenceEquals(Parameter.Type, Type) || !SourceCurrentnessChecks.Type(TypeSnapshot, budget)) { return false; }
                if (budget is null) { return Parameter.EmittedDeclarationState == EmittedState && (Carrier is null || ReferenceEquals(Parameter.CachedVariableExpression, Carrier)); }
                var variable = Parameter.CachedVariableExpression;
                if (Carrier is not null && !ReferenceEquals(variable, Carrier)) { return false; }
                var declaration = variable?.Declaration;
                if ((variable?.SourceRevision ?? 0) != EmittedState.Variable || (declaration?.SourceRevision ?? 0) != EmittedState.Declaration) { return false; }
                if (declaration?.RequestedName is { } emitted)
                { return SourceCurrentnessChecks.Text(emitted, EmittedState.Name, budget); }
                // No canonicalizer replay. Its observed fallback remains valid
                // only while actual input/revision/culture facts still match.
                return SourceCurrentnessChecks.Text(Parameter.Name, RawName, budget) &&
                    (Parameter.InputParameter?.IsExactName == true) == ExactName &&
                    (ExactName || Culture.GetType() == typeof(CultureInfo) && ReferenceEquals(CultureInfo.CurrentCulture, Culture));
            }
        }
    }
}
