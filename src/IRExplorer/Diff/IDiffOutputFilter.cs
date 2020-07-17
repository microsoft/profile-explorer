using DiffPlex.DiffBuilder.Model;

namespace IRExplorer.Diff {
    public interface IDiffOutputFilter {
        char[] IgnoredDiffLetters { get; }
        DiffKind EstimateModificationType(DiffPiece before, DiffPiece after);
    }
}
