// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // A POC-only prerequisite for ONE already-captured statement subtree.
    // It is not a file, binding, source-policy, lifetime, or spelling proof.
    internal sealed class PreparedPocBodyGrammar
    {
        private enum Slot
        {
            Statement,
            StatementExpression,
            Value,
            Receiver,
            Assignable,
            Argument,
            DeclarationTarget,
            ArrayInitializer
        }

        private sealed record Node(
            object Producer,
            StructuredSourceShape Shape,
            ImmutableArray<Node> Children);

        private readonly PreparedSourceFile? _file;
        private readonly PreparedSourceOccurrence? _occurrence;
        private readonly Node _root;
        private readonly bool _disableXmlDocs;
        private bool _stale;

        private PreparedPocBodyGrammar(
            PreparedSourceFile? file,
            PreparedSourceOccurrence? occurrence,
            Node root,
            bool disableXmlDocs)
        {
            _file = file;
            _occurrence = occurrence;
            _root = root;
            _disableXmlDocs = disableXmlDocs;
        }

        // Production entry: the occurrence must already belong to this file.
        // This only certifies its statement subtree, not the surrounding root.
        internal static PreparedPocBodyGrammar? TryCapture(
            PreparedSourceFile file,
            PreparedSourceOccurrence occurrence,
            PocWorkBudget budget,
            out string? failure)
        {
            failure = "file-or-occurrence-unavailable";
            if (file.IsInvalid || file.HasStarted ||
                occurrence.Producer is not MethodBodyStatement body ||
                !ContainsOccurrence(file, occurrence, budget))
            {
                return null;
            }

            var result = TryCaptureBody(body, file.DisableXmlDocs, budget, out failure);
            return result is null
                ? null
                : new PreparedPocBodyGrammar(file, occurrence, result._root, file.DisableXmlDocs);
        }

        // Useful for focused grammar controls. It does not establish file ownership.
        internal static PreparedPocBodyGrammar? TryCaptureBody(
            MethodBodyStatement body,
            bool disableXmlDocs,
            PocWorkBudget budget,
            out string? failure)
        {
            var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!TryNode(body, Slot.Statement, "$", 0, disableXmlDocs,
                    budget, ancestors, out var root, out failure))
            {
                return null;
            }

            // Construction witnesses are initial-capture provenance only.
            // Refuse the tree immediately if strict current-element capture
            // cannot independently match it.
            if (!Current(root!, disableXmlDocs, budget, 0))
            {
                failure = "$:strict-currentness-unavailable";
                return null;
            }

            failure = null;
            return new PreparedPocBodyGrammar(null, null, root!, disableXmlDocs);
        }

        internal bool IsCurrent(PocWorkBudget budget)
        {
            if (_stale) { return false; }
            if (_file is { IsInvalid: true } ||
                _occurrence is not null &&
                    !ReferenceEquals(_occurrence.Producer, _root.Producer) ||
                !Current(_root, _disableXmlDocs, budget, 0))
            {
                if (!budget.Exhausted) { _stale = true; }
                return false;
            }
            return true;
        }

        private static bool ContainsOccurrence(
            PreparedSourceFile file,
            PreparedSourceOccurrence target,
            PocWorkBudget budget)
        {
            var pending = new Stack<PreparedSourceOccurrence>();
            foreach (var root in file.Roots)
            {
                if (!budget.TrySpend()) { return false; }
                pending.Push(root);
            }

            while (pending.Count != 0)
            {
                if (!budget.TrySpend()) { return false; }
                var item = pending.Pop();
                if (ReferenceEquals(item, target)) { return true; }
                foreach (var child in item.Children)
                {
                    if (!budget.TrySpend()) { return false; }
                    pending.Push(child);
                }
            }

            return false;
        }

        private static bool TryNode(
            object? producer,
            Slot slot,
            string path,
            int depth,
            bool disableXmlDocs,
            PocWorkBudget budget,
            HashSet<object> ancestors,
            out Node? result,
            out string? failure)
        {
            result = null;
            failure = path + ":unsupported";

            // Ordinary arguments have the value grammar. ArgumentExpression is
            // the explicit modifier boundary; never admit it in a value slot.
            if (slot == Slot.Argument && producer is not ArgumentExpression) { slot = Slot.Value; }

            if (producer is null || depth >= 256 || !budget.TrySpend() ||
                !IsAdmittedRuntimeType(producer) || !ancestors.Add(producer))
            {
                return false;
            }

            try
            {
                // Check before shape capture: its call shape reads signature
                // metadata, and the witness does not prove current arguments.
                if (producer is InvokeMethodExpression call &&
                    ((call.MethodSignature is not null && call.MethodSignature.GetType() != typeof(MethodSignature)) ||
                     call.DelegateParameterIntent is not null ||
                     call.DeclaredContractIntent is not null ||
                     call.ExtensionType is not null ||
                     call.TypeArguments is not null ||
                     call.CallAsAsync))
                {
                    failure = path + ":call-metadata-or-async";
                    return false;
                }

                if (producer is NewInstanceExpression creation &&
                    (creation.ConstructorIntent is not null ||
                     creation.ConstructorProviderIntent is not null))
                {
                    failure = path + ":constructor-intent";
                    return false;
                }

                if (producer is LiteralExpression literal &&
                    !SafeLiteral(literal.Literal, budget))
                {
                    failure = path + ":literal";
                    return false;
                }

                var shape = StructuredSourceShape.CaptureForPocSupplement(
                    producer, disableXmlDocs, budget);
                if (budget.Exhausted || shape.Opaque || !shape.IntrinsicTokens)
                {
                    failure = path + ":" + (shape.UnavailableReason ?? "unsafe-tokens");
                    return false;
                }

                var children = ImmutableArray.CreateBuilder<Node>();
                var next = 0;
                string? childFailure = null;

                bool Child(string role, object? expected, Slot childSlot)
                {
                    if (expected is null || next >= shape.Children.Length)
                    {
                        childFailure = path + "/" + role + ":missing";
                        return false;
                    }

                    var edge = shape.Children[next++];
                    if (edge.Role != role || !ReferenceEquals(edge.Producer, expected))
                    {
                        childFailure = path + "/" + role + ":projection-mismatch";
                        return false;
                    }

                    if (!TryNode(expected, childSlot, path + "/" + role, depth + 1,
                            disableXmlDocs, budget, ancestors, out var child, out childFailure))
                    {
                        return false;
                    }

                    children.Add(child!);
                    return true;
                }

                bool Statements(string prefix, IReadOnlyList<MethodBodyStatement>? statements)
                {
                    if (!ContractCollections.TryCopyForPoc(statements, budget, out var copy))
                    {
                        childFailure = path + "/" + prefix + ":unowned-or-large";
                        return false;
                    }

                    for (int i = 0; i < copy.Length; i++)
                    {
                        if (!Child($"{prefix}[{i}]", copy[i], Slot.Statement)) { return false; }
                    }
                    return true;
                }

                bool Values(string prefix, IReadOnlyList<ValueExpression>? values, Slot childSlot)
                {
                    if (!ContractCollections.TryCopyForPoc(values, budget, out var copy))
                    {
                        childFailure = path + "/" + prefix + ":unowned-or-large";
                        return false;
                    }

                    for (int i = 0; i < copy.Length; i++)
                    {
                        if (!Child($"{prefix}[{i}]", copy[i], childSlot)) { return false; }
                    }
                    return true;
                }

                bool ok = false;

                if (ReferenceEquals(producer, MethodBodyStatement.Empty) ||
                    ReferenceEquals(producer, MethodBodyStatement.EmptyLine))
                {
                    ok = slot == Slot.Statement;
                }
                else
                {
                    switch (producer)
                    {
                        case MethodBodyStatements group:
                            ok = slot == Slot.Statement &&
                                Statements("statement", group.Statements);
                            break;

                        case ExpressionStatement statement:
                            ok = slot == Slot.Statement &&
                                Child("expression", statement.Expression, Slot.StatementExpression);
                            break;

                        case AssignmentExpression assignment:
                            if (slot != Slot.StatementExpression) { break; }
                            if (assignment.Variable is DeclarationExpression assignmentDeclaration)
                            {
                                // Native "using T x = value;" is admitted here,
                                // not as a general value or nested assignment.
                                ok = !assignment.UseNullCoalesce &&
                                    Child("target", assignmentDeclaration, Slot.DeclarationTarget) &&
                                    Child("value", assignment.Value, Slot.Value);
                            }
                            else
                            {
                                ok = Child("target", assignment.Variable, Slot.Assignable) &&
                                    Child("value", assignment.Value, Slot.Value);
                            }
                            break;

                        case DeclarationExpression declaration:
                            ok = slot == Slot.DeclarationTarget &&
                                !declaration.IsOut &&
                                !(declaration.IsUsing && declaration.IsConst) &&
                                declaration.Variable is not null &&
                                SafeType(declaration.Variable.Type, budget) &&
                                Child("variable", declaration.Variable, Slot.Assignable);
                            break;

                        case VariableExpression variable:
                            ok = slot is Slot.Value or Slot.Receiver or Slot.Assignable &&
                                SafeType(variable.Type, budget) &&
                                variable.Declaration is not null &&
                                Identifier(variable.Declaration.RequestedName, budget);
                            break;

                        case LiteralExpression:
                            ok = slot == Slot.Value;
                            break;

                        case KeywordExpression keyword:
                            if (slot == Slot.StatementExpression && keyword.Keyword == "return")
                            {
                                ok = keyword.Expression is null ||
                                    Child("value", keyword.Expression, Slot.Value);
                            }
                            else if (slot == Slot.Value &&
                                keyword.Keyword is "null" or "default" or "true" or "false" or "this")
                            {
                                ok = keyword.Expression is null;
                            }
                            else if (slot == Slot.Receiver &&
                                keyword.Keyword is "this" or "base")
                            {
                                ok = keyword.Expression is null;
                            }
                            break;

                        case TypeReferenceExpression typeReference:
                            // The null Static() placeholder is valid only where
                            // InvokeMethodExpression itself skips that receiver.
                            ok = slot == Slot.Receiver &&
                                !typeReference.IsStaticPlaceholder &&
                                SafeType(typeReference.Type, budget);
                            break;

                        case MemberExpression member:
                            if (slot is not (Slot.Value or Slot.Receiver or Slot.Assignable) ||
                                !Identifier(member.MemberName, budget) ||
                                member.Declaration is not null &&
                                    !Identifier(member.Declaration.RequestedName, budget))
                            {
                                break;
                            }

                            ok = member.Inner is null ||
                                Child("receiver", member.Inner, Slot.Receiver);
                            break;

                        case InvokeMethodExpression invocation:
                            // Match the native selected name. Signature/intent
                            // revisions stay in the captured shape; admitting
                            // their grammar does not certify method binding.
                            var emittedName = invocation.MethodSignature?.Name ?? invocation.MethodName;
                            if (slot is not (Slot.Value or Slot.Receiver or Slot.StatementExpression) ||
                                !Identifier(emittedName, budget) ||
                                emittedName == "nameof")
                            {
                                break;
                            }

                            var receiver = invocation.InstanceReference;
                            var omittedStatic = receiver is TypeReferenceExpression
                                { IsStaticPlaceholder: true };
                            ok = (receiver is null || omittedStatic ||
                                    Child("receiver", receiver, Slot.Receiver)) &&
                                Values("argument", invocation.Arguments, Slot.Argument) &&
                                InvocationArgumentsMatch(invocation, shape, budget);
                            break;

                        case ArgumentExpression argument:
                            if (slot != Slot.Argument ||
                                (argument.IsRef ? 1 : 0) +
                                (argument.IsOut ? 1 : 0) +
                                (argument.IsIn ? 1 : 0) > 1)
                            {
                                break;
                            }

                            ok = Child("value", argument.Expression,
                                argument.IsRef || argument.IsOut
                                    ? Slot.Assignable
                                    : Slot.Value);
                            break;

                        case NewInstanceExpression instance:
                            // Target-typed new and initializer branches are
                            // intentionally deferred.
                            ok = slot is Slot.Value or Slot.Receiver or Slot.StatementExpression &&
                                instance.InitExpression is null &&
                                SafeType(instance.Type, budget) &&
                                Values("argument", instance.Parameters, Slot.Argument);
                            break;

                        case NewArrayExpression array:
                            if (slot is not (Slot.Value or Slot.Receiver) ||
                                array.IsStackAlloc || !SafeType(array.Type, budget))
                            {
                                break;
                            }

                            if (array.Size is not null)
                            {
                                // Native ignores Items in this branch; reject a
                                // non-null ignored payload rather than infer it.
                                ok = array.Items is null &&
                                    Child("size", array.Size, Slot.Value);
                            }
                            else if (array.Items is null)
                            {
                                // Native Array.Empty<T>() branch.
                                ok = true;
                            }
                            else if (ContractCollections.TryCopyForPoc(
                                    array.Items.Elements, budget, out var elements))
                            {
                                // Native omits the initializer child when empty.
                                ok = elements.Length == 0 ||
                                    Child("initializer", array.Items, Slot.ArrayInitializer);
                            }
                            break;

                        case ArrayInitializerExpression initializer:
                            ok = slot == Slot.ArrayInitializer &&
                                Values("element", initializer.Elements, Slot.Value);
                            break;

                        case TupleExpression tuple:
                            ok = slot == Slot.Value &&
                                tuple.ValueExpressions is { Length: >= 2 } &&
                                Values("element", tuple.ValueExpressions, Slot.Value);
                            break;

                        case IndexerExpression indexer:
                            // Reject collection expressions and the native
                            // Index+Elements no-separator combination.
                            ok = slot is Slot.Value or Slot.Receiver or Slot.Assignable &&
                                indexer.Inner is not null &&
                                indexer.Index is not null &&
                                indexer.Elements is null &&
                                Child("receiver", indexer.Inner, Slot.Receiver) &&
                                Child("index", indexer.Index, Slot.Value);
                            break;

                        case IfStatement conditional:
                            // Inline/unbraced forms need separate embedded-
                            // statement and dangling-else proofs.
                            ok = slot == Slot.Statement &&
                                !conditional.Inline && conditional.AddBraces &&
                                Child("condition", conditional.Condition, Slot.Value) &&
                                Statements("body", conditional.SourceBody);
                            break;

                        case IfElseStatement conditional:
                            if (slot != Slot.Statement ||
                                !Child("if", conditional.If, Slot.Statement) ||
                                !ContractCollections.TryCopyForPoc(
                                    conditional.ElseIfs, budget, out var elseIfs))
                            {
                                break;
                            }

                            ok = true;
                            for (int i = 0; i < elseIfs.Length; i++)
                            {
                                if (!Child($"else-if[{i}]", elseIfs[i], Slot.Statement))
                                {
                                    ok = false;
                                    break;
                                }
                            }

                            if (ok && conditional.Else is not null)
                            {
                                ok = Child("else", conditional.Else, Slot.Statement);
                            }
                            break;
                    }
                }

                if (!ok || next != shape.Children.Length)
                {
                    failure = childFailure ?? path + ":category-or-unconsumed-child";
                    return false;
                }

                result = new Node(producer, shape, children.ToImmutable());
                failure = null;
                return true;
            }
            finally
            {
                ancestors.Remove(producer);
            }
        }

        private static bool Current(
            Node node,
            bool disableXmlDocs,
            PocWorkBudget budget,
            int depth)
        {
            if (depth >= 256 || !budget.TrySpend()) { return false; }

            if (node.Producer is LiteralExpression literal &&
                !SafeLiteral(literal.Literal, budget))
            {
                return false;
            }

            var current = StructuredSourceShape.CaptureForPocSupplementCurrent(
                node.Producer, disableXmlDocs, budget);

            if (current.Opaque || !current.IntrinsicTokens ||
                !node.Shape.MatchesForPoc(current, budget))
            {
                return false;
            }

            // CaptureForPocSupplementCurrent still uses the older invocation
            // argument witness. Independently compare actual strict elements.
            if (node.Producer is InvokeMethodExpression call &&
                !InvocationArgumentsMatch(call, node.Shape, budget))
            {
                return false;
            }

            foreach (var child in node.Children)
            {
                if (!Current(child, disableXmlDocs, budget, depth + 1)) { return false; }
            }
            return true;
        }

        private static bool InvocationArgumentsMatch(
            InvokeMethodExpression call,
            StructuredSourceShape shape,
            PocWorkBudget budget)
        {
            if (!ContractCollections.TryCopyForPoc(call.Arguments, budget, out var arguments))
            {
                return false;
            }

            int offset = call.InstanceReference is null ||
                call.InstanceReference is TypeReferenceExpression { IsStaticPlaceholder: true }
                ? 0 : 1;

            if (shape.Children.Length != offset + arguments.Length) { return false; }

            for (int i = 0; i < arguments.Length; i++)
            {
                var edge = shape.Children[offset + i];
                if (edge.Role != $"argument[{i}]" ||
                    !ReferenceEquals(edge.Producer, arguments[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAdmittedRuntimeType(object node)
        {
            if (ReferenceEquals(node, MethodBodyStatement.Empty) ||
                ReferenceEquals(node, MethodBodyStatement.EmptyLine))
            {
                return true;
            }

            var type = node.GetType();
            return type == typeof(MethodBodyStatements) ||
                type == typeof(ExpressionStatement) ||
                type == typeof(AssignmentExpression) ||
                type == typeof(DeclarationExpression) ||
                type == typeof(VariableExpression) ||
                type == typeof(LiteralExpression) ||
                type == typeof(KeywordExpression) ||
                type == typeof(TypeReferenceExpression) ||
                type == typeof(MemberExpression) ||
                type == typeof(InvokeMethodExpression) ||
                type == typeof(ArgumentExpression) ||
                type == typeof(NewInstanceExpression) ||
                type == typeof(NewArrayExpression) ||
                type == typeof(ArrayInitializerExpression) ||
                type == typeof(TupleExpression) ||
                type == typeof(IndexerExpression) ||
                type == typeof(IfStatement) ||
                type == typeof(IfElseStatement);
        }

        private static bool SafeLiteral(object? value, PocWorkBudget budget) => value switch
        {
            null => true,
            string text => budget.TrySpend(text.Length),
            char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal => true,
            float f => !float.IsNaN(f) && !float.IsInfinity(f),
            double d => !double.IsNaN(d) && !double.IsInfinity(d),
            _ => false
        };

        // Deliberately only non-generic, non-nested, non-array types. This
        // does not prove type binding or the validity of the selected spelling.
        private static bool SafeType(CSharpType? type, PocWorkBudget budget)
        {
            if (type is null || type.GetType() != typeof(CSharpType) ||
                type.IsLiteral || type.HasUnionMetadata ||
                type.DeclaringType is not null ||
                !Identifier(type.Name, budget) ||
                !Namespace(type.Namespace, budget) ||
                !ContractCollections.TryCopyForPoc(type.Arguments, budget, out var args) ||
                args.Length != 0)
            {
                return false;
            }

            if (!type.IsFrameworkType) { return true; }

            var runtime = type.FrameworkType;
            return runtime.GetType() == typeof(object).GetType() &&
                runtime != typeof(void) &&
                !runtime.IsArray && !runtime.IsPointer && !runtime.IsByRef &&
                !runtime.IsGenericType && !runtime.IsGenericParameter;
        }

        private static bool Namespace(string? text, PocWorkBudget budget)
        {
            if (string.IsNullOrEmpty(text) || !budget.TrySpend(text.Length)) { return false; }
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i != text.Length && text[i] != '.') { continue; }
                if (i == start || !IdentifierUncharged(text.Substring(start, i - start)))
                {
                    return false;
                }
                start = i + 1;
            }
            return true;
        }

        private static bool Identifier(string? text, PocWorkBudget budget) =>
            text is not null && budget.TrySpend(text.Length) && IdentifierUncharged(text);

        private static bool IdentifierUncharged(string text)
        {
            if (text.Length == 0 ||
                !(text[0] == '_' || text[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (!(c == '_' || c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
                {
                    return false;
                }
            }

            return SyntaxFacts.GetKeywordKind(text) == SyntaxKind.None &&
                SyntaxFacts.GetContextualKeywordKind(text) == SyntaxKind.None;
        }
    }
}
