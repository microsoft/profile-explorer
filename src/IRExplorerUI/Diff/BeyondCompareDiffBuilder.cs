using System;
using System.IO;
using DiffPlex.DiffBuilder.Model;
using System.Diagnostics;
using HtmlAgilityPack;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

#pragma warning disable CA1305, CA1307

namespace IRExplorerUI.Diff {
    class BeyondCompareDiffBuilder {
        private const string BeyondCompareDirectory = @"Beyond Compare 4";
        private const string BeyondCompareExecutable = @"BCompare.exe";

        public static string FindBeyondCompareExecutable() {
            // Look for BC in Program Files.
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                      BeyondCompareDirectory, BeyondCompareExecutable);
            if (File.Exists(path)) {
                return path;
            }

            // If not found, look on PATH.
            path = NativeMethods.GetFullPathFromWindows(BeyondCompareExecutable);
            return path ?? "";
        }

        public static bool HasBeyondCompareExecutable() {
            return !string.IsNullOrEmpty(FindBeyondCompareExecutable());
        }

        public static SideBySideDiffModel ComputeDiffs(string leftText, string rightText, string beyondComparePath) {
            string reportPath = GenerateReport(leftText, rightText, beyondComparePath);

            if (string.IsNullOrEmpty(reportPath)) {
                return null; // Failed to get Beyond Compare results.
            }

            SideBySideDiffModel diff = GenerateDiffsFromReport(reportPath);

            try {
                File.Delete(reportPath);
            }
            catch (Exception ex) {
                Trace.TraceError($"BeyondCompareDiffBuilder: Failed to delete report file: {ex.Message}");
            }

            return diff;
        }

        private static string GenerateReport(string leftText, string rightText, string beyondComparePath) {
            string leftPath;
            string rightPath;
            string scriptPath;
            string reportPath;

            try {
                leftPath = Path.GetTempFileName();
                rightPath = Path.GetTempFileName();
                scriptPath = Path.GetTempFileName();
                reportPath = Path.GetTempFileName();
            }
            catch (Exception ex) {
                Trace.TraceError($"BeyondCompareDiffBuilder: Failed to get temp file names: {ex.Message}");
                return null;
            }

            // Write the text to compare on multiple threads.
            var task1 = Task.Run(() => File.WriteAllText(leftPath, leftText));
            var task2 = Task.Run(() => File.WriteAllText(rightPath, rightText));
            var task3 = Task.Run(() => {
                string scriptBody = string.Format(
                    "file-report layout:side-by-side options:ignore-unimportant output-to:{0} output-options:html-color {1} {2}",
                    reportPath, leftPath, rightPath);
                File.WriteAllText(scriptPath, scriptBody);
            });

            Task.WaitAll(task1, task2, task3);

            // Start Beyond Compare.
            try {
                var psi = new ProcessStartInfo(beyondComparePath, string.Format("@{0} /silent", scriptPath));
                var process = Process.Start(psi);
                process.WaitForExit();
            }
            catch (Exception ex) {
                Trace.TraceError(
                        $"BeyondCompareDiffBuilder: Failed to start bcompare.exe: {beyondComparePath}");
                return null;
            }

            // Clean up temporary files.
            try {
                File.Delete(leftPath);
                File.Delete(leftPath);
                File.Delete(scriptPath);
            }
            catch (Exception ex) {
                Trace.TraceError($"BeyondCompareDiffBuilder: Failed to delete temp files: {ex.Message}");
            }

            return reportPath;
        }

        private enum Section {
            All,
            Begin,
            Middle,
            End
        }

