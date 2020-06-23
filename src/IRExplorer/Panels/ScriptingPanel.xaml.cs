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
using Client.Scripting;
using CSScriptLib;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Client {
    public static class ScriptingCommand {
        public static readonly RoutedUICommand ExecuteScript =
            new RoutedUICommand("Untitled", "ExecuteScript", typeof(BookmarksPanel));
    }

    public partial class ScriptingPanel : ToolPanelControl {
        private static readonly string InitialScript = string.Join(Environment.NewLine,
                                                                   "using Core;", "using Core.IR;",
                                                                   "using Core.UTC;", "using Client;",
                                                                   "using System.Windows.Media;",
                                                                   "\n// func: IR function on which the script executes",
                                                                   "// session: provides script interaction with Compiler Studio (text output, marking, etc.)",
                                                                   "\nvoid Execute(FunctionIR func, ScriptSession session) {",
                                                                   "    // Write C#-based script here.", "}");
        private CompletionWindow completionWindow_;

        private Microsoft.CodeAnalysis.Document roslynDocument;

        public ScriptingPanel() {
            InitializeComponent();
            TextView.TextArea.TextEntered += TextArea_TextEntered;
        }

        private void SetupAutocomplete() {
            var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
            var workspace = new AdhocWorkspace(host);
            var assemblyRefs = new List<PortableExecutableReference>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    assemblyRefs.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
                catch (Exception ex) {
                    return;
                }
            }

            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                                                 "Script", "Script", LanguageNames.CSharp)
                                         .WithMetadataReferences(assemblyRefs);

            var project = workspace.AddProject(projectInfo);
            roslynDocument = workspace.AddDocument(project.Id, "DummyFile.cs", SourceText.From(""));
        }

        private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e) {
            if (roslynDocument == null) {
                SetupAutocomplete();
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

        /*
         Adding assembly System.Private.CoreLib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e
Adding assembly compstudio, Version=0.3.5.0, Culture=neutral, PublicKeyToken=null
Adding assembly PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Runtime, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly System.IO.Packaging, Version=4.0.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly netstandard, Version=2.1.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Private.Uri, Version=4.0.6.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly DirectWriteForwarder, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Runtime.Extensions, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.InteropServices, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.CompilerServices.VisualC, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.Debug, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Microsoft.Win32.Primitives, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Collections.NonGeneric, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Linq, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Collections, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Microsoft.Win32.Registry, Version=4.1.3.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.TraceSource, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Collections.Specialized, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.ComponentModel.Primitives, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.Process, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading.Thread, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Configuration.ConfigurationManager, Version=4.0.3.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Xml.ReaderWriter, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Private.Xml, Version=4.0.2.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.IO.FileSystem, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Net.WebClient, Version=4.0.2.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Memory, Version=4.2.1.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Security.Cryptography.Algorithms, Version=4.3.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Text.Encoding.Extensions, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading.Tasks, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading.ThreadPool, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Resources.ResourceManager, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.ComponentModel.TypeConverter, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Windows.Extensions, Version=4.0.1.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.ComponentModel, Version=4.0.4.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Collections.Concurrent, Version=4.0.15.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.ObjectModel, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Net.Requests, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Net.Primitives, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Security.Principal, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Net.WebHeaderCollection, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
Adding assembly UIAutomationTypes, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly Xceed.Wpf.AvalonDock, Version=3.6.0.0, Culture=neutral, PublicKeyToken=3e4669d2f30244f4
Adding assembly PresentationFramework.Aero2, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly UIAutomationProvider, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly WindowsFormsIntegration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Windows.Controls.Ribbon, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly System.Buffers, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Drawing.Primitives, Version=4.2.1.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly ICSharpCode.AvalonEdit, Version=6.0.0.0, Culture=neutral, PublicKeyToken=9cc39be672370310
Adding assembly PresentationFramework-SystemXml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly System.Text.RegularExpressions, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Xceed.Wpf.AvalonDock.Themes.VS2013, Version=3.5.13.0, Culture=neutral, PublicKeyToken=3e4669d2f30244f4
Adding assembly protobuf-net, Version=3.0.0.0, Culture=neutral, PublicKeyToken=257b51d87d2e4d67
Adding assembly protobuf-net.Core, Version=3.0.0.0, Culture=neutral, PublicKeyToken=257b51d87d2e4d67
Adding assembly System.Reflection.Emit, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Reflection.Emit.Lightweight, Version=4.1.1.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Reflection.Emit.ILGeneration, Version=4.1.1.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Reflection.Primitives, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Exception thrown: 'System.NotSupportedException' in System.Private.CoreLib.dll
Adding assembly System.Collections.Immutable, Version=1.2.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.Tracing, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Xml.XmlSerializer, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Exception thrown: 'System.NotSupportedException' in System.Private.CoreLib.dll
Adding assembly AutoUpdater.NET, Version=1.5.8.0, Culture=neutral, PublicKeyToken=501435c91b35f4bc
Adding assembly System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.StackTrace, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Microsoft.VisualStudio.DesignTools.WpfTap, Version=16.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.IO.Pipes, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading.Timer, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.Serialization.Json, Version=4.0.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Private.DataContractSerialization, Version=4.1.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.Serialization.Xml, Version=4.1.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.Serialization.Primitives, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.Serialization.Formatters, Version=4.0.4.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly System.Net.ServicePoint, Version=4.0.2.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Net.Security, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly System.ComponentModel.EventBasedAsync, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Xml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.IO.FileSystem.Watcher, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Threading.Overlapped, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Windows.UI, Version=255.255.255.255, Culture=neutral, PublicKeyToken=null, ContentType=WindowsRuntime
Adding assembly Windows.Foundation, Version=255.255.255.255, Culture=neutral, PublicKeyToken=null, ContentType=WindowsRuntime
Adding assembly System.Runtime.InteropServices.WindowsRuntime, Version=4.0.4.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Runtime.WindowsRuntime, Version=4.0.15.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
Adding assembly Microsoft.CodeAnalysis.Features, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly Microsoft.CodeAnalysis, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly Microsoft.CodeAnalysis.Workspaces, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Text.Encoding, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Reflection, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Microsoft.CodeAnalysis.CSharp.Workspaces, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly Microsoft.CodeAnalysis.CSharp.Features, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Composition.TypedParts, Version=1.0.31.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Composition.Hosting, Version=1.0.31.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Composition.Runtime, Version=1.0.31.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Composition.AttributedModel, Version=1.0.31.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Globalization, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.IO, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Threading.Tasks.Extensions, Version=4.3.1.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
Adding assembly System.Reflection.Metadata, Version=1.4.5.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Reflection.Extensions, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly System.Diagnostics.Tools, Version=4.1.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Adding assembly Microsoft.CodeAnalysis.CSharp, Version=2.10.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35
Adding assembly System.Linq.Expressions, Version=4.2.2.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
         */

        private async void ExecuteScriptExecuted(object sender, ExecutedRoutedEventArgs e) {
            var document = Session.FindAssociatedDocument(this);

            if (document == null) {
                return;
            }

            string userScript = TextView.Text.Trim();
            var scriptSession = new ScriptSession(document);

            try {
                var sw = Stopwatch.StartNew();
                CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
                var script = await Task.Run(() => CSScript.Evaluator.CreateDelegate(userScript));
                await Task.Run(() => script(document.Function, scriptSession));
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
                            return (ImageSource) Application.Current.Resources["TagIcon"];
                        }
                        case AutocompleteEntryKind.Method:
                            return (ImageSource) Application.Current.Resources["RightArrowIcon"];
                        case AutocompleteEntryKind.Property:
                            return (ImageSource) Application.Current.Resources["TagIcon"];
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
