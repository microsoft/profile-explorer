// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Core.IR;
using Core.UTC;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Rendering;

namespace WpfTester {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        List<IRElement> list;
        List<IRElement> oplist;
        private IRHighlighter selectedIR;
        List<Core.IRTextSection> sections;
        UTCReader reader;


        public MainWindow() {
            InitializeComponent();

            var text = File.ReadAllText(@"C:\test\lex.txt");

            list = new List<IRElement>();
            oplist = new List<IRElement>();
            reader = new UTCReader(text);

            sections = reader.FindAllSections();

            if (sections.Count > 0) {
                LoadSection(sections[0]);
            }

            //textEditor.TextArea.TextView.BackgroundRenderers.Add(new TokenHighlighter(tokens_, textEditor));

            selectedIR = new IRHighlighter(null, textEditor);
            textEditor.TextArea.TextView.BackgroundRenderers.Add(new IRHighlighter(list, textEditor));
            textEditor.TextArea.TextView.BackgroundRenderers.Add(new IRHighlighter(oplist, textEditor));
            textEditor.TextArea.TextView.BackgroundRenderers.Add(selectedIR);
            textEditor.MouseUp += TextEditor_MouseDown;


        }

        private void LoadSection(Core.IRTextSection section) {
            list = new List<IRElement>();
            oplist = new List<IRElement>();

            string sectionText = reader.GetSectionText(section);
            UTCParser parser = new UTCParser(sectionText);
            textEditor.Document.Text = sectionText;
            var function = parser.Parse();

            if (function != null) {
                foreach (var block in function.Blocks) {
                    list.Add(block);

                    foreach (var tuple in block.Tuples) {
                        //list.Add(tuple);

                        if (tuple is InstructionIR instr) {
                            foreach (var op in instr.Destinations) {
                                oplist.Add(op);
                            }
                            foreach (var op in instr.Sources) {
                                //oplist.Add(op);
                            }
                        }
                        // Debug.WriteLine(tuple);
                        // Debug.WriteLine("of length {0} at {1}", tuple.TextLength, tuple.TextLocation);

                        // if (list.Count > 0) goto exit;
                    }
                }
            }
        }

        int GetOffsetFromMousePosition(Point positionRelativeToTextView, out int visualColumn) {
            visualColumn = 0;
            TextView textView = textEditor.TextArea.TextView;
            Point pos = positionRelativeToTextView;

            if (pos.Y < 0)
                pos.Y = 0;
            if (pos.Y > textView.ActualHeight)
                pos.Y = textView.ActualHeight;
            pos += textView.ScrollOffset;
            if (pos.Y >= textView.DocumentHeight)
                pos.Y = textView.DocumentHeight - 0.01;
            VisualLine line = textView.GetVisualLineFromVisualTop(pos.Y);

            if (line != null) {
                visualColumn = line.GetVisualColumn(pos, false);
                return line.GetRelativeOffset(visualColumn) + line.FirstDocumentLine.Offset;
            }

            return -1;
        }