        private static Section GetRowSection(HtmlNode row) {
            string rowClass = row.GetAttributeValue("class", "");
            switch (rowClass) {
                case "SectionAll":
                    return Section.All;
                case "SectionBegin":
                    return Section.Begin;
                case "SectionMiddle":
                    return Section.Middle;
                case "SectionEnd":
                    return Section.End;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static ChangeType GetDiffChangeType(HtmlNode node) {
            switch (node.InnerText) {
                case "=":
                    return ChangeType.Unchanged;
                case "+-":
                    return ChangeType.Deleted;
                case "-+":
                    return ChangeType.Inserted;
                case "&lt;&gt;":
                    return ChangeType.Modified;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static SideBySideDiffModel GenerateDiffsFromReport(string reportPath) {
            SideBySideDiffModel diffs = new SideBySideDiffModel();
            var doc = new HtmlDocument();
            doc.Load(reportPath);

            HtmlNode table = GetFirst(doc.DocumentNode.Descendants("table"));

            if (table == null) {
                return diffs;
            }

            // Use ChangeType.Imaginary to represent not being in a section.
            const ChangeType ChangeTypeNoSection = ChangeType.Imaginary;
            ChangeType sectionChangeType = ChangeTypeNoSection;
            int oldPosition = 0;
            int newPosition = 0;

            // The html body contains a table with one row per line.
            foreach (HtmlNode row in table.Descendants("tr")) {

                // We expect to see 3 columns. Old, diff type, New.
                var columns = row.Descendants("td");
                HtmlNode oldNode;
                HtmlNode diffNode;
                HtmlNode newNode;

                if (!GetNodes(columns, out oldNode, out diffNode, out newNode)) {
                    return diffs;
                }

                // BC breaks down diffs by sections. If a section is a single row, the row's class is
                // SectionAll, otherwise the class is SectionBegin, SectionMiddle, or SectionEnd.
                // Only the SectionAll and SectionBegin rows have the section type noted (unchanged,
                // insert, delete, modify, etc). 
                Section rowSection = GetRowSection(row);
                Debug.Assert(rowSection != Section.Middle || NodeIsEmpty(diffNode));

                if (rowSection == Section.All || rowSection == Section.Begin) {
                    Debug.Assert(sectionChangeType == ChangeTypeNoSection);
                    sectionChangeType = GetDiffChangeType(diffNode);
                }

                Debug.Assert(sectionChangeType != ChangeTypeNoSection);

                switch (sectionChangeType) {
                    case ChangeType.Unchanged:
                        diffs.OldText.Lines.Add(DiffPieceFromHtmlText(oldNode, ChangeType.Unchanged, ++oldPosition));
                        diffs.NewText.Lines.Add(DiffPieceFromHtmlText(newNode, ChangeType.Unchanged, ++newPosition));
                        break;
                    case ChangeType.Inserted:
                        diffs.OldText.Lines.Add(new DiffPiece());
                        diffs.NewText.Lines.Add(DiffPieceFromHtmlText(newNode, ChangeType.Inserted, ++newPosition));
                        break;
                    case ChangeType.Deleted:
                        diffs.OldText.Lines.Add(DiffPieceFromHtmlText(oldNode, ChangeType.Deleted, ++oldPosition));
                        diffs.NewText.Lines.Add(new DiffPiece());
                        break;
                    case ChangeType.Modified:
                        // Lines added or removed in the middle of a "modified" section show up as blank
                        // nodes on one side or the other. At least one side should be non-empty.
                        Debug.Assert(!NodeIsEmpty(oldNode) || !NodeIsEmpty(newNode));

                        if (NodeIsEmpty(oldNode)) {
                            diffs.OldText.Lines.Add(new DiffPiece());
                            diffs.NewText.Lines.Add(DiffPieceFromModifiedNode(newNode, ChangeType.Inserted, ChangeType.Inserted, ++newPosition));
                        }
                        else if (NodeIsEmpty(newNode)) {
                            diffs.OldText.Lines.Add(DiffPieceFromModifiedNode(oldNode, ChangeType.Deleted, ChangeType.Deleted, ++oldPosition));
                            diffs.NewText.Lines.Add(new DiffPiece());
                        }
                        else {
                            diffs.OldText.Lines.Add(DiffPieceFromModifiedNode(oldNode, ChangeType.Modified, ChangeType.Deleted, ++oldPosition));
                            diffs.NewText.Lines.Add(DiffPieceFromModifiedNode(newNode, ChangeType.Modified, ChangeType.Inserted, ++newPosition));
                        }
                        break;
                    default:
                        Debug.Assert(false);
                        break;
                }

                if (rowSection == Section.All || rowSection == Section.End) {
                    sectionChangeType = ChangeTypeNoSection;
                }
            }

            return diffs;
        }

        private static bool NodeIsEmpty(HtmlNode node) {
            return string.IsNullOrEmpty(node.InnerText) || node.InnerText == "&nbsp;";
        }

        private static DiffPiece DiffPieceFromModifiedNode(HtmlNode node, ChangeType type, ChangeType childChangeType, int position) {
            DiffPiece diffs = new DiffPiece("", type, position);

            int pieceNumber = 0;
            foreach (HtmlNode child in node.ChildNodes) {
                Debug.Assert(child.Name == "#text" || (child.Name == "span" && child.GetAttributeValue("class", "") == "TextSegSigDiff"));

                ChangeType childType = (child.Name == "#text")
                    ? ChangeType.Unchanged
                    : childChangeType;

                DiffPiece subPiece = DiffPieceFromHtmlText(child, childType, ++pieceNumber);
                diffs.SubPieces.Add(subPiece);
                diffs.Text += subPiece.Text;
            }

            return diffs;
        }

        private static DiffPiece DiffPieceFromHtmlText(HtmlNode node, ChangeType type, int position) {
            string text = WebUtility.HtmlDecode(node.InnerText);
            return new DiffPiece(text, type, position);
        }

        private static T GetFirst<T>(IEnumerable<T> collection) {
            var collectionEnum = collection.GetEnumerator();

            if (!collectionEnum.MoveNext()) {
                return default(T);
            }

            return collectionEnum.Current;
        }

        private static bool GetNodes<T>(IEnumerable<T> collection, out T node1, out T node2, out T node3) {
            var collectionEnum = collection.GetEnumerator();

            if (!collectionEnum.MoveNext()) {
                node1 = node2 = node3 = default(T);
                return false;
            }

            node1 = collectionEnum.Current;

            if (!collectionEnum.MoveNext()) {
                node1 = node2 = node3 = default(T);
                return false;
            }

            node2 = collectionEnum.Current;

            if (!collectionEnum.MoveNext()) {
                node1 = node2 = node3 = default(T);
                return false;
            }

            node3 = collectionEnum.Current;
            return true;
        }
    }
}
