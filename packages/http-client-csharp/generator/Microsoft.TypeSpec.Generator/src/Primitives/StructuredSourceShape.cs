// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Expressions;
using Microsoft.TypeSpec.Generator.Input;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Snippets;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Utilities;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>A bounded observation adapter, not a visitor, binder or emission/discovery pass.</summary>
    internal sealed class StructuredSourceShape
    {
        internal ImmutableArray<SourceChild> Children { get; private set; } = [];
        internal bool Opaque { get; private set; }
        internal string? UnavailableReason { get; private set; }
        internal bool IntrinsicTokens { get; private set; } = true;
        // Passive native-operand observation. Only canonical POC attribute
        // admission consumes this; shared Task03 shape policy is unchanged.
        internal bool HasMissingRequiredOperand { get; private set; }
        internal bool IntrinsicLineBreaks { get; private set; }
        internal bool HasDirectives { get; private set; }
        internal bool IntrinsicLayoutKnown { get; private set; } = true;
        private bool _pocVeto;
        private readonly bool _defensivePoc;
        private readonly bool _pocSupplement;
        private readonly bool _allowConstructionCollections;
        private readonly PocWorkBudget? _budget;
        private PocTypeTextMemo? _typeTextMemo;
        [Flags]
        private enum ShapeStorage
        {
            Values = 1, References = 2, Types = 4, Declarations = 8, Metadata = 16, Children = 32,
            All = Values | References | Types | Declarations | Metadata | Children
        }

        private readonly List<object?>? _values;
        private readonly List<object?>? _references;
        private readonly List<CSharpType>? _types;
        private readonly List<CodeWriterDeclaration>? _declarations;
        private readonly List<AttributeSnapshot>? _metadataAttributes;
        private ImmutableArray<DeclaredTypeSnapshot> _typeSnapshots;
        private ImmutableArray<(CodeWriterDeclaration Declaration, long Revision, string Name)> _declarationSnapshots;
        private readonly List<SourceChild>? _children;
        private int ValueCount => _values?.Count ?? 0;
        private int ReferenceCount => _references?.Count ?? 0;
        private int TypeCount => _types?.Count ?? 0;
        private int DeclarationCount => _declarations?.Count ?? 0;
        private int MetadataCount => _metadataAttributes?.Count ?? 0;

        internal static StructuredSourceShape Capture(object node, bool disableXmlDocs) => CaptureCore(node, disableXmlDocs, false, null);
        internal static StructuredSourceShape CaptureForPocSupplement(object node, bool disableXmlDocs) => CaptureForPocSupplement(node, disableXmlDocs, new PocWorkBudget(4096));
        // Construction collection provenance is available only for initial capture.
        internal static StructuredSourceShape CaptureForPocSupplement(object node, bool disableXmlDocs, PocWorkBudget budget) =>
            CaptureCore(node, disableXmlDocs, true, budget, allowConstructionCollections: true);
        internal static StructuredSourceShape CaptureForPocSupplementCurrent(object node, bool disableXmlDocs, PocWorkBudget budget) =>
            CaptureCore(node, disableXmlDocs, true, budget);
        // Preserve the shared graph's opacity while safely rejecting malformed
        // data seen by an additional POC-only currentness check.
        internal static StructuredSourceShape CaptureForPocRecheck(object node, bool disableXmlDocs) => CaptureForPocRecheck(node, disableXmlDocs, new PocWorkBudget(4096))!;
        internal static StructuredSourceShape? CaptureForPocRecheck(object node, bool disableXmlDocs, PocWorkBudget budget, PocTypeTextMemo? textMemo = null)
        {
            // Charge the shape and fixed type classification first; then
            // reserve every list this exact built-in profile can use before
            // constructing storage or reading any producer properties.
            if (!budget.TrySpend()) { return null; }
            var (storage, lists) = node switch
            {
                LiteralExpression => (ShapeStorage.Values | ShapeStorage.References, 2),
                KeywordExpression => (ShapeStorage.Values | ShapeStorage.References | ShapeStorage.Children, 3),
                ExpressionStatement statement when statement.GetType() == typeof(ExpressionStatement) =>
                    (ShapeStorage.Values | ShapeStorage.References | ShapeStorage.Children, 3),
                MethodBodyStatements statements when statements.GetType() == typeof(MethodBodyStatements) =>
                    (ShapeStorage.Values | ShapeStorage.References | ShapeStorage.Children, 3),
                VariableExpression variable when variable.GetType() == typeof(VariableExpression) =>
                    (ShapeStorage.Values | ShapeStorage.References | ShapeStorage.Types | ShapeStorage.Declarations, 4),
                _ => (ShapeStorage.All, 6)
            };
            if (!budget.TrySpend(lists)) { return null; }
            var shape = new StructuredSourceShape(budget, false, false, storage);
            if (node is LiteralExpression)
            {
                // A literal records the boxed source revision and either a
                // value or opaque reference. Reserve the exact backing slots
                // and revision box before reading any producer properties.
                if (!budget.TrySpend(4)) { shape.RejectPoc("POC work budget exhausted."); return shape; }
                shape._values!.Capacity = 2;
                shape._references!.Capacity = 1;
            }
            else if (node is KeywordExpression)
            {
                // The keyword records two boxed revisions, three values and
                // at most one reference and child. Fund their backing slots
                // before reading producer properties.
                if (!budget.TrySpend(7)) { shape.RejectPoc("POC work budget exhausted."); return shape; }
                shape._values!.Capacity = 3;
                shape._references!.Capacity = 1;
                shape._children!.Capacity = 1;
            }
            shape._typeTextMemo = textMemo;
            try { return CaptureCore(node, disableXmlDocs, false, budget, initialChargePaid: true, storage: storage, preparedShape: shape); }
            finally { shape._typeTextMemo = null; }
        }

        private StructuredSourceShape(PocWorkBudget? budget, bool pocSupplement, bool allowConstructionCollections, ShapeStorage storage)
        {
            _budget = budget; _defensivePoc = budget is not null; _pocSupplement = pocSupplement; _allowConstructionCollections = allowConstructionCollections;
            _values = (storage & ShapeStorage.Values) != 0 ? [] : null;
            _references = (storage & ShapeStorage.References) != 0 ? [] : null;
            _types = (storage & ShapeStorage.Types) != 0 ? [] : null;
            _declarations = (storage & ShapeStorage.Declarations) != 0 ? [] : null;
            _metadataAttributes = (storage & ShapeStorage.Metadata) != 0 ? [] : null;
            _children = (storage & ShapeStorage.Children) != 0 ? [] : null;
        }

        private static StructuredSourceShape CaptureCore(object node, bool disableXmlDocs, bool pocSupplement, PocWorkBudget? budget, bool allowConstructionCollections = false, bool initialChargePaid = false, ShapeStorage storage = ShapeStorage.All, StructuredSourceShape? preparedShape = null)
        {
            var s = preparedShape ?? new StructuredSourceShape(budget, pocSupplement, allowConstructionCollections, storage);
            var defensivePoc = budget is not null;
            if (budget is not null && !initialChargePaid && !budget.TrySpend()) { s.Fail("POC work budget exhausted."); return s; }
            // Every access below is statically typed, nonvirtual, and producer-owned. The POC-only
            // wrapper exception requires a registered exact type with audited inherited dispatch.
            if (node is ValueExpression expression) { s.Value(expression.SourceRevision); }
            if (node is MethodBodyStatement statement) { s.Value(statement.SourceRevision); }
            switch (node)
            {
                case InvokeMethodExpression n:
                    s.Value(n.DeclarationRevision); s.Value(n.MethodName); s.Ref(n.MethodSignature); s.Ref(n.MethodProviderIntent);
                    s.Ref(n.DelegateParameterIntent); s.Ref(n.DeclaredContractIntent); s.Value(n.IsReducedExtensionIntent);
                    s.Value(n.CallAsAsync); s.Value(n.AddConfigureAwaitFalse); s.Type(n.ExtensionType);
                    if (n.MethodSignature is { } signature) { s.SignatureMetadata(signature); }
                    if (n.MethodProviderIntent is { } intendedMethod)
                    {
                        s.Value(intendedMethod.DeclarationRevision);
                        if (defensivePoc && intendedMethod.Signature is null) { s.RejectPoc("Missing intended method signature."); }
                        else { s.SignatureMetadata(intendedMethod.Signature); }
                    }
                    if (n.DelegateParameterIntent is { } parameter) { s.Value(parameter.DeclarationRevision); }
                    if (n.InstanceReference is { } receiver)
                    {
                        // The singleton Static() receiver writes nothing and is skipped by Invoke.Write.
                        if (receiver is not TypeReferenceExpression { IsStaticPlaceholder: true }) { s.Child("receiver", receiver); }
                        else { s.Ref(receiver); }
                    }
                    s.Types(n.TypeArguments);
                    if (pocSupplement && allowConstructionCollections && n.Arguments is not ProducerOwnedReadOnlyList<ValueExpression> &&
                        ConstructedArgumentWitness.Read(n) is { } witnessedArguments)
                    {
                        s.Ref(n.Arguments);
                        if (s._budget is not null && !s._budget.TrySpend(witnessedArguments.Length)) { s.Fail("POC work budget exhausted."); }
                        else
                        {
                            for (int i = 0; i < witnessedArguments.Length; i++)
                            { if (witnessedArguments[i] is { } argument) { s.Child($"argument[{i}]", argument); } else { s.Fail("Missing witnessed argument."); } }
                        }
                    }
                    else { s.Nodes("argument", n.Arguments); }
                    var writtenMethodName = n.MethodSignature?.Name ?? n.MethodName;
                    // nameof is an audited raw contextual token; its argument
                    // capture, grammar and binding remain separate facts.
                    s.IntrinsicTokens &= pocSupplement && (s.IsPocSafeRawIdentifier(writtenMethodName) || writtenMethodName == "nameof");
                    s.IntrinsicLayoutKnown = false; break;
                case NewInstanceExpression n:
                    s.Type(n.Type); s.Ref(n.ConstructorIntent); s.Ref(n.ConstructorProviderIntent);
                    if (n.ConstructorIntent is { } ctor) { s.SignatureMetadata(ctor); s.Type(ctor.Type); }
                    if (n.ConstructorProviderIntent is { } intendedConstructor)
                    {
                        s.Value(intendedConstructor.DeclarationRevision);
                        if (defensivePoc && intendedConstructor.Signature is null) { s.RejectPoc("Missing intended constructor signature."); }
                        else { s.SignatureMetadata(intendedConstructor.Signature); s.Type(intendedConstructor.Signature.Type); }
                    }
                    s.Nodes("argument", n.Parameters); s.Child("initializer", n.InitExpression);
                    s.IntrinsicLayoutKnown = false; break; // actual wrapping/initializer edits still need a change contract
                case FuncExpression n:
                    if (s.TryCopy(n.Parameters, out var parameters))
                    {
                        foreach (var p in parameters) { s.Declaration(p); }
                    }
                    else { s.Fail("Unowned lambda parameter list."); }
                    if (n.BlockBody is { } block) { s.Child("block-body", block); s.IntrinsicLineBreaks = true; }
                    else { s.Child("expression-body", n.Inner); }
                    break;
                case ArgumentExpression n:
                    s.Value(n.DeclarationRevision); s.Value(n.IsRef); s.Value(n.IsOut); s.Value(n.IsIn); s.RequiredChild("value", n.Expression); break;
                case PositionalParameterReferenceExpression n:
                    s.Value(n.ParameterName); if (!s.SimpleIdentifier(n.ParameterName)) { s.IntrinsicTokens = false; s.IntrinsicLayoutKnown = false; }
                    s.Ref(n.ReferencedParameter); s.Value(n.ReferencedParameter?.DeclarationRevision);
                    s.RequiredChild("value", n.ParameterValue); break;
                case KeywordExpression n:
                    if (defensivePoc && (n.Keyword is null || !s._budget!.TrySpend(n.Keyword.Length)))
                    { s.RejectPoc("Missing or oversized keyword text."); break; }
                    s.Value(n.DeclarationRevision); s.Value(n.Keyword); s.Child("value", n.Expression);
                    s.IntrinsicTokens = n.Keyword is "null" or "default" or "this" or "base" or "true" or "false" or "return" or "throw" or "break" or "continue" or "_" or "checked" ||
                        pocSupplement && n.Keyword is ("value" or "yield");
                    s.IntrinsicLayoutKnown = s.IntrinsicTokens;
                    break;
                case LiteralExpression n:
                    if (n.Literal is null or string or char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                    { s.Value(n.Literal); }
                    else { s.Ref(n.Literal); s.Fail("Opaque literal formatter/value."); }
                    break;
                case TypeReferenceExpression n: s.Type(n.Type); break;
                case TypeOfExpression n when n.GetType() == typeof(TypeOfExpression): s.Type(n.Type); break;
                case VariableExpression n when n.GetType() == typeof(VariableExpression): s.Type(n.Type); s.Declaration(n.Declaration); break;
                case MemberExpression n:
                    s.Child("receiver", n.Inner); s.Value(n.MemberName); s.Declaration(n.Declaration);
                    s.IntrinsicTokens &= pocSupplement && (n.Declaration is not null || s.IsPocSafeRawIdentifier(n.MemberName));
                    s.IntrinsicLayoutKnown = false; break;
                case DeclarationExpression n:
                    s.Value(n.IsOut); s.Value(n.IsUsing); s.Value(n.IsConst); s.RequiredChild("variable", n.Variable); break;
                case AssignmentExpression n:
                    s.Value(n.UseNullCoalesce); s.RequiredChild("target", n.Variable); s.RequiredChild("value", n.Value); break;
                case BinaryOperatorExpression n:
                    s.Value(n.Operator); s.Value(n.AddParentheses); s.RequiredChild("left", n.Left); s.RequiredChild("right", n.Right);
                    s.IntrinsicTokens &= pocSupplement && s.IsPocSafeBinaryOperator(n.Operator);
                    s.IntrinsicLayoutKnown = false; break;
                case UnaryOperatorExpression n:
                    s.Value(n.Operator); s.Value(n.OperandOnTheLeft); s.RequiredChild("operand", n.Operand);
                    s.IntrinsicTokens &= pocSupplement && s.IsPocSafeUnaryOperator(n.Operator, n.OperandOnTheLeft);
                    s.IntrinsicLayoutKnown = false; break;
                case CastExpression n: s.Type(n.Type); s.RequiredChild("value", n.Inner); break;
                case AsExpression n: s.Type(n.Type); s.RequiredChild("value", n.Inner); break;
                case NullConditionalExpression n: s.RequiredChild("value", n.Inner); break;
                case TernaryConditionalExpression n:
                    s.RequiredChild("condition", n.Condition); s.RequiredChild("consequent", n.Consequent); s.RequiredChild("alternative", n.Alternative); break;
                case IndexerExpression n:
                    s.Child("receiver", n.Inner); s.Child("index", n.Index); if (n.Elements is not null) { s.Nodes("element", n.Elements); } break;
                case NewArrayExpression n:
                    s.Type(n.Type); s.Value(n.IsStackAlloc);
                    if (n.Size is not null) { s.Child("size", n.Size); s.Ref(n.Items); }
                    else if (n.Items is { } items)
                    {
                        if (items.Elements is null) { s.Ref(items); }
                        else if (s.TryCopy(items.Elements, out var elements))
                        { if (elements.Length > 0) { s.Child("initializer", items); } else { s.Ref(items); } }
                        else { s.Fail("Unowned array initializer elements."); }
                    }
                    break;
                case ArrayInitializerExpression n:
                    s.Value(n.IsInline); if (n.Elements is not null) { s.Nodes("element", n.Elements); }
                    s.IntrinsicLineBreaks = !n.IsInline && s._children!.Count > 0; break;
                case DictionaryInitializerExpression n: s.Dictionary(n.Values); s.IntrinsicLineBreaks = s._children!.Count > 0; break;
                case ObjectInitializerExpression n when n.GetType() == typeof(ObjectInitializerExpression):
                    s.Value(n.UseSingleLine); s.Dictionary(n.Values); s.IntrinsicLineBreaks = !n.UseSingleLine && s._children!.Count > 0; break;
                case CollectionInitializerExpression n: s.Nodes("element", n.Items); break;
                case FormattableStringExpression n:
                    s.Value(n.Format); s.Nodes("hole", n.Arguments);
                    // The native formatter writes literal parts raw inside $"...".
                    // Only a narrow POC character profile is known; child
                    // expressions, layout and the containing grammar remain separate.
                    s.IntrinsicTokens &= pocSupplement && s.HasSimplePocInterpolatedParts(n);
                    s.IntrinsicLayoutKnown = false; break;
                case ScopedApi n when AttributeSnapshot.IsKnownWrapper(n) ||
                    n.GetType().IsGenericType && n.GetType().GetGenericTypeDefinition() == typeof(ScopedApi<>) ||
                    pocSupplement && ConstructedArgumentWitness.IsAuditedWrapper(n):
                    s.Value(n.DeclarationRevision); s.Type(n.Type); s.Child("value", n.Original); break;
                case DictionaryExpression n: s.Type(n.Type); s.Child("value", n.Original); break;
                case ListExpression n: s.Type(n.ElementType); s.Child("value", n.Original); break;
                case KeyValuePairExpression n: s.Type(n.KeyType); s.Type(n.ValueType); s.Child("value", n.Original); break;
                case IndexableExpression n when n.GetType() == typeof(IndexableExpression): s.Child("value", n.Original); break;
                case TupleExpression n when n.GetType() == typeof(TupleExpression): s.Nodes("element", n.ValueExpressions); break;
                case VariableTupleExpression n when n.GetType() == typeof(VariableTupleExpression):
                    s.Value(n.IsRef); s.Nodes("variable", n.Variables, false); break;
                case WhereExpression n: s.Type(n.Type); s.Nodes("constraint", n.Constraints); break;
                case SwitchExpression n: s.Child("match", n.MatchExpression); s.Nodes("case", n.Cases); s.IntrinsicLineBreaks = true; break;
                case SwitchCaseExpression n: s.Child("match", n.Case); s.Child("value", n.Expression); break;
                case SwitchCaseWhenExpression n: s.Child("match", n.Case); s.Child("condition", n.Condition); break;
                case TryExpression n: s.FlattenedBody(n.SourceBody); s.IntrinsicLineBreaks = true; break;
                case CatchExpression n: s.Child("exception", n.Exception); s.FlattenedBody(n.SourceBody); s.IntrinsicLineBreaks = true; break;
                case FinallyExpression n: s.Nodes("body", n.SourceBody); s.IntrinsicLineBreaks = true; break;
                case MethodBodyStatements n when n.GetType() == typeof(MethodBodyStatements): s.Nodes("statement", n.Statements); break;
                case ExpressionStatement n when n.GetType() == typeof(ExpressionStatement): s.Child("expression", n.Expression); s.IntrinsicLineBreaks = true; break;
                case YieldReturnStatement n when n.GetType() == typeof(YieldReturnStatement): s.Child("value", n.Value); s.IntrinsicLineBreaks = true; break;
                case YieldBreakStatement n when n.GetType() == typeof(YieldBreakStatement): s.IntrinsicLineBreaks = true; break;
                case ForEachStatement n:
                    s.Type(n.ItemType); s.Declaration(n.Item); s.Value(n.IsAsync); s.Child("collection", n.Enumerable);
                    s.Nodes("body", n.Body as IReadOnlyList<MethodBodyStatement>); s.IntrinsicLineBreaks = true; break;
                case ForStatement n:
                    s.Child("initializer", n.IndexExpression); s.Child("condition", n.Condition); s.Child("increment", n.IncrementExpression);
                    s.Nodes("body", n.Body); s.IntrinsicLineBreaks = true; break;
                case WhileStatement n when n.GetType() == typeof(WhileStatement):
                    s.Child("condition", n.Condition); s.Nodes("body", n.SourceBody); s.IntrinsicLineBreaks = true; break;
                case DoWhileStatement n when n.GetType() == typeof(DoWhileStatement):
                    s.Nodes("body", n.SourceBody); s.Child("condition", n.Condition); s.IntrinsicLineBreaks = true; break;
                case IfStatement n:
                    s.Value(n.Inline); s.Value(n.AddBraces); s.Child("condition", n.Condition); s.Nodes("body", n.SourceBody); s.IntrinsicLineBreaks = !n.Inline; break;
                case IfElseStatement n:
                    s.Child("if", n.If); s.Nodes("else-if", n.ElseIfs); s.Child("else", n.Else); s.IntrinsicLineBreaks = n.Else is not null; break;
                case IfElsePreprocessorStatement n:
                    s.Value(n.Condition); s.Child("if-variant", n.If); s.Child("else-variant", n.Else);
                    // A simple conditional symbol cannot inject another line.
                    // Directives and both child variants remain separate facts.
                    s.HasDirectives = true; s.IntrinsicLineBreaks = true;
                    s.IntrinsicTokens &= pocSupplement && (n.Condition is "true" or "false" || s.IsPocSafeRawIdentifier(n.Condition)); break;
                case UsingScopeStatement n:
                    s.Type(n.Type); s.Declaration(n.Variable); s.Child("resource", n.Value); s.Nodes("body", n.Body); s.IntrinsicLineBreaks = true; break;
                case TryCatchFinallyStatement n:
                    s.Child("try", n.Try); s.Nodes("catch", n.Catches); s.Child("finally", n.Finally); break;
                case SwitchStatement n:
                    s.Child("match", n.MatchExpression); s.Nodes("case", n.Cases); s.IntrinsicLineBreaks = true; break;
                case SwitchCaseStatement n: s.Nodes("match", n.Matches); s.Child("body", n.Statement); s.IntrinsicLineBreaks = true; break;
                case DeclareLocalFunctionStatement n:
                    s.Declaration(n.Name); s.Type(n.ReturnType);
                    // The current local-function writer emits parameter types/names, NOT defaults/attributes.
                    if (s.TryCopy(n.Parameters, out var localParameters))
                    {
                        foreach (var p in localParameters)
                        {
                            if (defensivePoc && (p is null || p.Name is null || p.Type is null)) { s.RejectPoc("Missing local-function parameter metadata."); continue; }
                            s.Ref(p); s.Value(p!.DeclarationRevision); s.Value(p.Name); s.Name(p.Name!); s.Type(p.Type);
                            // Local-function parameters use Name directly,
                            // unlike method parameters' cached declarations.
                            if (pocSupplement) { s.IntrinsicTokens &= s.IsPocSafeRawIdentifier(p.Name); }
                        }
                    }
                    else { s.Fail("Unowned local-function parameters."); }
                    s.Child("expression-body", n.BodyExpression); if (n.BodyExpression is null) { s.Child("block-body", n.BodyStatement); }
                    s.IntrinsicLineBreaks = true; break;
                case AttributeStatement n:
                    s.Ref(n.Data);
                    if (n.Data is not null && (n.CachedType is null || n.CachedArguments is null || n.CachedPositionalArguments is null))
                    { s.Fail("Unmaterialized attribute child projection."); break; }
                    s.Type(n.CachedType); s.Nodes("argument", n.CachedArguments);
                    if (s.TryCopy(n.CachedPositionalArguments, out var named))
                    {
                        for (int i = 0; i < named.Length; i++)
                        {
                            s.Value(named[i].Key);
                            // The attribute writer appends the key raw; the
                            // value and containing attribute grammar are separate.
                            if (pocSupplement) { s.IntrinsicTokens &= s.IsPocSafeRawIdentifier(named[i].Key); }
                            s.RequiredChild($"named[{i}]", named[i].Value);
                        }
                    }
                    else { s.Fail("Unowned named attribute arguments."); }
                    break;
                case MethodProvider n:
                    if (defensivePoc && n.Signature is null) { s.RejectPoc("Missing method signature."); break; }
                    s.Value(n.DeclarationRevision); s.Ref(n.EnclosingType); s.Docs(n.XmlDocs, disableXmlDocs);
                    s.Child("signature", n.Signature, false);
                    // Explicit conversions spell their target with Name, not ReturnType.
                    var methodReturnType = n.Signature.Modifiers.HasFlag(MethodSignatureModifiers.Async) ||
                        n.Signature.Modifiers.HasFlag(MethodSignatureModifiers.Explicit) ? null : n.Signature.ReturnType;
                    s.Child("block-body", n.BodyStatements, methodReturnType: methodReturnType, parameterOwner: n);
                    if (n.BodyStatements is null)
                    {
                        s.Child("expression-body", n.BodyExpression, targetType: methodReturnType, parameterOwner: n);
                    }
                    s.IntrinsicLineBreaks = true; break;
                case ConstructorProvider n:
                    s.Value(n.DeclarationRevision); s.Ref(n.EnclosingType); s.Docs(n.XmlDocs, disableXmlDocs);
                    if (n.BodyStatements is not null || n.BodyExpression is not null) { s.Child("signature", n.Signature, false); }
                    s.Child("block-body", n.BodyStatements); if (n.BodyStatements is null) { s.Child("expression-body", n.BodyExpression); }
                    s.IntrinsicLineBreaks = true; break;
                case MethodSignatureBase n when n is MethodSignature or ConstructorSignature:
                    s.SignatureMetadata(n); s.IntrinsicLayoutKnown = false;
                    // The standard declaration writer uses ReturnType instead
                    // of Name only for the implicit-conversion branch.
                    if (pocSupplement && !(n is MethodSignature implicitMethod && implicitMethod.Modifiers.HasFlag(MethodSignatureModifiers.Implicit)))
                    { s.IntrinsicTokens &= s.IsPocSafeRawIdentifier(n.Name); }
                    s.Nodes("attribute", n.Attributes); s.Nodes("parameter", n.Parameters, false);
                    if (n is MethodSignature method && method.GenericParameterConstraints is { } constraints) { s.Nodes("constraint", constraints); }
                    if (n is ConstructorSignature constructor) { s.Type(constructor.Type); s.Child("initializer", constructor.Initializer, false); }
                    break;
                case ConstructorInitializer n:
                    s.Value(n.IsBase); s.Ref(n.TargetConstructorIntent); s.Ref(n.TargetConstructorProviderIntent);
                    s.Ref(n.OriginalSource); s.Ref(n.OriginalTargetSymbol); s.Ref(n.OriginalCompilation);
                    if (n.TargetConstructorIntent is { } target) { s.SignatureMetadata(target); s.Type(target.Type); }
                    if (n.TargetConstructorProviderIntent is { } targetProvider)
                    {
                        s.Value(targetProvider.DeclarationRevision);
                        if (defensivePoc && targetProvider.Signature is null) { s.RejectPoc("Missing intended initializer signature."); }
                        else { s.SignatureMetadata(targetProvider.Signature); s.Type(targetProvider.Signature.Type); }
                    }
                    s.Nodes("argument", n.Arguments); break;
                case ParameterProvider n:
                    VariableExpression? cachedVariable = null;
                    if (defensivePoc && !s.TryPreparePocParameter(n, out cachedVariable)) { break; }
                    s.Value(n.DeclarationRevision); var emitted = n.EmittedDeclarationState; s.Value(emitted);
                    s.Value(n.Name); s.Name(n.Name); s.Type(n.Type); s.Ref(n.OriginalSymbol); s.Ref(n.OriginalCompilation);
                    if (pocSupplement) { s.ObservePocParameterWriterName(n, cachedVariable, emitted.Name); }
                    s.Value(n.IsRef); s.Value(n.IsOut); s.Value(n.IsIn); s.Value(n.IsParams);
                    s.Nodes("attribute", n.Attributes); s.Child("default", n.DefaultValue); break;
                case FieldProvider n:
                    s.Ref(n.EnclosingType); s.Value(n.Name); s.Name(n.Name); s.Value(n.Modifiers); s.Type(n.Type);
                    // Enum fields use the raw Name branch. Apply the same
                    // conservative POC-only check to every field; escaped
                    // ordinary fields remain deferred rather than presumed safe.
                    if (pocSupplement)
                    {
                        s.IntrinsicTokens &= s.IsPocSafeRawIdentifier(n.Name);
                        // Class fields write the declaration, which can differ
                        // from Name; read only an already-materialized cache.
                        if (n.CachedDeclaration is { } declaration)
                        {
                            s.Declaration(declaration);
                            s.IntrinsicTokens &= s.IsPocSafeRawIdentifier(declaration.RequestedName) &&
                                string.Equals(declaration.RequestedName, n.Name, StringComparison.Ordinal);
                        }
                    }
                    s.Docs(n.XmlDocs, disableXmlDocs); s.Nodes("attribute", n.Attributes); s.Child("initializer", n.InitializationValue, targetType: n.Type); s.IntrinsicLineBreaks = true; break;
                case PropertyProvider n:
                    s.Ref(n.EnclosingType); s.Value(n.Name); s.Name(n.Name); s.Value(n.Modifiers); s.Value(n.IsRef); s.Type(n.Type); s.Type(n.ExplicitInterface);
                    s.Docs(n.XmlDocs, disableXmlDocs); s.Nodes("attribute", n.Attributes); s.Child("accessors", n.Body, false);
                    if (n is IndexPropertyProvider indexer)
                    {
                        // The indexer writer appends Name raw. Only the native
                        // canonical keyword is admitted by this POC profile.
                        if (pocSupplement) { s.IntrinsicTokens &= n.Name == "this"; }
                        var indexerParameter = indexer.IndexerParameter;
                        VariableExpression? indexerVariable = null;
                        if (defensivePoc && (indexerParameter is null || !s.TryPreparePocParameter(indexerParameter, out indexerVariable)))
                        { s.RejectPoc("Missing or unsupported indexer parameter metadata."); }
                        else
                        {
                            s.Ref(indexerParameter); s.Value(indexerParameter.DeclarationRevision); var indexerEmitted = indexerParameter.EmittedDeclarationState;
                            s.Value(indexerEmitted); s.Type(indexerParameter.Type);
                            if (pocSupplement) { s.ObservePocParameterWriterName(indexerParameter, indexerVariable, indexerEmitted.Name); }
                        }
                    }
                    s.IntrinsicLineBreaks = true; break;
                case ExpressionPropertyBody n when n.GetType() == typeof(ExpressionPropertyBody): s.Child("getter", n.Getter); s.Child("setter", n.Setter); break;
                case MethodPropertyBody n when n.GetType() == typeof(MethodPropertyBody):
                    s.Value(n.SetterModifiers); s.Child("getter", n.Getter); s.Child("setter", n.Setter); s.IntrinsicLineBreaks = true; break;
                case AutoPropertyBody n when n.GetType() == typeof(AutoPropertyBody):
                    s.Value(n.HasSetter); s.Value(n.SetterModifiers); s.Child("initializer", n.InitializationExpression); break;
                default:
                    if (ReferenceEquals(node, ValueExpression.Empty) || ReferenceEquals(node, MethodBodyStatement.Empty)) { break; }
                    if (ReferenceEquals(node, MethodBodyStatement.EmptyLine)) { s.IntrinsicLineBreaks = true; break; }
                    s.Fail("Unknown/custom/raw emission boundary."); break;
            }
            // A compact keyword may have one child. Reserve its immutable
            // projection only when that child was actually captured.
            if (initialChargePaid && node is KeywordExpression && s._children!.Count != 0 && !budget!.TrySpend())
            { s.RejectPoc("POC work budget exhausted."); return s; }
            s.Children = s._children is null ? [] : s._children.ToImmutableArray();
            if (s._types is null) { s._typeSnapshots = []; }
            else
            {
                var types = ImmutableArray.CreateBuilder<DeclaredTypeSnapshot>();
                foreach (var type in s._types)
                {
                    if (!(budget is not null ? DeclaredTypeSnapshot.TryCaptureForPoc(type, budget, out var snapshot) : DeclaredTypeSnapshot.TryCapture(type, out snapshot)))
                    {
                        if (defensivePoc) { s.RejectPoc("Unavailable type child snapshot."); }
                        else { s.Fail("Unavailable type child snapshot."); }
                        break;
                    }
                    types.Add(snapshot!);
                    if (!s.LexicalType(type)) { s.IntrinsicTokens = false; s.IntrinsicLayoutKnown = false; }
                }
                s._typeSnapshots = types.ToImmutable();
            }
            s._declarationSnapshots = s._declarations is null ? [] : s._declarations.Select(d => (d, d.SourceRevision, d.RequestedName)).ToImmutableArray();
            if (budget?.Exhausted == true) { s.RejectPoc("POC work budget exhausted."); }
            return s;
        }

        internal bool Matches(StructuredSourceShape current) => MatchesCore(current, null);
        internal bool MatchesForPoc(StructuredSourceShape current, PocWorkBudget budget) => MatchesCore(current, budget);

        private bool MatchesCore(StructuredSourceShape current, PocWorkBudget? budget)
        {
            if (budget is not null && (budget.Exhausted || current._pocVeto || _typeSnapshots.Length != TypeCount ||
                current._typeSnapshots.Length != current.TypeCount || MetadataCount != current.MetadataCount ||
                !budget.TrySpend(ValueCount) || !budget.TrySpend(ReferenceCount) || !budget.TrySpend(Children.Length) ||
                !budget.TrySpend(TypeCount) || !budget.TrySpend(DeclarationCount))) { return false; }
            if (Opaque != current.Opaque || UnavailableReason != current.UnavailableReason || ValueCount != current.ValueCount ||
                ReferenceCount != current.ReferenceCount || TypeCount != current.TypeCount || Children.Length != current.Children.Length ||
                DeclarationCount != current.DeclarationCount) { return false; }
            for (int i = 0; i < ValueCount; i++)
            {
                if (budget is not null && _values![i] is string left && current._values![i] is string right &&
                    !ReferenceEquals(left, right) && !budget.TrySpend(Math.Max(left.Length, right.Length))) { return false; }
                if (!Equals(_values![i], current._values![i])) { return false; }
            }
            for (int i = 0; i < ReferenceCount; i++) { if (!ReferenceEquals(_references![i], current._references![i])) { return false; } }
            for (int i = 0; i < Children.Length; i++)
            {
                if (budget is not null && !budget.TrySpendStringComparison(Children[i].Role, current.Children[i].Role)) { return false; }
                if (Children[i].Role != current.Children[i].Role || Children[i].EmissionBoundary != current.Children[i].EmissionBoundary ||
                    !ReferenceEquals(Children[i].Producer, current.Children[i].Producer) ||
                    !ReferenceEquals(Children[i].TargetType, current.Children[i].TargetType) ||
                    !ReferenceEquals(Children[i].MethodReturnType, current.Children[i].MethodReturnType) ||
                    !ReferenceEquals(Children[i].ParameterOwner, current.Children[i].ParameterOwner)) { return false; }
            }
            for (int i = 0; i < TypeCount; i++) { if (!ReferenceEquals(_types![i], current._types![i])) { return false; } }
            if (budget is null)
            {
                return _typeSnapshots.All(t => t.IsCurrent()) && (_metadataAttributes is null || _metadataAttributes.All(a => a.IsCurrent())) && _declarationSnapshots.All(d =>
                    d.Declaration.SourceRevision == d.Revision && d.Declaration.RequestedName == d.Name);
            }
            foreach (var type in _typeSnapshots) { if (!type.IsCurrentForPoc(budget)) { return false; } }
            if (_metadataAttributes is not null)
            {
                foreach (var attribute in _metadataAttributes) { if (!attribute.IsCurrentForPoc(budget)) { return false; } }
            }
            foreach (var declaration in _declarationSnapshots)
            {
                if (declaration.Declaration.SourceRevision != declaration.Revision ||
                    !budget.TrySpendStringComparison(declaration.Declaration.RequestedName, declaration.Name) ||
                    declaration.Declaration.RequestedName != declaration.Name) { return false; }
            }
            return true;
        }

        private bool TryCopy<T>(IReadOnlyList<T>? list, out ImmutableArray<T> items)
        {
            // A defensive recheck of the shared graph must keep its original
            // opacity. Only the distinct supplemental profile expands the new
            // producer carrier and inspects its actual current elements.
            if (!_pocSupplement && list is ProducerOwnedReadOnlyList<T>) { items = []; return false; }
            return _budget is null ? ContractCollections.TryCopy(list, out items) : _allowConstructionCollections
                ? ContractCollections.TryCopyPocConstruction(list, _budget, out items)
                : ContractCollections.TryCopyForPoc(list, _budget, out items);
        }

        private void Fail(string reason) { Opaque = true; IntrinsicTokens = false; UnavailableReason ??= reason; }
        private void RejectPoc(string reason) { _pocVeto = true; Fail(reason); }
        private void Value(object? value) => _values!.Add(value); // only primitive/string/enum values at the audited sites above
        private void Ref(object? value) => _references!.Add(value);
        private void Name(string name)
        {
            if (!SimpleIdentifier(name)) { IntrinsicTokens = false; IntrinsicLayoutKnown = false; }
        }
        private void Type(CSharpType? type)
        {
            Ref(type);
            if (type is not null)
            {
                _types!.Add(type);
                if (!TypeNameIsSafe(type) || !NamespaceIdentifiers(type.Namespace))
                { IntrinsicTokens = false; IntrinsicLayoutKnown = false; }
            }
        }
        private bool LexicalType(CSharpType type)
        {
            if (!TypeNameIsSafe(type) || !NamespaceIdentifiers(type.Namespace) ||
                !TryCopy(type.Arguments, out var arguments)) { return false; }
            return arguments.All(LexicalType) && (type.DeclaringType is null || LexicalType(type.DeclaringType));
        }
        private bool TypeNameIsSafe(CSharpType type)
        {
            if (!_pocSupplement) { return PocTypeIdentifier(type.Name); }
            if (_budget is null || _budget.Exhausted || type.GetType() != typeof(CSharpType)) { return false; }
            if (!type.IsFrameworkType) { return SimpleIdentifier(type.Name); }
            var runtime = type.FrameworkType;
            // A custom Type can change the name that the native formatter emits
            // even when CSharpType.Name is a simple cached identifier.
            if (runtime.GetType() != typeof(object).GetType()) { return false; }
            if (runtime.IsArray) { return IsSafeRuntimeArrayToken(type, runtime); }
            if (runtime.IsGenericParameter)
            {
                // The native formatter writes the runtime name, not merely the
                // cached CSharpType name. Charge both before comparing them.
                if (!SimpleIdentifier(type.Name)) { return false; }
                var runtimeName = runtime.Name;
                return runtimeName is not null && _budget.TrySpend(runtimeName.Length) &&
                    string.Equals(runtimeName, type.Name, StringComparison.Ordinal);
            }
            return SimpleIdentifier(type.Name);
        }

        // The native formatter writes these runtime names directly. This checks
        // only a bounded ASCII token; element binding and enclosing grammar are
        // separate. RuntimeType dispatch has already been checked above.
        private bool IsSafeRuntimeArrayToken(CSharpType type, Type runtime)
        {
            if (_budget is null || _budget.Exhausted) { return false; }
            var name = type.Name;
            if (name is null || !_budget.TrySpend(name.Length)) { return false; }
            var i = 0;
            if (name.Length == 0 || !(name[0] == '_' || name[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) { return false; }
            while (i < name.Length && (name[i] == '_' || name[i] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')) { i++; }
            if (i == name.Length) { return false; }
            var elementName = name[..i];
            if (SyntaxFacts.GetKeywordKind(elementName) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(elementName) != SyntaxKind.None) { return false; }
            while (i < name.Length)
            {
                if (name[i++] != '[') { return false; }
                while (i < name.Length && name[i] == ',') { i++; }
                if (i == name.Length || name[i++] != ']') { return false; }
            }
            var cachedNamespace = type.Namespace;
            if (cachedNamespace is null || !_budget.TrySpend(cachedNamespace.Length)) { return false; }
            var runtimeName = runtime.Name;
            if (runtimeName is null || !_budget.TrySpend(runtimeName.Length)) { return false; }
            var runtimeNamespace = runtime.Namespace ?? string.Empty;
            if (!_budget.TrySpend(runtimeNamespace.Length)) { return false; }
            return string.Equals(runtimeName, name, StringComparison.Ordinal) &&
                string.Equals(runtimeNamespace, cachedNamespace, StringComparison.Ordinal);
        }

        private bool PocTypeIdentifier(string? value)
        {
            if (_typeTextMemo is null || _budget is null || value is null || value.Length <= 2) { return SimpleIdentifier(value); }
            if (!_typeTextMemo.TryFind(value, false, _budget, out var found)) { RejectPoc("POC work budget exhausted."); return false; }
            if (found) { return true; }
            if (!SimpleIdentifier(value)) { return false; }
            if (!_typeTextMemo.TryRemember(value, false, _budget)) { RejectPoc("POC work budget exhausted."); return false; }
            return true;
        }

        private bool NamespaceIdentifiers(string? value)
        {
            if (_defensivePoc && value is null) { RejectPoc("Missing type namespace."); return false; }
            if (_budget is null) { return !value!.Split('.').Any(part => part.Length != 0 && !SimpleIdentifier(part)); }
            if (_typeTextMemo is not null && value is not null && value.Length > 2)
            {
                if (!_typeTextMemo.TryFind(value, true, _budget, out var found)) { RejectPoc("POC work budget exhausted."); return false; }
                if (found) { return true; }
                if (!NamespaceIdentifiersCore(value)) { return false; }
                if (!_typeTextMemo.TryRemember(value, true, _budget)) { RejectPoc("POC work budget exhausted."); return false; }
                return true;
            }
            return NamespaceIdentifiersCore(value!);
        }

        private bool NamespaceIdentifiersCore(string value)
        {
            if (!_budget!.TrySpend(value.Length)) { RejectPoc("POC work budget exhausted."); return false; }
            var atStart = true;
            foreach (var character in value)
            {
                if (character == '.') { atStart = true; continue; }
                if (!(character == '_' || character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' || !atStart && character is >= '0' and <= '9')) { return false; }
                atStart = false;
            }
            return true;
        }
        private bool TryPreparePocParameter(ParameterProvider parameter, out VariableExpression? cached)
        {
            cached = null;
            if (parameter.Name is null || parameter.Type is null || !SimpleIdentifier(parameter.Name))
            { RejectPoc("Missing or unsupported parameter metadata."); return false; }
            // Both the uncached emitted state and the cached expected-name
            // check can normalize through a virtual CultureInfo.TextInfo.
            // Decline before either accessor; native writing keeps its timing.
            if (parameter.InputParameter?.IsExactName != true &&
                System.Globalization.CultureInfo.CurrentCulture.GetType() != typeof(System.Globalization.CultureInfo))
            { RejectPoc("Unknown parameter name normalization culture."); return false; }
            cached = parameter.CachedVariableExpression;
            if (cached is not null)
            {
                var declaration = cached.Declaration;
                if (declaration is null || declaration.RequestedName is not { } name || !_budget!.TrySpend(name.Length))
                { RejectPoc("Missing or oversized parameter declaration."); return false; }
            }
            return true;
        }

        private void ObservePocParameterWriterName(ParameterProvider parameter, VariableExpression? cached, string? emitted)
        {
            Ref(cached);
            if (!ReferenceEquals(cached, parameter.CachedVariableExpression)) { RejectPoc("Parameter variable changed during observation."); return; }
            // Validate the input before calling the pure name normalizer, and
            // never materialize the variable or its declaration here.
            var expected = cached is null ? emitted : parameter.ExpectedEmittedVariableName;
            if (expected is null || !_budget!.TrySpend(expected.Length)) { RejectPoc("Missing or oversized parameter name."); return; }
            Value(expected);
            IntrinsicTokens &= IsPocSafeRawIdentifier(emitted) && string.Equals(emitted, expected, StringComparison.Ordinal);
        }

        // These checks certify only the raw operator string, not operand
        // adjacency, precedence, full grammar or semantic validity.
        private bool IsPocSafeBinaryOperator(string? op) => op is not null && _budget is not null && _budget.TrySpend(op.Length) &&
            op is "+" or "-" or "*" or "/" or "%" or "==" or "!=" or "<" or ">" or "<=" or ">=" or
                "&&" or "||" or "&" or "|" or "^" or "<<" or ">>" or ">>>" or "??" or "??=" or
                "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=" or ">>>=" or
                "is" or "is not" or "as" or "and" or "or" or " and " or " or ";

        private bool IsPocSafeUnaryOperator(string? op, bool operandOnTheLeft) => op is not null && _budget is not null &&
            _budget.TrySpend(op.Length) && (op is "!" or "~" or "+" or "-" or "++" or "--" or "^" ||
                op == "u8" && operandOnTheLeft || op == ".. " && !operandOnTheLeft);

        private bool HasSimplePocInterpolatedParts(FormattableStringExpression expression)
        {
            var format = expression.Format;
            if (format is null || _budget is null || !_budget.TrySpend(format.Length) ||
                !TryCopy(expression.Arguments, out var arguments)) { return false; }
            var holes = 0;
            foreach (var (span, isLiteral) in StringExtensions.GetFormattableStringFormatParts(format))
            {
                if (isLiteral)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        var character = span[i];
                        if (character is < ' ' or > '~' || character is '{' or '}') { return false; }
                        if (character == '\\')
                        {
                            // Accept only complete, in-part quote/backslash
                            // escapes; never let an escape cross a hole.
                            if (++i >= span.Length || span[i] is not ('"' or '\\')) { return false; }
                        }
                        else if (character == '"') { return false; }
                    }
                }
                else
                {
                    if (span.IsEmpty) { return false; }
                    foreach (var character in span) { if (character is < '0' or > '9') { return false; } }
                    holes++;
                }
            }
            return holes == arguments.Length && arguments.All(argument => argument is not null);
        }

        // Only the exact raw name branch is certified here; child tokens,
        // layout, scope, grammar and final spelling remain separate facts.
        private bool IsPocSafeRawIdentifier(string? name) => name is not null && SimpleIdentifier(name) &&
            SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None;

        private bool SimpleIdentifier(string? name)
        {
            if (_defensivePoc && name is null) { RejectPoc("Missing identifier."); return false; }
            if (_budget is not null && !_budget.TrySpend(name!.Length)) { RejectPoc("POC work budget exhausted."); return false; }
            return name!.Length > 0 && (name[0] == '_' || name[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') &&
                name.All(c => c == '_' || c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
        }
        private void Declaration(CodeWriterDeclaration? declaration)
        {
            Ref(declaration);
            if (declaration is not null)
            {
                _declarations!.Add(declaration);
                if (!SimpleIdentifier(declaration.RequestedName)) { IntrinsicTokens = false; IntrinsicLayoutKnown = false; }
            }
        }
        private void RequiredChild(string role, object? child)
        {
            HasMissingRequiredOperand |= child is null;
            Child(role, child);
        }

        private void Child(string role, object? child, bool boundary = true, CSharpType? targetType = null, CSharpType? methodReturnType = null, MethodProvider? parameterOwner = null)
        {
            if (_budget is not null && !_budget.TrySpend()) { Fail("POC work budget exhausted."); return; }
            Ref(child);
            if (child is not null)
            {
                if (targetType is not null) { Type(targetType); }
                if (methodReturnType is not null) { Type(methodReturnType); }
                _children!.Add(new(role, child, boundary, targetType, methodReturnType, parameterOwner));
            }
        }
        private void Nodes<T>(string role, IReadOnlyList<T>? nodes, bool boundary = true) where T : class
        {
            Ref(nodes);
            if (!TryCopy(nodes, out var children)) { Fail($"Unowned {role} collection."); return; }
            for (int i = 0; i < children.Length; i++)
            { if (children[i] is null) { Fail("Missing child object."); } else { Child($"{role}[{i}]", children[i], boundary); } }
        }
        private void Types(IReadOnlyList<CSharpType>? types)
        {
            Ref(types); if (types is null) { return; }
            if (!TryCopy(types, out var children)) { Fail("Unowned type-argument collection."); return; }
            foreach (var type in children)
            {
                if (_defensivePoc && type is null) { RejectPoc("Missing type argument."); continue; }
                Type(type);
            }
        }
        private void Docs(XmlDocProvider? docs, bool disabled) { Ref(docs); if (!disabled) { Child("documentation", docs); } }
        private void SignatureMetadata(MethodSignatureBase signature)
        {
            Ref(signature); Value(signature.DeclarationRevision); Value(signature.Name); Value(signature.Modifiers);
            if (!SimpleIdentifier(signature.Name) || signature.NonDocumentComment is not null) { IntrinsicTokens = false; IntrinsicLayoutKnown = false; }
            Value(signature.NonDocumentComment); Type(signature.ReturnType); Ref(signature.OriginalSymbol); Ref(signature.OriginalCompilation);
            if (signature is MethodSignature method) { Types(method.GenericArguments); Type(method.ExplicitInterface); }
            // Callee declaration metadata is a dependency, NOT an emitted child of the call. Keep
            // known retained-list/formal/attribute changes observable even when selection is unavailable.
            Ref(signature.Parameters);
            if (TryCopy(signature.Parameters, out var parameters))
            {
                foreach (var parameter in parameters)
                {
                    Ref(parameter);
                    if (parameter is null)
                    {
                        if (_defensivePoc) { RejectPoc("Missing signature parameter."); }
                        continue;
                    }
                    if (_defensivePoc && (parameter.Name is null || parameter.Type is null)) { RejectPoc("Missing signature parameter name or type."); continue; }
                    Value(parameter.DeclarationRevision); Value(parameter.Name); Type(parameter.Type); Ref(parameter.DefaultValue);
                    if (parameter.DefaultValue is ValueExpression expression) { Value(expression.SourceRevision); }
                    if (parameter.DefaultValue is KeywordExpression keyword) { Value(keyword.DeclarationRevision); }
                    if (parameter.DefaultValue is ScopedApi wrapper) { Value(wrapper.DeclarationRevision); }
                    MetadataAttributes(parameter.Attributes);
                }
            }
            else if (_defensivePoc) { RejectPoc("Unowned signature parameter collection."); }
            MetadataAttributes(signature.Attributes);
        }
        private void MetadataAttributes(IReadOnlyList<AttributeStatement> attributes)
        {
            Ref(attributes);
            if (!TryCopy(attributes, out var items))
            {
                if (_defensivePoc) { RejectPoc("Unowned signature attributes."); }
                return;
            }
            foreach (var attribute in items)
            {
                Ref(attribute);
                if (attribute is null)
                {
                    if (_defensivePoc) { RejectPoc("Missing signature attribute."); }
                    continue;
                }
                if (_defensivePoc)
                {
                    if (AttributeSnapshot.TryCaptureForPoc(attribute, _budget!, out var pocSnapshot)) { _metadataAttributes!.Add(pocSnapshot!); }
                    else { RejectPoc("Unsupported signature attribute metadata."); }
                }
                else if (AttributeSnapshot.TryCapture(attribute, out var snapshot)) { _metadataAttributes!.Add(snapshot!); }
            }
        }
        private void Dictionary(IReadOnlyDictionary<ValueExpression, ValueExpression> dictionary)
        {
            Ref(dictionary);
            if (dictionary is not Dictionary<ValueExpression, ValueExpression> values || dictionary.GetType() != typeof(Dictionary<ValueExpression, ValueExpression>))
            { Fail("Unowned initializer dictionary."); return; }
            // Enumeration visits retained slots, not just live entries. The
            // public capacity query is non-mutating and invokes no comparer.
            if (_budget is not null && !_budget.TrySpend(values.EnsureCapacity(0))) { Fail("POC work budget exhausted."); return; }
            int i = 0;
            foreach (var pair in values) { Child($"key[{i}]", pair.Key); RequiredChild($"value[{i++}]", pair.Value); }
        }
        // POC-only metadata path. Keep indices without copying every prefix;
        // materialize the identical full role only for an emitted leaf edge.
        private sealed class FlattenedRolePath
        {
            private int Index { get; }
            private FlattenedRolePath? Parent { get; }
            internal int Length { get; }

            internal FlattenedRolePath(int index, FlattenedRolePath? parent)
            {
                Index = index;
                Parent = parent;
                int digits = 1;
                for (int value = index; value >= 10; value /= 10) { digits++; }
                Length = (parent?.Length ?? 0) + (parent is null ? "body[".Length : "/statement[".Length) + digits + 1;
            }

            internal string Materialize() => string.Create(Length, this, static (text, path) =>
            {
                for (FlattenedRolePath? current = path; current is not null; current = current.Parent)
                {
                    int end = current.Length;
                    text[--end] = ']';
                    int value = current.Index;
                    do { text[--end] = (char)('0' + value % 10); value /= 10; } while (value != 0);
                    var prefix = current.Parent is null ? "body[" : "/statement[";
                    prefix.AsSpan().CopyTo(text[(end - prefix.Length)..end]);
                }
            });
        }

        private void FlattenedBody(IReadOnlyList<MethodBodyStatement> body)
        {
            // Try/Catch's existing writer enumerates MethodBodyStatement and flattens only statement
            // groups. Observe those exact edges without invoking that iterator or changing its behavior.
            Ref(body);
            if (!TryCopy(body, out var statements)) { Fail("Unowned flattened body."); return; }
            void Flatten(MethodBodyStatement statement, string? legacyPath, FlattenedRolePath? path, HashSet<MethodBodyStatement> ancestors, int depth)
            {
                if (_defensivePoc && (statement is null || depth >= 256 || !_budget!.TrySpend()))
                { RejectPoc("Missing or excessively deep flattened statement group."); return; }
                // Reserve the insertion and its eventual removal together:
                // descendants may exhaust the budget before unwinding.
                if (_defensivePoc && (!_budget!.TrySpendReferenceLookup(ancestors.Count) ||
                    !_budget.TrySpendReferenceLookup(ancestors.Count + 1)))
                { RejectPoc("Flattened statement identity budget exhausted."); return; }
                if (!ancestors.Add(statement)) { Fail("Cyclic flattened statement group."); return; }
                if (statement is MethodBodyStatements group)
                {
                    Ref(group); Value(group.SourceRevision); Ref(group.Statements);
                    if (group.GetType() != typeof(MethodBodyStatements) || !TryCopy(group.Statements, out var children))
                    { Fail("Opaque flattened statement group."); }
                    else
                    {
                        for (int j = 0; j < children.Length; j++)
                        {
                            if (_defensivePoc && Opaque) { break; }
                            if (_defensivePoc)
                            {
                                if (!_budget!.TrySpend()) { RejectPoc("Flattened statement path budget exhausted."); break; }
                                Flatten(children[j], null, new FlattenedRolePath(j, path), ancestors, depth + 1);
                            }
                            else { Flatten(children[j], $"{legacyPath}/statement[{j}]", null, ancestors, depth + 1); }
                        }
                    }
                }
                else if (_defensivePoc)
                {
                    if (!_budget!.TrySpend(path!.Length)) { RejectPoc("Flattened statement path budget exhausted."); }
                    else { Child(path.Materialize(), statement); }
                }
                else { Child(legacyPath!, statement); }
                ancestors.Remove(statement);
            }
            for (int i = 0; i < statements.Length; i++)
            {
                if (_defensivePoc && Opaque) { break; }
                if (_defensivePoc)
                {
                    if (!_budget!.TrySpend()) { RejectPoc("Flattened statement path budget exhausted."); break; }
                    Flatten(statements[i], null, new FlattenedRolePath(i, null), new(ReferenceEqualityComparer.Instance), 0);
                }
                else { Flatten(statements[i], $"body[{i}]", null, new(ReferenceEqualityComparer.Instance), 0); }
            }
        }
    }
}
