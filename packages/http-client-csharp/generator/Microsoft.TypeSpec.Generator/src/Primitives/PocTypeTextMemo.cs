// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // One POC file-currentness query owns this bounded cache. It stores only
    // positive syntax facts about immutable strings, never source freshness or
    // producer identities. Different string instances must be checked anew.
    internal sealed class PocTypeTextMemo
    {
        private string? _name0, _name1, _name2, _name3;
        private string? _namespace0, _namespace1, _namespace2, _namespace3;
        private int _nextName = -1, _nextNamespace = -1;

        internal bool TryFind(string text, bool isNamespace, PocWorkBudget budget, out bool found)
        {
            found = false;
            for (int index = 0; index < 4; index++)
            {
                if (!budget.TrySpend()) { return false; }
                var entry = isNamespace ? ReadNamespace(index) : ReadName(index);
                if (ReferenceEquals(entry, text)) { found = true; return true; }
            }
            return true;
        }

        internal bool TryRemember(string text, bool isNamespace, PocWorkBudget budget)
        {
            if (!budget.TrySpend()) { return false; }
            var index = (isNamespace ? Interlocked.Increment(ref _nextNamespace) : Interlocked.Increment(ref _nextName)) & 3;
            if (isNamespace) { WriteNamespace(index, text); }
            else { WriteName(index, text); }
            return true;
        }

        private string? ReadName(int index) => index switch
        {
            0 => Volatile.Read(ref _name0), 1 => Volatile.Read(ref _name1),
            2 => Volatile.Read(ref _name2), _ => Volatile.Read(ref _name3)
        };
        private string? ReadNamespace(int index) => index switch
        {
            0 => Volatile.Read(ref _namespace0), 1 => Volatile.Read(ref _namespace1),
            2 => Volatile.Read(ref _namespace2), _ => Volatile.Read(ref _namespace3)
        };
        private void WriteName(int index, string text)
        {
            switch (index)
            {
                case 0: Volatile.Write(ref _name0, text); break;
                case 1: Volatile.Write(ref _name1, text); break;
                case 2: Volatile.Write(ref _name2, text); break;
                default: Volatile.Write(ref _name3, text); break;
            }
        }
        private void WriteNamespace(int index, string text)
        {
            switch (index)
            {
                case 0: Volatile.Write(ref _namespace0, text); break;
                case 1: Volatile.Write(ref _namespace1, text); break;
                case 2: Volatile.Write(ref _namespace2, text); break;
                default: Volatile.Write(ref _namespace3, text); break;
            }
        }
    }
}
