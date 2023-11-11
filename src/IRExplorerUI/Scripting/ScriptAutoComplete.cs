// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace IRExplorerUI.Scripting {
    public enum AutocompleteEntryKind {
        Field,
        Method,
        Property,
        Symbol,
        Class,
        Keyword
    }

    public class AutocompleteEntry : ICompletionData {
        public AutocompleteEntry(AutocompleteEntryKind kind, bool isPreferred, string text,
                                 CompletionItem completionItem) {
            Kind = kind;
            IsPreferred = isPreferred;
            Text = text;
            CompletionItem = completionItem;
        }

        private static Dictionary<AutocompleteEntryKind, ImageSource> icons_;

        static AutocompleteEntry() {
            icons_ = new Dictionary<AutocompleteEntryKind, ImageSource>();
            icons_[AutocompleteEntryKind.Field] = (ImageSource)Application.Current.Resources["TagIcon"];
            icons_[AutocompleteEntryKind.Method] = (ImageSource)Application.Current.Resources["ZapIcon"];
            icons_[AutocompleteEntryKind.Property] = (ImageSource)Application.Current.Resources["TagIcon"];
            icons_[AutocompleteEntryKind.Symbol] = (ImageSource)Application.Current.Resources["WholeWordIcon"];
            icons_[AutocompleteEntryKind.Class] = (ImageSource)Application.Current.Resources["DocumentIcon"];
            icons_[AutocompleteEntryKind.Keyword] = (ImageSource)Application.Current.Resources["DotIcon"];
        }

        public AutocompleteEntryKind Kind { get; set; }
        public bool IsPreferred { get; set; }
        public string Text { get; set; }
        public string DescriptionText { get; set; }
        public CompletionItem CompletionItem { get; set; }

        public object Content => Text;
        public double Priority => 1;
        public object Description { get; set; }

        public ImageSource Image => icons_[Kind];

        public void Complete(TextArea textArea, ISegment completionSegment,
                             EventArgs insertionRequestEventArgs) {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    public class ScriptAutoComplete {
        private static long initialized_;
        private static object lockObject_;
        private Microsoft.CodeAnalysis.Document roslynDocument_;

        static ScriptAutoComplete() {
            initialized_ = 0;
            lockObject_ = new object();
        }

        public ScriptAutoComplete() {

        }

        public static bool WarmUp() {
            if (Interlocked.Read(ref initialized_) != 0) {
                return true;
            }

            lock (lockObject_) {
                if (Interlocked.Read(ref initialized_) != 0) {
                    return true;
                }

                var autoComplete = new ScriptAutoComplete();
                bool result = autoComplete.SetupAutocomplete(fromWarmUp: true);
                Interlocked.Exchange(ref initialized_, 1);
                return result;
            }
        }

        public async Task<List<Diagnostic>> GetSourceErrorsAsync(string text) {
            var diagnostics = new List<Diagnostic>();

            if (!SetupAutocomplete()) {
                return diagnostics;
            }

            var sourceText = SourceText.From(text);
            var document = roslynDocument_.WithText(sourceText);
            var model = await document.GetSemanticModelAsync().ConfigureAwait(false);
            var results = model.GetDiagnostics();

            if (results != null) {
                foreach (var result in results) {
                    if (result.Severity == DiagnosticSeverity.Error) {
                        diagnostics.Add(result);
                    }
                }
            }

            return diagnostics;
        }

        public async Task<List<AutocompleteEntry>>
        GetSuggestionsAsync(string text, int position, string changedText = "") {
            var suggestionList = new List<AutocompleteEntry>();

            if (!IsAutocompleteTrigger(text, position - 1)) {
                return suggestionList;
            }

            // If inserted text is whitespace, ignore.
            if (!string.IsNullOrEmpty(changedText) && string.IsNullOrWhiteSpace(changedText)) {
                return suggestionList;
            }

            if (!SetupAutocomplete()) {
                return suggestionList; // Failed to initialize Roslyn.
            }

            // Get the initial list of suggestions from Roslyn. This list is usually 
            // very large and is filtered and sorted below.
            var sourceText = SourceText.From(text);
            var document = roslynDocument_.WithText(sourceText);
            var completionService = CompletionService.GetService(document);
            var results = await completionService.
                GetCompletionsAsync(document, position).ConfigureAwait(false);

            if (results == null) {
                return suggestionList;
            }

            if (changedText == ".") {
                // Class member list, doesn't need extra filtering.
                foreach (var item in results.Items) {
                    if (item.Tags.Contains("Public")) {
                        bool preselect = preselect = item.Rules.MatchPriority == MatchPriority.Preselect;
                        suggestionList.Add(new AutocompleteEntry(AutocompleteEntryKind.Symbol,
                                                                 preselect, item.DisplayText, item));
                    }
                }

                await AddDescription(suggestionList, completionService, document).ConfigureAwait(false);
                return suggestionList;
            }
            else {
                var word = GetCurrentWord(text, position - 1);

                if (!IsValidWord(word) || word.Length < 2) {
                    // When using backspace, keep showing the autocomplete box for a single letter.
                    if (!(word.Length == 1 && char.IsLetter(word[0]) &&
                        string.IsNullOrEmpty(changedText))) {
                        return suggestionList;
                    }
                }

                // Variable/function/keyword list, needs filtering to trim down.
                CompletionItem exactMatchItem = null;
                int substringMatchItems = 0;

                foreach (var item in results.Items) {
                    bool preselect = preselect = item.Rules.MatchPriority == MatchPriority.Preselect;
                    string displayText = item.DisplayText;
                    string completionText = displayText;

                    if (displayText.IsValidCompletionFor(word)) {
                        if (!item.UseDisplayTextAsCompletionText() &&
                             displayText.StartsWith(word, StringComparison.OrdinalIgnoreCase)) {
                            // Check for complete and prefix match of the current word.
                            substringMatchItems++;

                            if (displayText == word) {
                                exactMatchItem = item;
                            }
                        }

                        suggestionList.Add(new AutocompleteEntry(AutocompleteEntryKind.Symbol,
                                                                 preselect, completionText, item));
                    }
                }

                if (suggestionList.Count == 0) {
                    return suggestionList;
                }

                // If there's an exact match and no other items having it as a prefix,
                // don't return any suggestions.
                if (exactMatchItem != null && substringMatchItems == 1) {
                    suggestionList.Clear();
                    return suggestionList;
                }

                // Sort the suggestions by best match, then take the top N.
                // The description is retrieved only for those, since it is fairly slow.
                var sortedData = suggestionList.OrderByDescending(c => c.IsPreferred).
                                ThenByDescending(c => c.Text.StartsWithExactCase(word)).
                                ThenByDescending(c => c.Text.StartsWithIgnoreCase(word)).
                                ThenByDescending(c => c.Text.IsSubsequenceMatch(word)).
                                ThenBy(c => c.Text, StringComparer.OrdinalIgnoreCase).Take(20);

                await AddDescription(sortedData, completionService, document).ConfigureAwait(false);

                // The description includes the namespace, sort again so that
                // IR Explorer API entries are placed  closer to the front.
                sortedData = sortedData.OrderByDescending(c => c.IsPreferred).
                             ThenByDescending(c => c.Text.StartsWithExactCase(word)).
                             ThenByDescending(c => c.DescriptionText.Contains("IRExplorer",
                                                                              StringComparison.Ordinal));
                return sortedData.ToList();
            }
        }

        private async Task AddDescription(IEnumerable<AutocompleteEntry> sortedData,
                                          CompletionService completionService,
                                          Microsoft.CodeAnalysis.Document document) {
            foreach (var item in sortedData) {
                var description = await completionService.
                    GetDescriptionAsync(document, item.CompletionItem).ConfigureAwait(false);
                var descriptionText = description != null ? description.Text : "";
                item.Description = descriptionText;
                item.DescriptionText = descriptionText;
                item.Kind = GetAutocompleteKind(item.CompletionItem);
            }
        }

        public static string GetCurrentWord(string text, int position) {
            // Extend left as long the characters are valid identifiers letters.
            int endPosition = position;

            while (position > 0) {
                if (char.IsLetterOrDigit(text[position]) || text[position] == '_') {
                    position--;
                }
                else {
                    position++;
                    break;
                }
            }

            return text.Substring(position, endPosition - position + 1);
        }

        private bool SetupAutocomplete(bool fromWarmUp = false) {
            if (!fromWarmUp && !WarmUp()) {
                return false;
            }

            if (roslynDocument_ != null) {
                return true;
            }

            try {
                // Load the binaries currently referenced by IR Explorer,
                // this will include the entire API and all .NET Core libraries.
                var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
                var workspace = new AdhocWorkspace(host);
                var assemblyRefs = new List<PortableExecutableReference>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    try {
                        assemblyRefs.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                    catch (Exception ex) {
                        // Dynamic assemblies don't have a valid location, ignore.
                        if (!assembly.IsDynamic) {
                            Trace.TraceWarning($"Failed to setup scripting auto-complete for assembly {assembly.Location}: {ex.Message}");
                        }
                    }
                }

                var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                                                     "Script", "Script", LanguageNames.CSharp)
                                             .WithMetadataReferences(assemblyRefs);
                var project = workspace.AddProject(projectInfo);
                roslynDocument_ = workspace.AddDocument(project.Id, "script.cs", SourceText.From(""));
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to setup scripting auto-complete {ex.Message}");
                return false;
            }
        }

        private bool IsValidWord(string word) {
            foreach (var letter in word) {
                if (!char.IsLetterOrDigit(letter)) {
                    return false;
                }
            }

            return true;
        }

        private bool IsAutocompleteTrigger(string text, int position) {
            if (text[position] == '.') {
                return true;
            }

            if (char.IsLetter(text[position])) {
                return true;
            }
            else if (char.IsDigit(text[position])) {
                // Accept a digit only if it's part of an identifier.
                bool foundLetter = false;

                for (int i = position - 1; i >= 0; i--) {
                    if (char.IsLetter(text[i])) {
                        foundLetter = true;
                    }
                    else if (!char.IsDigit(text[i]) || (text[i] == '_')) {
                        break;
                    }
                }

                return foundLetter;
            }


            return false;
        }

        private AutocompleteEntryKind GetAutocompleteKind(CompletionItem result) {
            if (result.Tags.Contains("Method")) {
                return AutocompleteEntryKind.Method;
            }
            else if (result.Tags.Contains("Field")) {
                return AutocompleteEntryKind.Field;
            }
            else if (result.Tags.Contains("Class")) {
                return AutocompleteEntryKind.Class;
            }
            else if (result.Tags.Contains("Struct")) {
                return AutocompleteEntryKind.Class;
            }
            else if (result.Tags.Contains("Keyword")) {
                return AutocompleteEntryKind.Keyword;
            }

            return AutocompleteEntryKind.Property;
        }
    }

    static class StringExtensions {
        private const string NamedParameterCompletionProvider = "Microsoft.CodeAnalysis.CSharp.Completion.Providers.NamedParameterCompletionProvider";
        private const string OverrideCompletionProvider = "Microsoft.CodeAnalysis.CSharp.Completion.Providers.OverrideCompletionProvider";
        private static readonly PropertyInfo getProviderName_;

        static StringExtensions() {
            getProviderName_ = typeof(CompletionItem).GetProperty("ProviderName", BindingFlags.NonPublic |
                                                                                  BindingFlags.Instance);
        }

        public static bool UseDisplayTextAsCompletionText(this CompletionItem completionItem) {
            var provider = GetProviderName(completionItem);
            return provider == NamedParameterCompletionProvider || provider == OverrideCompletionProvider;
        }

        public static bool IsValidCompletionFor(this string completion, string partial) {
            return completion.StartsWithIgnoreCase(partial) || completion.IsSubsequenceMatch(partial);
        }

        public static bool StartsWithExactCase(this string completion, string partial) {
            return completion.StartsWith(partial, StringComparison.Ordinal);
        }

        public static bool StartsWithIgnoreCase(this string completion, string partial) {
            return completion.StartsWith(partial, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCamelCaseMatch(this string completion, string partial) {
            return new string(completion.Where(c => c >= 'A' && c <= 'Z').ToArray()).
                              StartsWith(partial, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSubsequenceMatch(this string completion, string partial) {
            if (string.IsNullOrEmpty(partial)) {
                return true;
            }

            if (partial.Length > 1 &&
                completion.IndexOf(partial, StringComparison.InvariantCultureIgnoreCase) >= 0) {
                return true;
            }

            return ComputeEditingDistance(completion, partial, 2) <= 1;
        }

        private static bool FirstLetterMatches(string word, string match) {
            if (string.IsNullOrEmpty(match)) {
                return false;
            }

            return char.ToLowerInvariant(word[0]) == char.ToLowerInvariant(match[0]);
        }

        private static string GetProviderName(CompletionItem item) {
            return (string)getProviderName_.GetValue(item);
        }

        private static int ComputeEditingDistance(string source, string target, int threshold) {
            // Damerau-Levenshtein editing distance algorithm based on https://stackoverflow.com/a/9454016
            int length1 = source.Length;
            int length2 = target.Length;

            // Return trivial case - difference in string lengths exceeds threshold.
            if (Math.Abs(length1 - length2) > threshold) { return int.MaxValue; }

            // Ensure arrays [i] / length1 use shorter length 
            if (length1 > length2) {
                Swap(ref target, ref source);
                Swap(ref length1, ref length2);
            }

            int maxi = length1;
            int maxj = length2;

            int[] dCurrent = new int[maxi + 1];
            int[] dMinus1 = new int[maxi + 1];
            int[] dMinus2 = new int[maxi + 1];
            int[] dSwap;

            for (int i = 0; i <= maxi; i++) { dCurrent[i] = i; }

            int jm1 = 0, im1 = 0, im2 = -1;

            for (int j = 1; j <= maxj; j++) {

                // Rotate
                dSwap = dMinus2;
                dMinus2 = dMinus1;
                dMinus1 = dCurrent;
                dCurrent = dSwap;

                // Initialize
                int minDistance = int.MaxValue;
                dCurrent[0] = j;
                im1 = 0;
                im2 = -1;

                for (int i = 1; i <= maxi; i++) {

                    int cost = source[im1] == target[jm1] ? 0 : 1;

                    int del = dCurrent[im1] + 1;
                    int ins = dMinus1[i] + 1;
                    int sub = dMinus1[im1] + cost;

                    //Fastest execution for min value of 3 integers
                    int min = (del > ins) ? (ins > sub ? sub : ins) : (del > sub ? sub : del);

                    if (i > 1 && j > 1 && source[im2] == target[jm1] && source[im1] == target[j - 2])
                        min = Math.Min(min, dMinus2[im2] + cost);

                    dCurrent[i] = min;
                    if (min < minDistance) { minDistance = min; }
                    im1++;
                    im2++;
                }
                jm1++;
                if (minDistance > threshold) { return int.MaxValue; }
            }

            int result = dCurrent[maxi];
            return (result > threshold) ? int.MaxValue : result;
        }

        static void Swap<T>(ref T arg1, ref T arg2) {
            T temp = arg1;
            arg1 = arg2;
            arg2 = temp;
        }
    }
}
