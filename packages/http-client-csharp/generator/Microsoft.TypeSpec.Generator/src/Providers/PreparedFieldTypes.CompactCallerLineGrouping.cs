// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private const string CallerLineImport = "System.Runtime.CompilerServices";
        private PreparedSourceFile? _compactCallerLineFile;

        internal void ObserveCompactCallerLineImports(PreparedSourceFile? file, HashSet<string> imports)
        {
            if (file is not null && ReferenceEquals(file, _compactCallerLineFile) &&
                (imports.Count != 1 || !imports.Contains(CallerLineImport)))
            { file.Invalidate(); }
        }

        private MethodProvider? CaptureCompactCallerLineGrouping(PreparedSourceFile file, HashSet<SourceOccurrenceId> supported,
            List<PreparedSourceOccurrence> choices)
        {
            var roots = file.Roots.Where(node => node.Id.RootRole != "type-documentation").Take(3).ToArray();
            if (roots.Length != 2 || roots[0].Producer is not MethodProvider entry || entry.GetType() != typeof(MethodProvider) ||
                roots[1].Producer is not MethodProvider line || line.GetType() != typeof(MethodProvider) ||
                entry.BodyStatements is not AuthoredCompactCastLocalStatement statement ||
                !ReferenceEquals(entry.EnclosingType, file.FileOwner) || !ReferenceEquals(line.EnclosingType, file.FileOwner) ||
                entry.Signature.Name != "M" || entry.Signature.Modifiers != MethodSignatureModifiers.Public ||
                !PocSignature(entry.Signature, true) || !PocIntType(entry.Signature.ReturnType) || entry.Signature.HasUnsupportedBaseContract ||
                entry.Signature.OriginalSymbol is not null || !ContractCollections.TryCopy(entry.Signature.Parameters, out var parameters) || parameters.Length != 1 ||
                !ContractCollections.TryCopy(entry.Suppressions, out var entrySuppressions) || !entrySuppressions.IsEmpty ||
                !ContractCollections.TryCopy(line.Suppressions, out var lineSuppressions) || !lineSuppressions.IsEmpty)
            { return null; }

            // Decline malformed authored children before reading their shape.
            // Preparation must not replace the ordinary native write failure.
            if (statement.Initializer is not { Operand: { Operand: { Operand: { } formal } cast } inner } outer ||
                statement.Declaration is not { Variable: { } local } ||
                outer.Before != "/*a*/" || inner.After != "/*b*/" ||
                !IsActualPlainIntFormal(parameters[0], formal) || formal.Declaration.RequestedName != "value" ||
                local.GetType() != typeof(VariableExpression) || !PocIntType(local.Type) || local.Declaration?.RequestedName != "result" ||
                statement.Return is not { Keyword: "return", Expression: AuthoredCompactLocalAdditionExpression sum } ||
                !ReferenceEquals(sum.Local, local) || sum.Call is not { MethodName: "Line", InstanceReference: null, MethodSignature: null,
                    TypeArguments: null, ExtensionType: null, CallAsAsync: false, DeclaredContractIntent: null,
                    MethodProviderIntent: null, DelegateParameterIntent: null, IsReducedExtensionIntent: false } call ||
                !ContractCollections.TryCopy(call.Arguments, out var arguments) || !arguments.IsEmpty ||
                !HasExactCallerLineMethod(line) || !HasClosedInferredLocalInputs(file) || !HasCanonicalCallerLineImport(file))
            { return null; }

            // The closed written owner has no competing declaration. The
            // unqualified zero-argument call can only bind its one static Line;
            // only the two authored expression pairs can change its column.
            var body = roots[0].Children.FirstOrDefault(node => ReferenceEquals(node.Producer, statement));
            var outerNode = Child(body, 3, 1, outer);
            var innerNode = Child(outerNode, 1, 0, inner);
            var castNode = Child(innerNode, 1, 0, cast);
            var declaration = Child(body, 3, 0, statement.Declaration);
            var returned = Child(body, 3, 2, statement.Return);
            var addition = Child(returned, 1, 0, sum);
            if (body is null || outerNode is null || innerNode is null || Child(castNode, 1, 0, formal) is null ||
                Child(declaration, 1, 0, local) is null || Child(addition, 2, 0, local) is null || Child(addition, 2, 1, call) is null ||
                roots.Any(root => root.DescendantsAndSelf().Any(node => node.IsOpaque)))
            { return null; }

            foreach (var node in roots.SelectMany(root => root.DescendantsAndSelf())) { supported.Add(node.Id); }
            choices.Add(outerNode);
            choices.Add(innerNode);
            return line;
        }

        private static PreparedSourceOccurrence? Child(PreparedSourceOccurrence? parent, int count, int index, object producer) =>
            parent is { IsOpaque: false } && parent.Children.Length == count && parent.Children[index] is { IsOpaque: false } child &&
            ReferenceEquals(child.Producer, producer) ? child : null;

        private static bool HasExactCallerLineMethod(MethodProvider line)
        {
            var signature = line.Signature;
            if (signature.Name != "Line" || signature.Modifiers != (MethodSignatureModifiers.Private | MethodSignatureModifiers.Static) ||
                signature.NonDocumentComment is not null || !PocIntType(signature.ReturnType) || signature.ExplicitInterface is not null ||
                signature.GenericArguments is not null || signature.GenericParameterConstraints is not null || signature.HasUnsupportedBaseContract ||
                signature.OriginalSymbol is not null || !ContractCollections.TryCopy(signature.Attributes, out var methodAttributes) || !methodAttributes.IsEmpty ||
                !ContractCollections.TryCopy(signature.Parameters, out var parameters) || parameters.Length != 1 ||
                line.BodyStatements is not null || line.BodyExpression is not VariableExpression operand || !IsActualPlainIntFormal(parameters[0], operand))
            { return false; }
            var parameter = parameters[0];
            var defaultValue = parameter.DefaultValue is ScopedApi<int> wrapper && PreparedCallProofs.IsTransparentWrapper(wrapper)
                ? wrapper.Original : parameter.DefaultValue;
            return parameter.Name == "line" && operand.Declaration.RequestedName == "line" && defaultValue is LiteralExpression { Literal: 0 } &&
                !parameter.IsRef && !parameter.IsOut && !parameter.IsIn && !parameter.IsParams && parameter.Field is null && parameter.Property is null &&
                parameter.InputParameter is null && ContractCollections.TryCopy(parameter.Attributes, out var attributes) &&
                attributes is [{ Data: null, CachedType: { } type, CachedArguments: { } args, CachedPositionalArguments: { } positional }] &&
                type.GetType() == typeof(CSharpType) && type.IsFrameworkType && ReferenceEquals(type.FrameworkType, typeof(CallerLineNumberAttribute)) &&
                type.Name == nameof(CallerLineNumberAttribute) && type.Namespace == CallerLineImport && type.DeclaringType is null && !type.IsNullable &&
                ContractCollections.TryCopy(type.Arguments, out var typeArgs) && typeArgs.IsEmpty &&
                ContractCollections.TryCopy(args, out var attributeArgs) && attributeArgs.IsEmpty &&
                ContractCollections.TryCopy(positional, out var namedArgs) && namedArgs.IsEmpty;
        }

        private static bool HasCanonicalCallerLineImport(PreparedSourceFile file)
        {
            if (file.Compilation is not CSharpCompilation compilation ||
                compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly is not { } core ||
                core.GetTypeByMetadataName(CallerLineImport + "." + nameof(CallerLineNumberAttribute)) is not { DeclaredAccessibility: Accessibility.Public } attribute)
            { return false; }
            var scope = attribute.ContainingNamespace;
            return scope.ToDisplayString() == CallerLineImport && !scope.GetMembers("var").Any() &&
                attribute.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.IsEmpty);
        }
    }
}
