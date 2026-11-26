// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Statements;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Finite direct-match capability for a normally captured method region.
    // A miss is NOT invalidation: the caller executes the original shape check.
    internal sealed class PreparedEqualityRegion
    {
        private readonly ExpressionStatement? _statement;
        private readonly long _statementRevision;
        private readonly KeywordExpression? _keyword;
        private readonly long _keywordSourceRevision;
        private readonly long _keywordDeclarationRevision;
        private readonly BinaryOperatorExpression _binary;
        private readonly long _binaryRevision;
        private readonly bool _parentheses;
        private readonly VariableState _left;
        private readonly VariableState _right;

        private PreparedEqualityRegion(ExpressionStatement? statement, KeywordExpression? keyword,
            BinaryOperatorExpression binary, VariableState left, VariableState right)
        {
            _statement = statement;
            _statementRevision = statement?.SourceRevision ?? 0;
            _keyword = keyword;
            _keywordSourceRevision = keyword?.SourceRevision ?? 0;
            _keywordDeclarationRevision = keyword?.DeclarationRevision ?? 0;
            _binary = binary;
            _binaryRevision = binary.SourceRevision;
            _parentheses = binary.AddParentheses;
            _left = left;
            _right = right;
        }

        internal static PreparedEqualityRegion? TryCapture(object body,
            ImmutableArray<(object Producer, StructuredSourceShape Shape)> region)
        {
            ExpressionStatement? statement = null;
            KeywordExpression? keyword = null;
            var binary = body as BinaryOperatorExpression;
            if (body is ExpressionStatement { Expression: KeywordExpression { Keyword: "return", Expression: BinaryOperatorExpression returned } k } s &&
                s.GetType() == typeof(ExpressionStatement))
            {
                statement = s; keyword = k; binary = returned;
            }
            if (binary is not { Operator: "==", Left: VariableExpression left, Right: VariableExpression right } ||
                left.GetType() != typeof(VariableExpression) || right.GetType() != typeof(VariableExpression)) { return null; }
            int start = statement is null ? 0 : 2;
            if (region.Length != start + 3 || !ReferenceEquals(region[start].Producer, binary) ||
                !ReferenceEquals(region[start + 1].Producer, left) || !ReferenceEquals(region[start + 2].Producer, right) ||
                statement is not null && (!ReferenceEquals(region[0].Producer, statement) || !ReferenceEquals(region[1].Producer, keyword))) { return null; }
            // Do not add a fast capability for arbitrary type implementations or
            // type argument carriers. Unsupported hints retain the original path.
            if (!VariableState.TryCapture(left, out var l) || !VariableState.TryCapture(right, out var r)) { return null; }
            return new(statement, keyword, binary, l, r);
        }

        internal bool MatchesCurrent()
        {
            // Same region preorder as the original shape checks. These are only
            // native fields; signature/parameter callbacks have already run.
            if (_statement is not null && (_statement.SourceRevision != _statementRevision ||
                !ReferenceEquals(_statement.Expression, _keyword))) { return false; }
            if (_keyword is not null && (_keyword.SourceRevision != _keywordSourceRevision ||
                _keyword.DeclarationRevision != _keywordDeclarationRevision || _keyword.Keyword != "return" ||
                !ReferenceEquals(_keyword.Expression, _binary))) { return false; }
            return _binary.SourceRevision == _binaryRevision && _binary.Operator == "==" && _binary.AddParentheses == _parentheses &&
                ReferenceEquals(_binary.Left, _left.Variable) && ReferenceEquals(_binary.Right, _right.Variable) &&
                _left.MatchesCurrent() && _right.MatchesCurrent();
        }

        private readonly record struct VariableState(VariableExpression Variable, long Revision, CSharpType Type,
            long TypeRevision, string TypeName, string TypeNamespace, CodeWriterDeclaration Declaration, long DeclarationRevision, string RequestedName)
        {
            internal static bool TryCapture(VariableExpression variable, out VariableState state)
            {
                state = default;
                var type = variable.Type;
                if (type?.GetType() != typeof(CSharpType) || !type.IsFrameworkType || !ReferenceEquals(type.FrameworkType, typeof(int)) ||
                    type.DeclaringType is not null || !KnownEmpty(type.Arguments) || variable.Declaration is not { } declaration) { return false; }
                state = new(variable, variable.SourceRevision, type, type.IdentityVersion, type.Name, type.Namespace,
                    declaration, declaration.SourceRevision, declaration.RequestedName);
                return true;
            }
            internal bool MatchesCurrent() => Variable.SourceRevision == Revision && ReferenceEquals(Variable.Type, Type) &&
                Type.IdentityVersion == TypeRevision && Type.Name == TypeName && Type.Namespace == TypeNamespace &&
                Type.DeclaringType is null && KnownEmpty(Type.Arguments) && ReferenceEquals(Variable.Declaration, Declaration) &&
                Declaration.SourceRevision == DeclarationRevision && Declaration.RequestedName == RequestedName;
        }

        private static bool KnownEmpty(IReadOnlyList<CSharpType> items) => items switch
        {
            CSharpType[] array => array.Length == 0,
            List<CSharpType> list when list.GetType() == typeof(List<CSharpType>) => list.Count == 0,
            ImmutableArray<CSharpType> array => !array.IsDefault && array.IsEmpty,
            _ => false
        };
    }
}
