// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.TypeSpec.Generator.Providers;
using Microsoft.TypeSpec.Generator.Statements;
using Microsoft.TypeSpec.Generator.Utilities;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>
    /// Bounded documentation data that cannot escape the writer's line comments.
    /// This is one prerequisite of a generated declaration envelope, not lookup or selection evidence.
    /// </summary>
    internal sealed class PreparedGeneratedDocumentationEnvelope
    {
        private static readonly Type FactoryStringType = FormattableStringFactory.Create(string.Empty, []).GetType();
        private readonly bool _fileProfile;
        private readonly PocWorkBudget? _captureBudget;
        private readonly List<Func<PocWorkBudget, bool>> _filePrechecks = [];
        private readonly List<Func<PocWorkBudget?, bool>> _checks = [];
        private readonly HashSet<object> _ancestors = new(ReferenceEqualityComparer.Instance);
        private int _stale;
        private readonly HashSet<string> _importNamespaces = new(StringComparer.Ordinal);
        internal IEnumerable<string> ImportNamespaces => _importNamespaces;
        private readonly List<CSharpType> _importTypes = [];
        private readonly List<Type> _deferredRuntimeImportTypes = [];
        internal IEnumerable<CSharpType> ImportTypes => _importTypes;
        // File-profile consumers must account for these canonical runtime
        // identities separately; ImportTypes alone is not complete for them.
        internal IEnumerable<Type> DeferredRuntimeImportTypes => _deferredRuntimeImportTypes;

        private PreparedGeneratedDocumentationEnvelope(bool fileProfile) : this(fileProfile, parentBudget: null) { }

        private PreparedGeneratedDocumentationEnvelope(bool fileProfile, PocWorkBudget? parentBudget)
        {
            _fileProfile = fileProfile;
            _captureBudget = fileProfile ? new PocWorkBudget(4096, parentBudget) : null;
        }

        private bool Spend(int count = 1) => _captureBudget?.TrySpend(count) ?? true;
        private bool SpendAncestorVisit() => _captureBudget is null ||
            _captureBudget.TrySpendReferenceLookup(_ancestors.Count) && _captureBudget.TrySpendReferenceLookup(_ancestors.Count + 1);
        private bool TryCopy<T>(IReadOnlyList<T>? list, out ImmutableArray<T> items) => _captureBudget is null
            ? ContractCollections.TryCopy(list, out items) : ContractCollections.TryCopyForPoc(list, _captureBudget, out items);

        internal static PreparedGeneratedDocumentationEnvelope? TryCapture(XmlDocProvider docs)
        {
            var envelope = new PreparedGeneratedDocumentationEnvelope(fileProfile: false);
            return envelope.Capture(docs, allowExceptionDocumentation: false) ? envelope : null;
        }

        // Separate file-confinement profile: existing call/header consumers keep
        // their original stricter admission and cannot gain new permissions.
        // Raw cached descriptions must be admitted before the native XML-doc
        // builder normalizes them. This conservative file-profile capture invokes
        // no unknown formatter and never grants a spelling or documentation use.
        internal static PreparedGeneratedDocumentationEnvelope? TryCaptureForHeaderTypeArguments(CSharpType? parent, CSharpType? additional)
        {
            // Metadata only: do not construct a preview description or invoke
            // its builder. Both actual operands share the existing file limit.
            var envelope = new PreparedGeneratedDocumentationEnvelope(fileProfile: true);
            return (parent is null || envelope.CaptureArgument(parent, 0, cref: true)) &&
                (additional is null || envelope.CaptureArgument(additional, 0, cref: true)) ? envelope : null;
        }

        internal static PreparedGeneratedDocumentationEnvelope? TryCaptureForHeaderDescription(FormattableString description)
        {
            var envelope = new PreparedGeneratedDocumentationEnvelope(fileProfile: true);
            var normalization = new PocWorkBudget(4096);
            var replacements = 0;
            if (!envelope.CaptureFormat(description, 0) ||
                !IsHeaderNormalizationBounded(description, normalization, 0, ref replacements)) { return null; }
            // Flattened literal fragments and string operands can contribute
            // to the same native escape dictionary. Bound their combined
            // insertion work, not a sum of independent smaller triangles.
            long probes = replacements * (replacements + 1L) / 2;
            return probes <= int.MaxValue && normalization.TrySpend((int)probes) ? envelope : null;
        }

        private static bool IsHeaderNormalizationBounded(FormattableString value, PocWorkBudget budget, int depth, ref int replacements)
        {
            if (depth > 32 || !budget.TrySpend() || value.GetType() != FactoryStringType) { return false; }
            var format = value.Format;
            if (!budget.TrySpend(format.Length) || format.IndexOf('<') >= 0 ||
                format.Length == 0 && value.ArgumentCount != 0) { return false; }
            // Native BreakLines preserves an empty-format object unchanged,
            // including unused arguments that EscapeArguments subsequently scans.
            // Decline that exceptional raw shape rather than normalizing it.
            // Follow the native BreakLines traversal PER placeholder occurrence,
            // not once per argument identity. A duplicated nested format must
            // spend again before recursion, so exponential expansion is refused.
            foreach (var (span, isLiteral) in StringExtensions.GetFormattableStringFormatParts(format.AsSpan()))
            {
                if (!budget.TrySpend(span.Length)) { return false; }
                if (isLiteral)
                {
                    // Bound brace/XML escaping and temporary normalized strings.
                    if (!budget.TrySpend(span.Length * 12)) { return false; }
                    foreach (var character in span) { if (character is '&' or '>') { replacements++; } }
                    continue;
                }
                var index = GetValidatedPocFormatIndex(span);
                if (index < 0 || index >= value.ArgumentCount) { return false; }
                var argument = value.GetArgument(index);
                if (argument is FormattableString nested)
                {
                    if (!IsHeaderNormalizationBounded(nested, budget, depth + 1, ref replacements)) { return false; }
                }
                else if (argument is string text)
                {
                    if (text.Length > 4096 || !budget.TrySpend(text.Length * 12) || text.IndexOf('<') >= 0) { return false; }
                    foreach (var character in text) { if (character is '&' or '>') { replacements++; } }
                }
                else
                {
                    // CaptureFormat already checked the non-string family. It
                    // remains an argument during normalization rather than being
                    // formatted; account for its rewritten placeholder/list slot.
                    if (argument is not (bool or char or CodeWriterDeclaration or CSharpType) || !budget.TrySpend(16)) { return false; }
                }
            }
            return true;
        }

        internal static PreparedGeneratedDocumentationEnvelope? TryCaptureForFile(XmlDocProvider docs) =>
            TryCaptureForFileWithBudget(docs, parentBudget: null);

        internal static PreparedGeneratedDocumentationEnvelope? TryCaptureForFileWithBudget(XmlDocProvider docs, PocWorkBudget? parentBudget)
        {
            // Envelope, child budget and its six initially empty collections.
            // Do not allocate even these fixed objects after parent exhaustion.
            if (parentBudget is not null && !parentBudget.TrySpend(8)) { return null; }
            var envelope = new PreparedGeneratedDocumentationEnvelope(fileProfile: true, parentBudget);
            try { return envelope.Capture(docs, allowExceptionDocumentation: true) ? envelope : null; }
            finally { envelope._captureBudget!.DetachParent(); }
        }

        internal bool IsCurrent => IsCurrentCore(invalidate: true);
        internal bool IsCurrentWithoutInvalidating(PocWorkBudget? budget = null) => IsCurrentCore(invalidate: false, budget);

        internal bool IsCurrentWithoutInvalidating(PocWorkBudget? budget, out bool unavailable) => IsCurrentCore(invalidate: false, budget, out unavailable);

        private bool IsCurrentCore(bool invalidate, PocWorkBudget? budget = null) => IsCurrentCore(invalidate, budget, out _);

        private bool IsCurrentCore(bool invalidate, PocWorkBudget? budget, out bool unavailable)
        {
            unavailable = false;
            if (System.Threading.Volatile.Read(ref _stale) != 0) { return false; }
            if (!SourceCurrentnessChecks.Spend(budget)) { unavailable = budget?.Exhausted == true; return false; }
            if (_fileProfile)
            {
                // Enforce the existing 4,096 child limit while charging each
                // actual precheck to the enclosing query before it executes.
                // A fixed maximum is not the work performed by every envelope.
                var precheckBudget = new PocWorkBudget(4096, budget);
                if (_filePrechecks.Any(check => !check(precheckBudget)))
                {
                    // A child can exhaust without exhausting its parent. Keep
                    // unavailability distinct from an observed mismatch.
                    unavailable = precheckBudget.Exhausted;
                    if (invalidate) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
                    return false;
                }
            }
            if (_checks.All(check => SourceCurrentnessChecks.Spend(budget) && check(budget))) { return true; }
            unavailable = budget?.Exhausted == true;
            if (invalidate) { System.Threading.Interlocked.Exchange(ref _stale, 1); }
            return false;
        }

        private bool Capture(XmlDocProvider docs, bool allowExceptionDocumentation)
        {
            if (!Spend() || docs.GetType() != typeof(XmlDocProvider) ||
                !TryCopy(docs.Parameters, out var parameters) ||
                !TryCopy(docs.Exceptions, out var exceptions) ||
                (!allowExceptionDocumentation && !exceptions.IsEmpty)) { return false; }
            if (_fileProfile)
            {
                _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(docs.Parameters, budget));
                _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(docs.Exceptions, budget));
            }
            var summary = docs.Summary;
            var returns = docs.Returns;
            var inherit = docs.Inherit;
            _checks.Add(budget => ReferenceEquals(docs.Summary, summary) && ReferenceEquals(docs.Returns, returns) &&
                ReferenceEquals(docs.Inherit, inherit) && SourceCurrentnessChecks.References(docs.Parameters, parameters, budget) &&
                SourceCurrentnessChecks.References(docs.Exceptions, exceptions, budget));
            return (summary is null || CaptureStatement(summary, 0)) &&
                (returns is null || CaptureStatement(returns, 0)) && parameters.All(p => p is not null && CaptureStatement(p, 0)) &&
                (inherit is null || inherit.GetType() == typeof(XmlDocInheritStatement)) &&
                exceptions.All(exception => exception is not null && CaptureException(exception));
        }

        private bool CaptureException(XmlDocExceptionStatement exception)
        {
            // This profile confines only the standard empty-parameter writer.
            // Never execute an unknown writer, collection or virtual Type accessor.
            if (!Spend() || exception.GetType() != typeof(XmlDocExceptionStatement) ||
                !TryCopy(exception.Parameters, out var parameters) || !parameters.IsEmpty ||
                exception.ExceptionType is not { } runtimeType || !IsFileRuntimeTypeSafe(runtimeType, 0) ||
                exception.Reason is not { } reason || !Spend(reason.Length) || !SingleLine(reason)) { return false; }
            if (_fileProfile) { _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(exception.Parameters, budget)); }
            // Do not construct a CSharpType here: that can materialize and
            // cache its base chain or throw before an inherit-only writer skips
            // this exception. Reuse an existing canonical type when available.
            CSharpType? type;
            if (_fileProfile)
            {
                // A dictionary lookup can dispatch foreign Type-key equality,
                // even for an exact runtime exception the inherit writer skips.
                if (!CSharpType.TryGetPocCachedRuntimeType(runtimeType, _captureBudget!, out type) ||
                    !CaptureRuntimeBaseCache(runtimeType, 0)) { return false; }
                var capturedRuntimeType = type;
                _filePrechecks.Add(budget => CSharpType.TryGetPocCachedRuntimeType(runtimeType, budget, out var current) &&
                    ReferenceEquals(current, capturedRuntimeType));
            }
            else { CSharpType.TryGetCachedRuntimeType(runtimeType, out type); }
            if (type is not null)
            {
                if (!SafeType(type, 0) || !MatchesRuntimeType(type, runtimeType, 0) || !IsFileFormattedTypeSafe(type, 0) ||
                    !(_captureBudget is null ? DeclaredTypeSnapshot.TryCapture(type, out var snapshot) : DeclaredTypeSnapshot.TryCaptureForPoc(type, _captureBudget, out snapshot))) { return false; }
                _checks.Add(budget => ReferenceEquals(exception.ExceptionType, runtimeType) && SourceCurrentnessChecks.Text(exception.Reason, reason, budget) &&
                    SourceCurrentnessChecks.References(exception.Parameters, parameters, budget) && SourceCurrentnessChecks.Type(snapshot, budget));
                if (!CaptureImportNamespaces(type)) { return false; }
                _importTypes.Add(type);
                return true;
            }
            // Preserve the established safe core-library exception family
            // without synthesizing a CSharpType or populating the cache. Other
            // uncached runtime types remain unavailable until independently proved.
            if (!ReferenceEquals(runtimeType.Assembly, typeof(Exception).Assembly) || !CaptureUncachedCoreRuntimeType(runtimeType, 0)) { return false; }
            _checks.Add(budget => ReferenceEquals(exception.ExceptionType, runtimeType) && SourceCurrentnessChecks.Text(exception.Reason, reason, budget) &&
                SourceCurrentnessChecks.References(exception.Parameters, parameters, budget));
            _deferredRuntimeImportTypes.Add(runtimeType);
            return true;
        }

        private bool CaptureStatement(XmlDocStatement statement, int depth)
        {
            var type = statement.GetType();
            if (!SpendAncestorVisit() || depth > 32 || (type != typeof(XmlDocStatement) && type != typeof(XmlDocSummaryStatement) &&
                type != typeof(XmlDocParamStatement) && type != typeof(XmlDocReturnsStatement)) || !_ancestors.Add(statement)) { return false; }
            if (!TryCopy(statement.Lines, out var lines) || !TryCopy(statement.InnerStatements, out var children)) { return false; }
            if (_fileProfile)
            {
                _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(statement.Lines, budget));
                _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(statement.InnerStatements, budget));
            }
            var start = statement.StartTag;
            var end = statement.EndTag;
            _checks.Add(budget => ReferenceEquals(statement.StartTag, start) && ReferenceEquals(statement.EndTag, end) &&
                SourceCurrentnessChecks.References(statement.Lines, lines, budget) && SourceCurrentnessChecks.References(statement.InnerStatements, children, budget));
            var result = CaptureFormat(start, depth + 1) && CaptureFormat(end, depth + 1) &&
                lines.All(line => CaptureFormat(line, depth + 1)) && children.All(child => child is not null && CaptureStatement(child, depth + 1));
            _ancestors.Remove(statement);
            return result;
        }

        private bool CaptureFormat(FormattableString value, int depth)
        {
            // Type check precedes every user-overridable formatter accessor.
            if (!SpendAncestorVisit() || value is null || depth > 32 || value.GetType() != FactoryStringType || !_ancestors.Add(value)) { return false; }
            var format = value.Format;
            var rawArguments = value.GetArguments();
            if (!Spend(format.Length) || !Spend(rawArguments.Length)) { return false; }
            if (_fileProfile) { _filePrechecks.Add(budget => budget.TrySpend(value.Format.Length) && budget.TrySpend(value.GetArguments().Length)); }
            var arguments = rawArguments.ToArray();
            if (!SafeFormat(format, arguments.Length)) { return false; }
            if (_fileProfile)
            {
                // Numeric indices select operands on every native occurrence;
                // the argument array alone does not bound repeated expansion.
                if (!Spend(arguments.Length)) { return false; }
                var used = new bool[arguments.Length];
                foreach (var (span, literal) in StringExtensions.GetFormattableStringFormatParts(format.AsSpan()))
                {
                    if (literal) { continue; }
                    var index = GetValidatedPocFormatIndex(span);
                    if (!CaptureArgument(arguments[index], depth + 1, span.EndsWith(":C", StringComparison.Ordinal))) { return false; }
                    used[index] = true;
                }
                // Preserve rejection of unsafe unused operands as well.
                for (int i = 0; i < arguments.Length; i++)
                { if (!used[i] && !CaptureArgument(arguments[i], depth + 1)) { return false; } }
            }
            else if (arguments.Any(arg => !CaptureArgument(arg, depth + 1))) { return false; }
            _checks.Add(budget => SourceCurrentnessChecks.Text(value.Format, format, budget) && SameArguments(value.GetArguments(), arguments, budget));
            _ancestors.Remove(value);
            return true;
        }

        private bool CaptureArgument(object? argument, int depth, bool cref = false)
        {
            if (!Spend() || depth > 32) { return false; }
            switch (argument)
            {
                case null:
                    return false;
                case string text:
                    return Spend(text.Length) && SingleLine(text);
                case char character:
                    return SingleLine(character.ToString());
                // The writer's numeric ToString fallback is culture-sensitive.
                // A later culture change can introduce newlines even for values
                // captured under invariant culture. Do not certify that family.
                case bool:
                    return true;
                case FormattableString nested:
                    return CaptureFormat(nested, depth);
                case CodeWriterDeclaration declaration:
                    var name = declaration.RequestedName;
                    var revision = declaration.SourceRevision;
                    if ((_fileProfile && name is null) || !Spend(name.Length) || !Identifier(name)) { return false; }
                    _checks.Add(budget => declaration.SourceRevision == revision && SourceCurrentnessChecks.Text(declaration.RequestedName, name, budget));
                    return true;
                case CSharpType type:
                    if (_fileProfile && cref && type.IsFrameworkType && type.IsNullable)
                    {
                        // Cref clears nullability using a runtime constructor,
                        // not the current IR name alone. Observe its provenance
                        // and actual cache dependency without replaying it.
                        if (!type.TrySpendPocNullableRuntimeConstruction(_captureBudget!, out var cachedBase)) { return false; }
                        _filePrechecks.Add(budget => type.TrySpendPocNullableRuntimeConstruction(budget, out var currentBase) &&
                            ReferenceEquals(currentBase, cachedBase));
                    }
                    if (!SafeType(type, depth) || (_fileProfile && !IsFileFormattedTypeSafe(type, depth)) ||
                        !(_captureBudget is null ? DeclaredTypeSnapshot.TryCapture(type, out var snapshot) : DeclaredTypeSnapshot.TryCaptureForPoc(type, _captureBudget, out snapshot))) { return false; }
                    _checks.Add(budget => SourceCurrentnessChecks.Type(snapshot, budget));
                    if (!CaptureImportNamespaces(type)) { return false; }
                    _importTypes.Add(type);
                    return true;
                default:
                    return false;
            }
        }

        private bool SafeType(CSharpType type, int depth)
        {
            if (_fileProfile && (type.Name is null || type.Namespace is null)) { return false; }
            if (!Spend() || depth > 32 || type.GetType() != typeof(CSharpType) || !Spend(type.Name.Length) || !Identifier(type.Name) ||
                !Spend(type.Namespace.Length) || type.Namespace.Length != 0 && !type.Namespace.Split('.').All(Identifier) ||
                !TryCopy(type.Arguments, out var arguments) || _fileProfile && !arguments.IsEmpty) { return false; }
            if (_fileProfile) { _filePrechecks.Add(budget => ContractCollections.ReserveForPoc(type.Arguments, budget)); }
            return arguments.All(argument => argument is not null && SafeType(argument, depth + 1)) &&
                (type.DeclaringType is null || SafeType(type.DeclaringType, depth + 1));
        }

        // The native cref formatter can reconstruct generic arguments from a
        // runtime definition. Until that separate family is audited, reject it
        // in this file-only profile before executing those metadata operations.
        private bool IsFileFormattedTypeSafe(CSharpType type, int depth) =>
            IsFileFormattedTypeGraphSafe(type, depth) && PreparedCallImportScope.HasPureFormattedTypes([type], _captureBudget);

        private bool IsFileFormattedTypeGraphSafe(CSharpType type, int depth)
        {
            // The import helper will scan every canonical namespace once.
            // Charge that work before calling it, then call it only once for
            // the complete declaring-type chain.
            if (depth > 32 || !Spend() || !Spend(type.Namespace.Length) || !TryCopy(type.Arguments, out var arguments) || !arguments.IsEmpty ||
                (type.IsFrameworkType && !ChargeRuntimeNamespaces(type.FrameworkType, depth + 1)) ||
                (type.DeclaringType is { } declaring && !IsFileFormattedTypeGraphSafe(declaring, depth + 1))) { return false; }
            return true;
        }

        private bool ChargeRuntimeNamespaces(Type type, int depth)
        {
            // Validate and charge each node once in this pass. The import
            // helper will separately inspect the same bounded chain.
            if (depth > 32 || !Spend() || !IsFileRuntimeTypeNodeSafe(type) || !Spend((type.Namespace ?? string.Empty).Length)) { return false; }
            return type.DeclaringType is not { } declaring || ChargeRuntimeNamespaces(declaring, depth + 1);
        }

        private static bool IsFileRuntimeTypeSafe(Type type, int depth)
        {
            if (depth > 32 || !IsFileRuntimeTypeNodeSafe(type)) { return false; }
            return type.DeclaringType is not { } declaring || IsFileRuntimeTypeSafe(declaring, depth + 1);
        }

        private static bool IsFileRuntimeTypeNodeSafe(Type type) => type.GetType() == typeof(object).GetType() &&
            !type.IsGenericType && !type.IsGenericParameter && !type.HasElementType;

        private bool CaptureRuntimeBaseCache(Type runtimeType, int depth)
        {
            // Native runtime construction converts its base through the global
            // cache. Observe that actual entry, not a target's historical base.
            if (depth > 32 || !Spend(48) || !IsFileRuntimeTypeSafe(runtimeType, depth)) { return false; }
            if (runtimeType.BaseType is { } baseType)
            {
                if (!CSharpType.TryGetPocCachedRuntimeType(baseType, _captureBudget!, out var cachedBase) ||
                    cachedBase is not null && (cachedBase.GetType() != typeof(CSharpType) || !cachedBase.IsFrameworkType ||
                        !ReferenceEquals(cachedBase.FrameworkType, baseType))) { return false; }
                _filePrechecks.Add(budget => CSharpType.TryGetPocCachedRuntimeType(baseType, budget, out var current) &&
                    ReferenceEquals(current, cachedBase));
                if (cachedBase is null && (!CaptureUncachedCoreRuntimeType(baseType, depth + 1) ||
                    !CaptureRuntimeBaseCache(baseType, depth + 1))) { return false; }
            }
            return runtimeType.DeclaringType is not { } declaring || CaptureRuntimeBaseCache(declaring, depth + 1);
        }

        private bool CaptureUncachedCoreRuntimeType(Type type, int depth)
        {
            if (!Spend() || !IsFileRuntimeTypeSafe(type, depth)) { return false; }
            var name = type.Name;
            var ns = type.Namespace ?? string.Empty;
            if (!Spend(name.Length) || !Identifier(name) || !Spend(ns.Length) || ns.Length != 0 && !ns.Split('.').All(Identifier)) { return false; }
            if (!CaptureImportNamespace(ns)) { return false; }
            return type.DeclaringType is not { } declaring || CaptureUncachedCoreRuntimeType(declaring, depth + 1);
        }

        private static bool MatchesRuntimeType(CSharpType type, Type runtimeType, int depth)
        {
            if (depth > 32 || !type.IsFrameworkType || !ReferenceEquals(type.FrameworkType, runtimeType) ||
                type.Name != runtimeType.Name || type.Namespace != (runtimeType.Namespace ?? string.Empty)) { return false; }
            var declaring = runtimeType.DeclaringType;
            return declaring is null ? type.DeclaringType is null : type.DeclaringType is { } parent && MatchesRuntimeType(parent, declaring, depth + 1);
        }

        private bool CaptureImportNamespace(string name)
        {
            // The namespace's own length does not cover set insertion chains
            // or equal-hash comparisons. Fund each possible probe BEFORE Add.
            // Keep the established non-file profile's admission unchanged.
            if (_fileProfile)
            {
                long work = (name.Length + 1L) * (_importNamespaces.Count + 1L);
                if (work > int.MaxValue || !Spend((int)work)) { return false; }
            }
            _importNamespaces.Add(name);
            return true;
        }

        private bool CaptureImportNamespaces(CSharpType type)
        {
            // SafeType already audited this owned, acyclic, bounded type graph.
            if (!CaptureImportNamespace(type.Namespace) ||
                type.DeclaringType is { } declaring && !CaptureImportNamespaces(declaring)) { return false; }
            if (ContractCollections.TryCopy(type.Arguments, out var arguments))
            {
                foreach (var argument in arguments) { if (!CaptureImportNamespaces(argument)) { return false; } }
            }
            return true;
        }

        private static bool SameArguments(object?[] current, object?[] captured, PocWorkBudget? budget)
        {
            if (current.Length != captured.Length || !SourceCurrentnessChecks.Spend(budget, captured.Length)) { return false; }
            for (var index = 0; index < current.Length; index++)
            {
                if (!ReferenceEquals(current[index], captured[index])) { return false; }
            }
            return true;
        }

        private static bool SingleLine(string value) => value.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) < 0;

        private static bool Identifier(string value) => value.Length > 0 && (value[0] == '_' || char.IsAsciiLetter(value[0])) &&
            value.All(c => c == '_' || char.IsAsciiLetterOrDigit(c));

        // SafeFormat has already proved nonempty ASCII digits, optional :I/:C,
        // range and overflow, and prepaid the format characters. Preserve the
        // native part boundaries without its culture-sensitive int.Parse.
        private static int GetValidatedPocFormatIndex(ReadOnlySpan<char> span)
        {
            var ordinal = 0;
            foreach (var character in span)
            {
                if (character == ':') { break; }
                ordinal = ordinal * 10 + character - '0';
            }
            return ordinal;
        }

        private static bool SafeFormat(string format, int argumentCount)
        {
            if (!SingleLine(format)) { return false; }
            for (var index = 0; index < format.Length; index++)
            {
                var c = format[index];
                if (c != '{' && c != '}') { continue; }
                if (index + 1 < format.Length && format[index + 1] == c) { index++; continue; }
                if (c == '}') { return false; }
                index++;
                var start = index;
                var ordinal = 0;
                while (index < format.Length && char.IsAsciiDigit(format[index]))
                {
                    if (ordinal > (int.MaxValue - 9) / 10) { return false; }
                    ordinal = ordinal * 10 + format[index++] - '0';
                }
                if (index == start || ordinal >= argumentCount) { return false; }
                if (index < format.Length && format[index] == ':')
                {
                    index++;
                    // No declaration-emitting D format, arbitrary formatters or alignments.
                    if (index >= format.Length || (format[index] != 'I' && format[index] != 'C')) { return false; }
                    index++;
                }
                if (index >= format.Length || format[index] != '}') { return false; }
            }
            return true;
        }
    }
}
