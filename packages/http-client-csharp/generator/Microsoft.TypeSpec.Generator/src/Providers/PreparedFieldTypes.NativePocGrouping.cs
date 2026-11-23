// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TypeSpec.Generator.Primitives;

namespace Microsoft.TypeSpec.Generator.Providers
{
    internal sealed partial class PreparedFieldTypes
    {
        // Evidence from the existing native pass, never a successful permission.
        private readonly HashSet<PreparedSourceFile> _nativePocFiles = [];
        private readonly Dictionary<TypeProvider, CodeFile> _nativeWrittenFiles = new(ReferenceEqualityComparer.Instance);
        private bool _nativePocInvalid;
        private string? _nativePocOutputDirectory;
        private bool HasNativePocPlans => _nativePocFiles.Count != 0;
        private bool HasLegacyPocPlans => _pocGrouping.Values.Any(choice => !choice.File.UsesNativeWrittenIdentity);
        private bool HasLegacyPocUses => _pocUsedByFile.Values.Any(use => !use.File.UsesNativeWrittenIdentity);

        internal void ObserveNativePocHeader(CodeModelGenerator generator)
        {
            // The actual header is part of the written source, including when
            // a provider callback temporarily changed the generator before it.
            // Keep this veto on the native-written inventory, not legacy choices.
            if (HasNativePocPlans && !ReferenceEquals(_sourceConfiguration, generator.Configuration))
            {
                _nativePocInvalid = true;
            }
        }

        internal bool IsNativePocFile(CodeFile output) => output.SourceConsumption is { } receipt &&
            _nativePocFiles.Any(file => file.Generation == receipt.Generation && file.FilePath == receipt.File);

        internal void ObserveNativeWrittenFile(TypeProvider provider, CodeFile output)
        {
            if (!HasNativePocPlans)
            { return; }
            if (!_nativeWrittenFiles.TryAdd(provider, output))
            { _nativePocInvalid = true; }
        }

        private static bool CachedPocFormals(PreparedSourceFile file) =>
            file.Roots.All(root => root.HasCachedParameterEmittedStates);

        private bool NativePocContextDataIsCurrent()
        {
            // Live private path/serialization queries are deliberately absent.
            // Physical identities are proved from the native output inventory.
            return !_nativePocInvalid &&
                ReferenceEquals(CodeModelGenerator.Instance.CachedSourceInputModel, _sourceModel) &&
                _sourceConfiguration is not null && ReferenceEquals(CodeModelGenerator.Instance.Configuration, _sourceConfiguration) &&
                _sourceConfiguration.DisableXmlDocs == _sourceDisableXmlDocs && _sourceDisableXmlDocs &&
                IsPolicyEnabled(CodeModelGenerator.Instance) &&
                ContractCollections.SameReferences(_sourceRootList, _sourceRootSnapshot) &&
                (_sourceInputs is null || _sourceModel is not null && ReferenceEquals(_sourceModel.PreEmissionSourceInputs, _sourceInputs)) &&
                _callEmissionKnown && _callLookupScopes.All(scope => scope.IsNativeWrittenContextCurrent()) &&
                _callEmissionChecks.All(check => check());
        }

        private bool NativePocDataIsCurrent(PreparedSourceFile file) =>
            _nativePocFiles.Contains(file) && CachedPocFormals(file) && NativePocContextDataIsCurrent() && file.IsCurrent();

        private bool ObserveNativePocSource(PreparedSourceFile file)
        {
            // No currentness walk may compute an unwritten formal's spelling.
            if (!CachedPocFormals(file))
            { return false; }
            bool contextCurrent = NativePocContextDataIsCurrent() && SourceInputsAreCurrent() && NativePocContextDataIsCurrent();
            if (!contextCurrent)
            { _nativePocInvalid = true; return false; }
            // A local AST/frame expiry is sticky on its own source file. It is
            // not a failure of another file's source or the shared inventory.
            return NativePocDataIsCurrent(file);
        }

        private bool NativePocUseIsCurrent(PreparedSourceFile file, Func<bool> envelope, Func<bool> finalData) =>
            CachedPocFormals(file) && envelope() && file.IsCurrent() && finalData();

        private bool NativeWrittenInventoryIsCurrent(IReadOnlyList<CodeFile>? actualFiles = null)
        {
            if (_nativePocInvalid || _sourceConfiguration is null ||
                _nativeWrittenFiles.Count != _sourceFiles.Count ||
                _sourceFiles.Keys.Any(owner => !_nativeWrittenFiles.ContainsKey(owner)))
            { return false; }
            if (actualFiles is not null && (actualFiles.Count != _nativeWrittenFiles.Count ||
                actualFiles.Any(file => !_nativeWrittenFiles.Values.Any(written => ReferenceEquals(file, written)))))
            { return false; }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var directory = Path.GetFullPath(_nativePocOutputDirectory ?? _sourceConfiguration.OutputDirectory);
                var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                foreach (var output in _nativeWrittenFiles.Values)
                {
                    if (Path.IsPathRooted(output.Name))
                    { return false; }
                    var path = Path.GetFullPath(output.Name, directory);
                    if (path.Equals(directory, comparison) || !path.StartsWith(prefix, comparison) ||
                        !names.Add(path) || HasUnstablePath(path))
                    { return false; }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            { return false; }
            return true;
        }

        private bool CanCommitNativePocFile(CodeFile output)
        {
            if (!TryMatchPocFileUse(output, out var file, out var envelope, out var finalData) ||
                !_nativeWrittenFiles.TryGetValue(file.FileOwner, out var written) || !ReferenceEquals(written, output) ||
                !CachedPocFormals(file) || !NativeWrittenInventoryIsCurrent())
            { return false; }
            return envelope() && finalData();
        }

        private bool SealNativePocFile(CodeFile output) =>
            TryMatchPocFileUse(output, out var file, out _, out var finalData) &&
            _nativeWrittenFiles.TryGetValue(file.FileOwner, out var written) && ReferenceEquals(written, output) &&
            CachedPocFormals(file) && NativeWrittenInventoryIsCurrent() && finalData();

        private bool NativePocSelectionDataIsCurrent(CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<CodeFile> files) =>
            ReferenceEquals(CodeModelGenerator.Instance, generator) && ReferenceEquals(generator.CachedSourceInputModel, _sourceModel) &&
            ReferenceEquals(output.CachedTypeProviders, _sourceRootList) &&
            ContractCollections.SameReferences(_sourceRootList, _sourceRootSnapshot) && NativeWrittenInventoryIsCurrent(files) &&
            NativePocContextDataIsCurrent();

        internal bool CanCommitNativePocGrouping(CodeModelGenerator generator, OutputLibrary output, IReadOnlyList<CodeFile> files)
        {
            if (!HasNativePocPlans)
            { return false; }
            return NativePocSelectionDataIsCurrent(generator, output, files) && SourceInputsAreCurrent() &&
                NativePocSelectionDataIsCurrent(generator, output, files);
        }

        internal bool SealNativePocGroupingWithoutCallbacks(CodeFile file, CodeModelGenerator generator,
            OutputLibrary output, IReadOnlyList<CodeFile> files) =>
            NativePocSelectionDataIsCurrent(generator, output, files) && SealNativePocFile(file);
    }
}
