// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Input;

namespace IRExplorer {
    public static class DocumentCommand {
        public static readonly RoutedUICommand GoToDefinition =
            new RoutedUICommand("Untitled", "GoToDefinition", typeof(IRDocumentHost));
        public static readonly RoutedUICommand GoToDefinitionSkipCopies =
            new RoutedUICommand("Untitled", "GoToDefinitionSkipCopies", typeof(IRDocumentHost));

        public static readonly RoutedUICommand MarkDefinition =
            new RoutedUICommand("Untitled", "MarkDefinition", typeof(IRDocumentHost));
        public static readonly RoutedUICommand MarkDefinitionBlock =
            new RoutedUICommand("Untitled", "MarkDefinitionBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand Mark =
            new RoutedUICommand("Untitled", "Mark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowUses =
            new RoutedUICommand("Untitled", "ShowUses", typeof(IRDocumentHost));
        public static readonly RoutedUICommand MarkUses =
            new RoutedUICommand("Untitled", "MarkUses", typeof(IRDocumentHost));
        public static readonly RoutedUICommand MarkBlock =
            new RoutedUICommand("Untitled", "MarkBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand MarkReferences =
            new RoutedUICommand("Untitled", "MarkReferences", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowExpressionGraph =
            new RoutedUICommand("Untitled", "ShowExpressionGraph", typeof(IRDocumentHost));

        public static readonly RoutedUICommand NextBlock =
            new RoutedUICommand("Untitled", "NextBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand PreviousBlock =
            new RoutedUICommand("Untitled", "PreviousBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowReferences =
            new RoutedUICommand("Untitled", "ShowReferences", typeof(IRDocumentHost));

        public static readonly RoutedUICommand AddBookmark =
            new RoutedUICommand("Untitled", "AddBookmark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand RemoveBookmark =
            new RoutedUICommand("Untitled", "RemoveBookmark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand RemoveAllBookmarks =
            new RoutedUICommand("Untitled", "RemoveAllBookmarks", typeof(IRDocumentHost));
        public static readonly RoutedUICommand PreviousBookmark =
            new RoutedUICommand("Untitled", "PreviousBookmark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand NextBookmark =
            new RoutedUICommand("Untitled", "NextBookmark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowBookmarks =
            new RoutedUICommand("Untitled", "ShowBookmarks", typeof(IRDocumentHost));
        public static readonly RoutedUICommand FirstBookmark =
            new RoutedUICommand("Untitled", "FirstBookmark", typeof(IRDocumentHost));
        public static readonly RoutedUICommand LastBookmark =
            new RoutedUICommand("Untitled", "LastBookmark", typeof(IRDocumentHost));

        public static readonly RoutedUICommand FocusBlockSelector =
            new RoutedUICommand("Untitled", "FocusBlockSelector", typeof(IRDocumentHost));

        public static readonly RoutedUICommand ClearMarker =
            new RoutedUICommand("Untitled", "ClearMarker", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ClearBlockMarkers =
            new RoutedUICommand("Untitled", "ClearBlockMarkers", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ClearInstructionMarkers =
            new RoutedUICommand("Untitled", "ClearInstructionMarkers", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ClearAllMarkers =
            new RoutedUICommand("Untitled", "ClearAllMarkers", typeof(IRDocumentHost));

        public static readonly RoutedUICommand UndoAction =
            new RoutedUICommand("Untitled", "UndoAction", typeof(IRDocumentHost));
    }
}
