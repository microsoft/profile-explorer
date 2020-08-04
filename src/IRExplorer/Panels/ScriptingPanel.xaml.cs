// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSScriptLib;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using IRExplorer.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace IRExplorer {
    public static class ScriptingCommand {
        public static readonly RoutedUICommand ExecuteScript =
            new RoutedUICommand("Untitled", "ExecuteScript", typeof(BookmarksPanel));
    }

    public partial class ScriptingPanel : ToolPanelControl {
        private static readonly string InitialScript = string.Join(Environment.NewLine,
                                                                   "using System;",
                                                                   "using System.Collections.Generic;",
                                                                   "using IRExplorerCore;", "using IRExplorerCore.IR;",
                                                                   "using IRExplorerCore.Analysis;",
                                                                   "using IRExplorerCore.UTC;", "using IRExplorer;",
                                                                   "using IRExplorer.Scripting;",
                                                                   "using System.Windows.Media;",
                                                                   "\n",
                                                                   "public class Script {",
                                                                   "    // func: IR function on which the script executes",
                                                                   "    // s: provides script interaction with Compiler Studio (text output, marking, etc.)",
                                                                   "    public bool Execute(FunctionIR func, ScriptSession s) {",
                                                                   "        // Write C#-based script here.",
                                                                   "        return true;",
                                                                   "    }",
                                                                   "}");
        private CompletionWindow completionWindow_;

        private Microsoft.CodeAnalysis.Document roslynDocument;

        public ScriptingPanel() {
            InitializeComponent();
            TextView.TextArea.TextEntered += TextArea_TextEntered;
        }

        private bool SetupAutocomplete() {
            if (roslynDocument != null) {
                return true;
            }

            try {
                var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
                var workspace = new AdhocWorkspace(host);
                var assemblyRefs = new List<PortableExecutableReference>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    try {
                        assemblyRefs.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                    catch (Exception ex) {
                        // Dynamic assemblies don't have a valid location.
                        if (!assembly.IsDynamic) {
                            Trace.TraceWarning($"Failed to setup scripting auto-complete for assembly {assembly.Location}: {ex.Message}");
                        }
                    }
                }

                var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                                                     "Script", "Script", LanguageNames.CSharp)
                                             .WithMetadataReferences(assemblyRefs);
                var project = workspace.AddProject(projectInfo);
                roslynDocument = workspace.AddDocument(project.Id, "DummyFile.cs", SourceText.From(""));
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to setup scripting auto-complete {ex.Message}");
                return false;
            }
        }

        private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e) {
            if (!SetupAutocomplete()) {
                return;
            }

            // https://stackoverflow.com/questions/39422126/whats-the-right-way-to-update-roslyns-document-while-typing
            // https://stackoverflow.com/questions/39421668/whats-the-most-efficient-way-to-use-roslyns-completionsevice-when-typing
            int position = TextView.CaretOffset;
            var sourceText = SourceText.From(TextView.Text);
            var document = roslynDocument.WithText(sourceText);
            var completionService = CompletionService.GetService(document);

            if (e.Text == ".") {
                // Open code completion after the user has pressed dot:
                completionWindow_ = new CompletionWindow(TextView.TextArea);
                var data = completionWindow_.CompletionList.CompletionData;
                var results = await completionService.GetCompletionsAsync(document, position);

                if (results != null) {
                    foreach (var result in results.Items) {
                        if (result.Tags.Contains("Public")) {
                            var description = await completionService.GetDescriptionAsync(document, result);
                            string descriptionText = description != null ? description.Text : "";

                            data.Add(new AutocompleteEntry(GetAutocompleteKind(result), result.DisplayText,
                                                           descriptionText));
                        }
                    }

                    completionWindow_.Show();
                    completionWindow_.Closed += delegate { completionWindow_ = null; };
                }
            }
        }

        private AutocompleteEntryKind GetAutocompleteKind(CompletionItem result) {
            if (result.Tags.Contains("Method")) {
                return AutocompleteEntryKind.Method;
            }
            else if (result.Tags.Contains("Field")) {
                return AutocompleteEntryKind.Field;
            }

            return AutocompleteEntryKind.Property;
        }

        private async void ExecuteScriptExecuted(object sender, ExecutedRoutedEventArgs e) {
            var document = Session.FindAssociatedDocument(this);

            if (document == null) {
                return;
            }

            string userScript = TextView.Text.Trim();
            var scriptSession = new ScriptSession(document, Session);

            try {
                var sw = Stopwatch.StartNew();
                CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
                dynamic script = await Task.Run(() => CSScript.Evaluator.LoadCode(userScript));
                await Task.Run(() => script.Execute(document.Function, scriptSession));
                sw.Stop();

                string outputText = string.Join(Environment.NewLine,
                                                $"Script completed in {sw.ElapsedMilliseconds} ms",
                                                "----------------------------------------\n",
                                                $"{scriptSession.OutputText}");

                OutputTextView.Text = outputText;

                foreach (var pair in scriptSession.MarkedElements) {
                    document.MarkElement(pair.Item1, pair.Item2);
                }
            }
            catch (Exception ex) {
                OutputTextView.Text = $"Failed to run script: {ex}";
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private enum AutocompleteEntryKind {
            Field,
            Method,
            Property
        }

        private class AutocompleteEntry : ICompletionData {
            public AutocompleteEntry(AutocompleteEntryKind kind, string text, string description = "") {
                Text = text;
                Kind = kind;
                Description = description;
            }

            public AutocompleteEntryKind Kind { get; set; }
            public string Text { get; set; }

            public object Content => Text;
            public double Priority => 1;
            public object Description { get; set; }

            public ImageSource Image {
                get {
                    //? TODO: Preload icons in static constructor
                    switch (Kind) {
                        case AutocompleteEntryKind.Field: {
                                return (ImageSource)Application.Current.Resources["TagIcon"];
                            }
                        case AutocompleteEntryKind.Method:
                            return (ImageSource)Application.Current.Resources["RightArrowIcon"];
                        case AutocompleteEntryKind.Property:
                            return (ImageSource)Application.Current.Resources["TagIcon"];
                        default: {
                                return null;
                            }
                    }
                }
            }

            public void Complete(TextArea textArea, ISegment completionSegment,
                                 EventArgs insertionRequestEventArgs) {
                textArea.Document.Replace(completionSegment, Text);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Scripting;

        public override void OnSessionStart() {
            base.OnSessionStart();
            TextView.Text = InitialScript;
        }

        #endregion
    }
}
