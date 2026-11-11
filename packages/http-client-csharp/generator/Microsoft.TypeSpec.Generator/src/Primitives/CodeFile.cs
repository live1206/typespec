// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    public class CodeFile
    {
        private readonly ImmutableArray<PreparedNameAlternative> _nameAlternatives;
        internal int PreparedBodyNameCount => _nameAlternatives.IsDefault ? 0 :
            _nameAlternatives.Count(alternative => alternative.Channel == PreparedNameChannel.Body);
        internal int PreparedPocNameCount => _nameAlternatives.IsDefault ? 0 :
            _nameAlternatives.Count(alternative => alternative.Channel == PreparedNameChannel.Poc);

        internal CodeFile(string content, string name, string? qualifiedContent = null, SourceConsumptionReceipt? sourceConsumption = null,
            ImmutableArray<PreparedNameAlternative> nameAlternatives = default)
        {
            Name = name;
            Content = content;
            QualifiedContent = qualifiedContent;
            SourceConsumption = sourceConsumption;
            _nameAlternatives = nameAlternatives;
        }

        public string Name { get; }

        public string Content { get; }

        internal string? QualifiedContent { get; }
        internal SourceConsumptionReceipt? SourceConsumption { get; }

        internal CodeFile WithoutPreparedNames() => QualifiedContent is null ? this : new CodeFile(QualifiedContent, Name, sourceConsumption: SourceConsumption);

        // Existing callers grant only their established channels. A POC choice
        // requires its own explicit permission, never an old all-true fast path.
        internal CodeFile SelectPreparedNames(bool fieldAllowed, bool bodyAllowed) =>
            SelectPreparedNames(fieldAllowed, bodyAllowed, pocAllowed: false);

        // Final selection only. Shared failure must deny every channel; a
        // POC-only failure must leave independent field/body decisions intact.
        // A rendered result is terminal, not a new preparation lifetime.
        internal CodeFile SelectPreparedNames(bool fieldAllowed, bool bodyAllowed, bool pocAllowed)
        {
            if (QualifiedContent is null || (fieldAllowed && bodyAllowed && (pocAllowed || PreparedPocNameCount == 0)))
            {
                return this;
            }
            if (!fieldAllowed && !bodyAllowed && !pocAllowed)
            {
                return WithoutPreparedNames();
            }
            // Older callers supplied only the two buffers; they have no BODY use.
            if (_nameAlternatives.IsDefault)
            {
                return fieldAllowed ? this : WithoutPreparedNames();
            }

            var builder = new StringBuilder(QualifiedContent.Length);
            int previousEnd = 0;
            int copiedUntil = 0;
            foreach (var alternative in _nameAlternatives)
            {
                // Validate every captured range, including disabled channels.
                // Never search for text, rebind it, or infer a new reduction.
                if (alternative.QualifiedOffset < previousEnd || alternative.QualifiedLength <= 0 ||
                    alternative.QualifiedOffset > QualifiedContent.Length ||
                    alternative.QualifiedLength > QualifiedContent.Length - alternative.QualifiedOffset ||
                    string.IsNullOrEmpty(alternative.ShortSpelling) ||
                    alternative.ShortSpelling.Length >= alternative.QualifiedLength ||
                    (alternative.Channel != PreparedNameChannel.Field && alternative.Channel != PreparedNameChannel.Body && alternative.Channel != PreparedNameChannel.Poc))
                {
                    return WithoutPreparedNames();
                }
                previousEnd = alternative.QualifiedOffset + alternative.QualifiedLength;
                bool allowed = alternative.Channel switch
                {
                    PreparedNameChannel.Field => fieldAllowed,
                    PreparedNameChannel.Body => bodyAllowed,
                    _ => pocAllowed
                };
                if (!allowed)
                {
                    continue;
                }
                builder.Append(QualifiedContent, copiedUntil, alternative.QualifiedOffset - copiedUntil);
                builder.Append(alternative.ShortSpelling);
                copiedUntil = previousEnd;
            }
            builder.Append(QualifiedContent, copiedUntil, QualifiedContent.Length - copiedUntil);
            return new CodeFile(builder.ToString(), Name, sourceConsumption: SourceConsumption);
        }
    }
}
