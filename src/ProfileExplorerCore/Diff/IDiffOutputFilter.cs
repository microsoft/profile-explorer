// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using DiffPlex.DiffBuilder.Model;
using ProfileExplorerCore.Document.Renderers.Highlighters;
using ProfileExplorerCore.Settings;

namespace ProfileExplorerCore.Diff;

public interface IDiffOutputFilter {
  char[] IgnoredDiffLetters { get; }
  void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo);

  DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset,
                                    string beforeDocumentText, string afterDocumentText);

  AdjustedDiffPiece AdjustChange(DiffPiece change, int offset, int lineOffset, string lineText);
}

public interface IDiffInputFilter {
  void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo);
  FilteredDiffInput FilterInputText(string text);
  string FilterInputLine(string line);
}

public struct AdjustedDiffPiece {
  public AdjustedDiffPiece(int offset, int length) {
    Offset = offset;
    Length = length;
  }

  public int Offset { get; set; }
  public int Length { get; set; }
}

public class FilteredDiffInput {
  public static List<Replacement> NoReplacements = new(0);

  public FilteredDiffInput(int capacity) {
    Text = string.Empty;
    LineReplacements = new List<List<Replacement>>(capacity);
  }

  public FilteredDiffInput(string text) {
    Text = text;
    LineReplacements = null;
  }

  public string Text { get; set; }
  public List<List<Replacement>> LineReplacements { get; set; }

  public struct Replacement {
    public Replacement(int offset, string replaced, string original) {
      Offset = offset;
      Replaced = replaced;
      Original = original;
    }

    public int Offset { get; set; }
    public string Replaced { get; set; }
    public string Original { get; set; }
    public int Length => Replaced.Length;
  }
}

public class BasicDiffOutputFilter : IDiffOutputFilter {
  public char[] IgnoredDiffLetters => new[] {
    '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t'
  };

  public AdjustedDiffPiece AdjustChange(DiffPiece change, int documentOffset, int lineOffset, string lineText) {
    return new AdjustedDiffPiece(documentOffset, change.Text.Length);
  }

  public DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset,
                                           string beforeDocumentText, string afterDocumentText) {
    return DiffKind.Modification;
  }

  public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
  }
}