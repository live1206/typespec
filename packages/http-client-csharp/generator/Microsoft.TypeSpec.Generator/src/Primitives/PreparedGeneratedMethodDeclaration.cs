// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.TypeSpec.Generator.Providers;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    /// <summary>
    /// A snapshot of an actual provider declaration on the standard writer path.
    /// This is not a selected call, complete lookup group, or declaration-envelope proof.
    /// </summary>
    internal sealed class PreparedGeneratedMethodDeclaration
    {
        internal MethodProvider Method { get; }
        internal TypeProvider Owner { get; }
        internal DeclaredCallableContract Contract { get; }
        internal PreparedGeneratedDocumentationEnvelope? DocumentationEnvelope { get; }
        private readonly PreparedSourceFile.OwnerSnapshot _ownerSnapshot;
        private readonly StructuredSourceShape _shape;
        private readonly long _revision;
        private readonly bool _nativeWrittenIdentity;
        private int _stale;

        private PreparedGeneratedMethodDeclaration(MethodProvider method, DeclaredCallableContract contract, bool nativeWrittenIdentity)
        {
            Method = method;
            Owner = method.EnclosingType;
            Contract = contract;
            DocumentationEnvelope = PreparedGeneratedDocumentationEnvelope.TryCapture(method.XmlDocs);
            _ownerSnapshot = new(Owner);
            _shape = StructuredSourceShape.Capture(method, true);
            _revision = method.DeclarationRevision;
            _nativeWrittenIdentity = nativeWrittenIdentity;
        }

        internal static PreparedGeneratedMethodDeclaration? TryCapture(MethodProvider method, Compilation compilation) =>
            TryCaptureCore(method, compilation, nativeWrittenIdentity: false);

        internal static PreparedGeneratedMethodDeclaration? TryCaptureForNativeWrittenGrouping(MethodProvider method, Compilation compilation)
        {
            // A current original-symbol contract can still differ from the
            // emitted formals, including optional caller-expression observers.
            // This lane has no proof of correspondence for retained metadata.
            if (method.Signature.OriginalSymbol is not null || !ContractCollections.TryCopy(method.Signature.Parameters, out var parameters) ||
                parameters.Any(parameter => parameter is null || parameter.OriginalSymbol is not null)) { return null; }
            return TryCaptureCore(method, compilation, nativeWrittenIdentity: true);
        }

        private static PreparedGeneratedMethodDeclaration? TryCaptureCore(MethodProvider method, Compilation compilation, bool nativeWrittenIdentity)
        {
            if (!CodeWriter.HasMethodDeclaration(method)) { return null; }
            // FromProvider validates exact membership in the captured owner. It
            // remains declaration metadata: selection additionally needs the full
            // written lookup surface, applicability and emission-envelope proof.
            var contract = DeclaredCallableContract.FromProvider(method, compilation);
            return contract.IsAvailable && contract.IsCurrent() ? new(method, contract, nativeWrittenIdentity) : null;
        }

        internal bool IsCurrent
        {
            get
            {
                if (System.Threading.Volatile.Read(ref _stale) != 0) { return false; }
                if (Method.DeclarationRevision == _revision && ReferenceEquals(Method.EnclosingType, Owner) && CodeWriter.HasMethodDeclaration(Method) &&
                    _ownerSnapshot.IsCurrent(_nativeWrittenIdentity) && Contract.IsCurrent() && (DocumentationEnvelope is null || DocumentationEnvelope.IsCurrent) &&
                    _shape.Matches(StructuredSourceShape.Capture(Method, true))) { return true; }
                System.Threading.Interlocked.Exchange(ref _stale, 1);
                return false;
            }
        }
    }
}
