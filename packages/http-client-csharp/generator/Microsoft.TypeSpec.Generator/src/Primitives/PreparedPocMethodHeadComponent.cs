// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // ISOLATED PARTIAL COMPONENT, NOT a method envelope. The return/interface/
    // generic/parameter/default/attribute/constraint/suppression inputs are OPEN.
    // This binds only the raw method name, ordinary modifier branch, absent
    // non-doc comment and (when enabled) finite documentation to an owned body.
    internal sealed class PreparedPocMethodHeadComponent
    {
        private readonly PreparedPocOwnedRootEffect _owned;
        private readonly MethodProvider _method;
        private readonly MethodSignature _signature;
        private readonly long _revision;
        private readonly string _name;
        private readonly MethodSignatureModifiers _modifiers;
        private readonly XmlDocProvider _docs;
        private readonly PreparedGeneratedDocumentationEnvelope? _documentation;
        private readonly PocWorkBudget _account;
        private readonly bool _disableDocs;
        private bool _started, _finished, _sawDocs;
        private bool _failed;
        internal bool HasCompleted => !_failed && _started && _finished && (_disableDocs || _sawDocs) &&
            _owned.IsSelectedMethodHead(this) && _owned.HasCompleted;
        internal int DeclarationStarts { get; private set; }
        internal int DeclarationCompletions { get; private set; }
        internal int DocumentationEntries { get; private set; }

        private PreparedPocMethodHeadComponent(PreparedPocOwnedRootEffect owned, MethodProvider method,
            PreparedGeneratedDocumentationEnvelope? documentation, PocWorkBudget account, bool disableDocs)
        {
            _owned = owned; _method = method; _signature = method.Signature;
            _revision = _signature.DeclarationRevision; _name = _signature.Name; _modifiers = _signature.Modifiers;
            _docs = method.XmlDocs; _documentation = documentation; _account = account; _disableDocs = disableDocs;
        }

        internal static PreparedPocMethodHeadComponent? TryCapture(PreparedPocOwnedRootEffect owned,
            MethodProvider method, bool disableDocs, PocWorkBudget account)
        {
            // No virtual provider/classifier/formatter callback or materialization.
            if (!account.TrySpend(8) || disableDocs || disableDocs != owned.File.DisableXmlDocs ||
                !ReferenceEquals(owned.Root.Producer, method) ||
                method.Signature.GetType() != typeof(MethodSignature)) { return null; }
            var signature = method.Signature;
            var name = signature.Name;
            if (name is null || !account.TrySpend(name.Length) || !Identifier(name) ||
                signature.NonDocumentComment is not null ||
                (signature.Modifiers & (MethodSignatureModifiers.Implicit | MethodSignatureModifiers.Explicit |
                    MethodSignatureModifiers.Operator | MethodSignatureModifiers.Extension)) != 0) { return null; }
            var docs = disableDocs ? null : PreparedGeneratedDocumentationEnvelope.TryCaptureForFileWithBudget(method.XmlDocs, account);
            if (!disableDocs && docs is null || !account.TrySpend()) { return null; }
            return new(owned, method, docs, account, disableDocs);
        }

        private static bool Identifier(string text)
        {
            if (text.Length == 0 || !(text[0] == '_' || text[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) { return false; }
            foreach (var c in text)
            { if (!(c == '_' || c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')) { return false; } }
            return SyntaxFacts.GetKeywordKind(text) == SyntaxKind.None && SyntaxFacts.GetContextualKeywordKind(text) == SyntaxKind.None;
        }

        internal bool IsCurrent()
        {
            if (_failed || !_account.TrySpend() || !ReferenceEquals(_method.Signature, _signature) ||
                _signature.DeclarationRevision != _revision || _signature.Modifiers != _modifiers ||
                !_account.TrySpendStringComparison(_signature.Name, _name) || _signature.Name != _name ||
                _signature.NonDocumentComment is not null || !ReferenceEquals(_method.XmlDocs, _docs) ||
                _documentation is not null && !_documentation.IsCurrentWithoutInvalidating(_account))
            { _failed = true; return false; }
            return true;
        }

        internal void ObserveDocumentation(object producer)
        {
            if (!_account.TrySpend()) { _failed = true; return; }
            if (_disableDocs || !ReferenceEquals(producer, _docs)) { return; }
            if (_sawDocs || _started || !IsCurrent()) { _failed = true; return; }
            _sawDocs = true; DocumentationEntries++;
        }
        internal void BeginDeclaration(MethodSignatureBase? signature)
        {
            if (_started || !_disableDocs && !_sawDocs || !ReferenceEquals(signature, _signature) || !IsCurrent())
            { _failed = true; return; }
            _started = true; DeclarationStarts++;
        }
        internal void CompleteDeclaration(MethodSignatureBase? signature)
        {
            if (!_started || _finished || !ReferenceEquals(signature, _signature) || !IsCurrent())
            { _failed = true; return; }
            _finished = true; DeclarationCompletions++;
        }
        internal bool HasNativeHead => !_failed && _started && _finished && (_disableDocs || _sawDocs);
    }
}
