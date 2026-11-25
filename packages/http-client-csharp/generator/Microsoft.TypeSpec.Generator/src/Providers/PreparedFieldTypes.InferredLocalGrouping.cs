// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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
        private bool CaptureInferredLocalGrouping(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            PreparedSourceOccurrence? root = null;
            foreach (var candidate in file.Roots)
            {
                if (candidate.Id.RootRole == "type-documentation") { continue; }
                if (root is not null) { return false; }
                root = candidate;
            }
            // No general declaration inference or change to the typed-local frame.
            if (root?.Producer is not MethodProvider method || method.GetType() != typeof(MethodProvider) ||
                method.BodyStatements is not MethodBodyStatements body || body.GetType() != typeof(MethodBodyStatements) ||
                !ContractCollections.TryCopy(body.Statements, out var statements) || statements.Length != 2 ||
                statements[0] is not ExpressionStatement { Expression: AssignmentExpression { UseNullCoalesce: false,
                    Variable: InferredLocalDeclarationExpression declaration, Value: ParenthesizedExpression { Operand: BinaryOperatorExpression
                        { Operator: "+", AddParentheses: true, Left: VariableExpression left, Right: VariableExpression right } binary } wrapper } assignment } initialized ||
                initialized.GetType() != typeof(ExpressionStatement) || statements[1] is not ExpressionStatement { Expression: KeywordExpression
                    { Keyword: "return", Expression: VariableExpression returned } keyword } returnedStatement ||
                returnedStatement.GetType() != typeof(ExpressionStatement) || keyword.GetType() != typeof(KeywordExpression) ||
                declaration.Variable is not { } local || !ReferenceEquals(local, returned) || local.GetType() != typeof(VariableExpression) ||
                !PocIntType(local.Type) || !PocIntType(method.Signature.ReturnType) || !PocSignature(method.Signature, true) ||
                method.Signature.HasUnsupportedBaseContract || method.Signature.OriginalSymbol is not null ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) || parameters.Length != 2 ||
                !IsActualPlainIntFormal(parameters[0], left) || !IsActualPlainIntFormal(parameters[1], right) ||
                local.Declaration?.RequestedName is not { } name || !Identifier(name) || name == "var" ||
                name == left.Declaration.RequestedName || name == right.Declaration.RequestedName ||
                left.Declaration.RequestedName == right.Declaration.RequestedName || !HasClosedInferredLocalInputs(file))
            { return false; }

            static PreparedSourceOccurrence? Child(PreparedSourceOccurrence? parent, int count, int index, object producer) =>
                parent is { IsOpaque: false } && parent.Children.Length == count && parent.Children[index] is { IsOpaque: false } child &&
                    ReferenceEquals(child.Producer, producer) ? child : null;
            var bodyNode = root.Children.FirstOrDefault(node => ReferenceEquals(node.Producer, body));
            var assignmentNode = Child(Child(bodyNode, 2, 0, initialized), 1, 0, assignment);
            var declarationNode = Child(assignmentNode, 2, 0, declaration);
            var wrapperNode = Child(assignmentNode, 2, 1, wrapper);
            var binaryNode = Child(wrapperNode, 1, 0, binary);
            var returnNode = Child(Child(bodyNode, 2, 1, returnedStatement), 1, 0, keyword);
            if (declarationNode is null || wrapperNode is null || binaryNode is null ||
                Child(declarationNode, 1, 0, local) is not { Children.IsEmpty: true } ||
                Child(returnNode, 1, 0, returned) is not { Children.IsEmpty: true } ||
                Child(binaryNode, 2, 0, left) is not { Children.IsEmpty: true } || Child(binaryNode, 2, 1, right) is not { Children.IsEmpty: true })
            { return false; }
            methods.Add(method);
            signatures.Add(method.Signature);
            nodes.Add(bodyNode!.Id);
            nodes.Add(assignmentNode!.Id);
            nodes.Add(declarationNode.Id);
            nodes.Add(wrapperNode.Id);
            nodes.Add(binaryNode.Id);
            choices.Add(wrapperNode);
            choices.Add(binaryNode);
            return true;
        }

        private bool CaptureInferredWideningCastGrouping(PreparedSourceFile file, HashSet<MethodProvider> methods,
            HashSet<MethodSignature> signatures, HashSet<SourceOccurrenceId> nodes, List<PreparedSourceOccurrence> choices)
        {
            PreparedSourceOccurrence? root = null;
            foreach (var candidate in file.Roots)
            {
                if (candidate.Id.RootRole == "type-documentation") { continue; }
                if (root is not null) { return false; }
                root = candidate;
            }
            if (root?.Producer is not MethodProvider method || method.GetType() != typeof(MethodProvider) ||
                method.BodyStatements is not MethodBodyStatements body || body.GetType() != typeof(MethodBodyStatements) ||
                !ContractCollections.TryCopy(body.Statements, out var statements) || statements.Length != 2 ||
                statements[0] is not ExpressionStatement { Expression: AssignmentExpression { UseNullCoalesce: false,
                    Variable: InferredLocalDeclarationExpression declaration, Value: CastExpression { Inner: VariableExpression operand } cast } assignment } initialized ||
                initialized.GetType() != typeof(ExpressionStatement) || statements[1] is not ExpressionStatement { Expression: KeywordExpression
                    { Keyword: "return", Expression: VariableExpression returned } keyword } returnedStatement ||
                returnedStatement.GetType() != typeof(ExpressionStatement) || keyword.GetType() != typeof(KeywordExpression) ||
                declaration.Variable is not { } local || !ReferenceEquals(local, returned) || local.GetType() != typeof(VariableExpression) ||
                !PreparedPrimitiveCastParent.IsLong(local.Type) || !PreparedPrimitiveCastParent.IsLong(cast.Type) ||
                !PreparedPrimitiveCastParent.IsLong(method.Signature.ReturnType) || !PocSignature(method.Signature, primitiveCastParent: true) ||
                method.Signature.HasUnsupportedBaseContract || method.Signature.OriginalSymbol is not null ||
                !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) || parameters.Length != 1 ||
                !IsActualPlainIntFormal(parameters[0], operand) || local.Declaration?.RequestedName is not { } name || !Identifier(name) || name == "var" ||
                name == operand.Declaration.RequestedName || !HasClosedInferredLocalInputs(file))
            { return false; }

            var compilation = (CSharpCompilation)file.Compilation;
            var source = compilation.GetSpecialType(SpecialType.System_Int32);
            var target = compilation.GetSpecialType(SpecialType.System_Int64);
            if (target.TypeKind != TypeKind.Struct || !SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, target.ContainingAssembly) ||
                compilation.ClassifyConversion(source, target) is not { IsImplicit: true, IsNumeric: true, IsUserDefined: false })
            { return false; }

            static PreparedSourceOccurrence? Child(PreparedSourceOccurrence? parent, int count, int index, object producer) =>
                parent is { IsOpaque: false } && parent.Children.Length == count && parent.Children[index] is { IsOpaque: false } child &&
                    ReferenceEquals(child.Producer, producer) ? child : null;
            var bodyNode = root.Children.FirstOrDefault(node => ReferenceEquals(node.Producer, body));
            var assignmentNode = Child(Child(bodyNode, 2, 0, initialized), 1, 0, assignment);
            var declarationNode = Child(assignmentNode, 2, 0, declaration);
            var castNode = Child(assignmentNode, 2, 1, cast);
            var returnNode = Child(Child(bodyNode, 2, 1, returnedStatement), 1, 0, keyword);
            if (declarationNode is null || castNode is null ||
                Child(declarationNode, 1, 0, local) is not { Children.IsEmpty: true } ||
                Child(returnNode, 1, 0, returned) is not { Children.IsEmpty: true } ||
                Child(castNode, 1, 0, operand) is not { Children.IsEmpty: true })
            { return false; }
            methods.Add(method);
            signatures.Add(method.Signature);
            nodes.Add(bodyNode!.Id);
            nodes.Add(assignmentNode!.Id);
            nodes.Add(declarationNode.Id);
            nodes.Add(castNode.Id);
            choices.Add(castNode);
            return true;
        }

        // The native provider automatically prints `partial` on top-level
        // classes. Exclude actual companions, not that syntactic modifier:
        // the compiler proof has no source trees and the inventory has one owner.
        private static bool InferredLocalOwnerIsUnsplit(PreparedSourceFile file) =>
            file.FileOwner.PartialDeclarationOwner is null && file.FileOwner.PartialDeclarationParts.IsEmpty;

        private bool HasClosedInferredLocalInputs(PreparedSourceFile file)
        {
            // This proof is deliberately core-only. With no source declarations,
            // no aliases and no imported scopes, this is a finite negative lookup,
            // not a C# binder. The native output must also observe no imports.
            if (_sourceInputs is null || !ReferenceEquals(CodeModelGenerator.Instance.CompilerBuildContext?.SourceProof, _sourceInputs) ||
                !ReferenceEquals(_sourceInputs.Compilation, file.Compilation) || file.Compilation is not CSharpCompilation compilation ||
                compilation.SyntaxTrees.Any() || !compilation.Options.Usings.IsEmpty ||
                _sourceRootSnapshot.Length != 1 || _callLookupScopes.Length != 1 || !ReferenceEquals(_sourceRootSnapshot[0], file.FileOwner) ||
                file.FileOwner.CachedType is not { } owner || owner.Namespace.Length == 0 || owner.Name == "var" ||
                !InferredLocalOwnerIsUnsplit(file))
            { return false; }
            var reference = compilation.References.Take(2).ToArray();
            if (reference.Length != 1 || !reference[0].Properties.Aliases.IsDefaultOrEmpty || reference[0].Properties.EmbedInteropTypes ||
                compilation.GetAssemblyOrModuleSymbol(reference[0]) is not IAssemblySymbol core || core.Modules.Count() != 1 ||
                !core.GetForwardedTypes().IsEmpty)
            { return false; }
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            var intType = compilation.GetSpecialType(SpecialType.System_Int32);
            var namespaceParts = owner.Namespace.Split('.');
            return objectType is { TypeKind: TypeKind.Class, BaseType: null } && intType.TypeKind == TypeKind.Struct &&
                SymbolEqualityComparer.Default.Equals(objectType.ContainingAssembly, core) && SymbolEqualityComparer.Default.Equals(intType.ContainingAssembly, core) &&
                objectType.GetMembers("var").IsEmpty && !core.GlobalNamespace.GetMembers("var").Any() &&
                !namespaceParts.Contains("var", StringComparer.Ordinal) && !core.GlobalNamespace.GetMembers(namespaceParts[0]).Any();
        }
    }
}
