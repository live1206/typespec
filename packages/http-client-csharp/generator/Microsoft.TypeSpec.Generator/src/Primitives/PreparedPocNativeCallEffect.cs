// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Isolated, non-authorizing point-in-time expression/statement-effect
    // experiment. This is NOT a member/file certificate, binding proof, native
    // consumption lease, resource certificate, or writer permission. Each
    // admitted native implementation composes ALL its emitting children.
    internal sealed class PreparedPocNativeCallEffect
    {
        private enum Role { Value, Statement, StatementValue, Declaration }
        private sealed record Node(object Producer, StructuredSourceShape Shape, ImmutableArray<Node> Children);
        private readonly Node _root;
        private readonly bool _disableXmlDocs;
        private bool _stale;
        private PreparedPocNativeCallEffect(Node root, bool disableXmlDocs)
        { _root = root; _disableXmlDocs = disableXmlDocs; }

        internal static PreparedPocNativeCallEffect? TryCapture(ValueExpression value, bool disableXmlDocs,
            PocWorkBudget budget, out string? failure)
        {
            return TryCaptureCore(value, Role.Value, disableXmlDocs, budget, out failure);
        }

        internal static PreparedPocNativeCallEffect? TryCaptureStatement(MethodBodyStatement statement, bool disableXmlDocs,
            PocWorkBudget budget, out string? failure) => TryCaptureCore(statement, Role.Statement, disableXmlDocs, budget, out failure);

        private static PreparedPocNativeCallEffect? TryCaptureCore(object value, Role role, bool disableXmlDocs,
            PocWorkBudget budget, out string? failure)
        {
            var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!Capture(value, role, "$", 0, disableXmlDocs, budget, ancestors, out var root, out failure)) { return null; }
            var result = new PreparedPocNativeCallEffect(root!, disableXmlDocs);
            if (!result.IsCurrent(budget)) { failure = "$:strict-currentness"; return null; }
            return result;
        }

        internal bool IsCurrent(PocWorkBudget budget)
        {
            if (_stale) { return false; }
            if (Current(_root, budget, 0)) { return true; }
            if (!budget.Exhausted) { _stale = true; }
            return false;
        }

        private bool Current(Node node, PocWorkBudget budget, int depth)
        {
            if (depth >= 256 || !budget.TrySpend()) { return false; }
            var current = StructuredSourceShape.CaptureForPocSupplementCurrent(node.Producer, _disableXmlDocs, budget);
            if (current.Opaque || !current.IntrinsicTokens || !node.Shape.MatchesForPoc(current, budget)) { return false; }
            foreach (var child in node.Children) { if (!Current(child, budget, depth + 1)) { return false; } }
            return true;
        }

        private static bool Capture(object producer, Role role, string path, int depth, bool disableXmlDocs,
            PocWorkBudget budget, HashSet<object> ancestors, out Node? result, out string? failure)
        {
            result = null;
            failure = path + ":unprofiled-native-effect:" + producer.GetType().Name;
            if (depth >= 256 || !budget.TrySpend() || !ancestors.Add(producer)) { return false; }
            try
            {
                // Only scalars actually emitted by these implementations are
                // grammar inputs. In particular VariableExpression does not emit
                // Type, and ScopedApi.WriteCore does not emit its wrapper Type.
                bool admitted = producer switch
                {
                    MethodBodyStatements group when group.GetType() == typeof(MethodBodyStatements) => role == Role.Statement,
                    ExpressionStatement statement when statement.GetType() == typeof(ExpressionStatement) => role == Role.Statement,
                    AssignmentExpression => role == Role.StatementValue,
                    DeclarationExpression declaration => role == Role.Declaration && declaration.Variable is { } declaredVariable &&
                        SafeType(declaredVariable.Type, budget),
                    KeywordExpression keyword when keyword.Keyword == "return" => role == Role.StatementValue,
                    ScopedApi wrapper when role is Role.Value or Role.StatementValue => (AttributeSnapshot.IsKnownWrapper(wrapper) ||
                        wrapper.GetType().IsGenericType && wrapper.GetType().GetGenericTypeDefinition() == typeof(ScopedApi<>) ||
                        ConstructedArgumentWitness.IsAuditedWrapper(wrapper)),
                    VariableExpression variable when variable.GetType() == typeof(VariableExpression) && role is Role.Value or Role.StatementValue =>
                        variable.Declaration is { } declaredName && Identifier(declaredName.RequestedName, budget),
                    MemberExpression member when role is Role.Value or Role.StatementValue => Identifier(member.Declaration?.RequestedName ?? member.MemberName, budget),
                    KeywordExpression keyword when role == Role.Value => keyword.Expression is null &&
                        keyword.Keyword is "this" or "base" or "null" or "true" or "false" or "default",
                    InvokeMethodExpression call when role is Role.Value or Role.StatementValue => Identifier(call.MethodSignature?.Name ?? call.MethodName, budget) &&
                        call.MethodName != "nameof" &&
                        // No type-argument formatter is assumed from its identity.
                        (call.TypeArguments is null || ContractCollections.TryCopyForPoc(call.TypeArguments, budget, out var types) && types.Length == 0) &&
                        (call.ExtensionType is null || Namespace(call.ExtensionType.Namespace, budget)),
                    // The existing POC shape verifies the actual native format
                    // parts, in-part quote/backslash escapes, numeric holes and
                    // exact carrier count. Every hole is still composed below.
                    FormattableStringExpression => role == Role.Value,
                    TypeReferenceExpression type => role == Role.Value && !type.IsStaticPlaceholder && SafeType(type.Type, budget),
                    _ => false
                };
                if (!admitted) { return false; }
                var shape = StructuredSourceShape.CaptureForPocSupplement(producer, disableXmlDocs, budget);
                if (shape.Opaque || !shape.IntrinsicTokens) { failure = path + ":shape:" + shape.UnavailableReason; return false; }
                if (!budget.TrySpend(shape.Children.Length + 1)) { return false; }
                var children = ImmutableArray.CreateBuilder<Node>(shape.Children.Length);
                foreach (var edge in shape.Children)
                {
                    var childRole = producer switch
                    {
                        MethodBodyStatements => Role.Statement,
                        ExpressionStatement => Role.StatementValue,
                        AssignmentExpression when edge.Role == "target" && edge.Producer is DeclarationExpression => Role.Declaration,
                        _ => Role.Value
                    };
                    if (!Capture(edge.Producer, childRole, path + "/" + edge.Role, depth + 1, disableXmlDocs, budget,
                        ancestors, out var child, out failure)) { return false; }
                    children.Add(child!);
                }
                result = new Node(producer, shape, children.ToImmutable());
                failure = null;
                return true;
            }
            finally { ancestors.Remove(producer); }
        }

        internal readonly record struct NativeStep(object Producer, int End);

        // The finite admitted implementations call Write on these children in
        // shape order. Keep occurrence positions even when producers are shared.
        internal bool TryCreateNativeSteps(PocWorkBudget account, out ImmutableArray<NativeStep> steps)
        {
            var builder = ImmutableArray.CreateBuilder<NativeStep>();
            bool Add(Node node, int depth)
            {
                if (depth >= 256 || !account.TrySpend(2)) { return false; }
                var index = builder.Count;
                builder.Add(new NativeStep(node.Producer, 0));
                foreach (var child in node.Children) { if (!Add(child, depth + 1)) { return false; } }
                builder[index] = new NativeStep(node.Producer, builder.Count);
                return true;
            }
            if (!Add(_root, 0) || !account.TrySpend(builder.Count)) { steps = []; return false; }
            steps = builder.ToImmutable(); return true;
        }

        private static bool SafeType(CSharpType? type, PocWorkBudget budget) =>
            type is not null && type.GetType() == typeof(CSharpType) && !type.IsLiteral && !type.HasUnionMetadata &&
            type.DeclaringType is null && Identifier(type.Name, budget) && Namespace(type.Namespace, budget) &&
            ContractCollections.TryCopyForPoc(type.Arguments, budget, out var args) && args.Length == 0 &&
            (!type.IsFrameworkType || type.FrameworkType.GetType() == typeof(object).GetType() &&
                !type.FrameworkType.IsArray && !type.FrameworkType.IsPointer && !type.FrameworkType.IsByRef &&
                !type.FrameworkType.IsGenericType && !type.FrameworkType.IsGenericParameter);

        private static bool Identifier(string? text, PocWorkBudget budget) =>
            !string.IsNullOrEmpty(text) && budget.TrySpend(text.Length) &&
            SyntaxFacts.IsValidIdentifier(text) && SyntaxFacts.GetKeywordKind(text) == SyntaxKind.None &&
            text.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) < 0;

        private static bool Namespace(string? text, PocWorkBudget budget)
        {
            if (string.IsNullOrEmpty(text) || !budget.TrySpend(text.Length)) { return false; }
            foreach (var part in text.Split('.')) { if (!Identifier(part, budget)) { return false; } }
            return true;
        }
    }
}