        bool FindToken(int offset, out IRElement result, List<IRElement> list) {
            foreach (var token in list) {
                if (offset >= token.TextLocation.Offset &&
                   offset < (token.TextLocation.Offset + token.TextLength)) {
                    result = token;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private void TextEditor_MouseDown(object sender, MouseButtonEventArgs e) {
            DateTime start = DateTime.Now;
            Point position = e.GetPosition(textEditor.TextArea.TextView);
            int column;
            int offset = GetOffsetFromMousePosition(position, out column);

            if (offset != -1) {
                //Debug.WriteLine("Found at offset {0}, column {1}", offset, column);
                IRElement element;
                string text = "";

                if (FindToken(offset, out element, oplist)) {
                    text = element.ToString();

                }
                else if (FindToken(offset, out element, list)) {
                    text = element.ToString();
                }

                if (element is OperandIR op && op.Tags != null) {
                    foreach (var tag in op.Tags) {
                        text = text + "\ntag " + tag.ToString();

                        if (tag is SSADefinitionTag ssaDef) {
                            selectedIR.ClearTokens();

                            foreach (var user in ssaDef.Users) {
                                selectedIR.AddElement(user.Parent);
                            }

                            textEditor.TextArea.TextView.Redraw();
                        }
                    }
                }

                infoText.Document.Blocks.Clear();
                infoText.Document.Blocks.Add(new Paragraph(new Run(text)));

                DateTime end = DateTime.Now;
                Debug.WriteLine("Found in {0}", (end - start).TotalMilliseconds);

                //if(FindToken(offset, out token)) {
                //    Debug.WriteLine(" => {0}", token);
                //    selectedToken_.AddToken(token);
                //    textEditor.TextArea.TextView.Redraw();
                //}
                //else {
                //    Debug.WriteLine(" => no token");
                //    selectedToken_.ClearTokens();
                //    textEditor.TextArea.TextView.Redraw();
                //}
            }
            else {
                DateTime end = DateTime.Now;
                Debug.WriteLine("Not found in {0}", (end - start).TotalMilliseconds);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            sectionBox.ItemsSource = sections;
            sectionBox.DisplayMemberPath = "Name";

            ListCollectionView listCollectionView = new ListCollectionView(sections);
            listCollectionView.Filter = new Predicate<object>(ListFilter);

            sectionList.ItemsSource = listCollectionView;
            sectionList.DisplayMemberPath = "Name";

            listCollectionView.Refresh();

        }

        private bool ListFilter(object value) {
            var section = (Core.IRTextSection)value;
            string text = filterTextbox.Text.Trim();

            if (text.Length > 0) {
                return section.Name.Contains(text);
            }

            return true;
        }

        private void MarkDiffs(DiffPiece line, ICSharpCode.AvalonEdit.TextEditor textEditor, bool invert = false) {
            /*
            string before = File.ReadAllText(@"C:\\work\\before.log");
            string after = File.ReadAllText(@"C:\\work\\after.log");

            var diffBuilder = new SideBySideDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(before, after);

            textEditor.Document.Text = before;
            textEditor2.Document.Text = before;

            foreach (var line in diff.OldText.Lines) {

                MarkDiffs(line, textEditor);
            }

            switch (line.Type) {
                case ChangeType.Deleted: {
                    var docLine = textEditor.Document.GetLineByNumber(Math.Min(textEditor.Document.LineCount -1 , line.Position.Value));
                    //textEditor.Document.Remove(docLine.Offset, docLine.Length);

                    if (invert) {
                        textEditor.Document.Insert(docLine.Offset, new string('=', line.Text.Length) + "\n");
                    }
                    else {
                        textEditor.Document.Replace(docLine.Offset, docLine.Length, new string('.', docLine.Length));
                    }
                    break;
                }
                case ChangeType.Inserted: {
                    var offset = textEditor.Document.GetOffset(line.Position.Value, 0);
                    textEditor.Document.Insert(offset, new string('#', line.Text.Length) + Environment.NewLine);
                    break;
                }
                case ChangeType.Modified: {
                    int column = 0;

                    foreach (var piece in line.SubPieces) {
                        if (piece.Type == ChangeType.Deleted) {
                            var offset = textEditor.Document.GetOffset(line.Position.Value, column);
                            textEditor.Document.Replace(offset, piece.Text.Length, new string('.', piece.Text.Length));
                        }
                        else if (piece.Type == ChangeType.Inserted) {
                            var offset = textEditor.Document.GetOffset(line.Position.Value, column);
                            textEditor.Document.Insert(offset, new string('@', piece.Text.Length));
                        }
                        else if (piece.Type == ChangeType.Modified) {
                            var offset = textEditor.Document.GetOffset(line.Position.Value, column);
                            textEditor.Document.Replace(offset, piece.Text.Length, piece.Text);
                        }

                        column += piece.Text.Length;
                    }


                    break;
                }
                case ChangeType.Imaginary: {
                    //var offset = textEditor.Document.GetOffset(number, line.Position.Value);
                    break;
                }
            }*/
        }

        private void sectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            Debug.WriteLine("Selected " + e.ToString());

            LoadSection((Core.IRTextSection)e.AddedItems[0]);
        }

        private void sectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {

        }

        private void filterTextbox_TextChanged(object sender, TextChangedEventArgs e) {
            ((ListCollectionView)sectionList.ItemsSource).Refresh();
        }
    }

}
