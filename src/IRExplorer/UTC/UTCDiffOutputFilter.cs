using IRExplorer.Diff;
using DiffPlex.DiffBuilder.Model;
using System;

namespace IRExplorer.UTC {
    public class UTCDiffOutputFilter : IDiffOutputFilter {
        public char[] IgnoredDiffLetters => new char[] {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!'
        };

        public DiffKind EstimateModificationType(DiffPiece before, DiffPiece after) {
            bool isTemporary(string text, out int number) {
                int prefixLength = 0;
                var name = text.AsSpan();
                number = 0;

                if (name.StartsWith("tv".AsSpan()) || name.StartsWith("hv".AsSpan())) {
                    prefixLength = 2;
                }
                else if (name.StartsWith("t".AsSpan())) {
                    prefixLength = 1;
                }
                else {
                    return false;
                }

                var remainingName = name.Slice(prefixLength);

                foreach (char letter in remainingName) {
                    if (!char.IsDigit(letter)) {
                        return false;
                    }
                }

                return int.TryParse(remainingName, out number);
            }

            if (isTemporary(before.Text, out int beforeNumber) &&
                isTemporary(after.Text, out int afterNumber) &&
                beforeNumber == afterNumber) {
                return DiffKind.MinorModification;
            }

            return DiffKind.Modification;
        }
    }
}
