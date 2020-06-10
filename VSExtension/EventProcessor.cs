using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text;
using System.Windows;

namespace IRExplorerExtension
{
    public class TextLineInfo
    {
        public string LineText;
        public int LineNumber;
        public int LineColumn;
        public int DocumentOffset;
        public MouseButton PressedMouseButton;
        public ModifierKeys PressedModifierKeys;
        public bool Handled;

        public bool HasTextInfo => LineText != null;

        public TextLineInfo(string lineText, int lineNumber, int lineColumn, int documentOffset)
        {
            LineText = lineText;
            LineNumber = lineNumber;
            LineColumn = lineColumn;
            DocumentOffset = documentOffset;
        }

        public override string ToString()
        {
            return $"{LineNumber}:{LineColumn} ({DocumentOffset}) {LineText}";
        }
    }

    [Export(typeof(IMouseProcessorProvider))]
    [Name("IrxMouseProcessor")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [Order(Before = "VisualStudioMouseProcessor")]
    class MouseProcessorProvider : IMouseProcessorProvider
    {
        IVsEditorAdaptersFactoryService editorAdapters_;

        [ImportingConstructor]
        public MouseProcessorProvider(
            IVsEditorAdaptersFactoryService editorAdapters)
        {
            editorAdapters_ = editorAdapters;
        }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            var instance = new EventProcessor(wpfTextView, editorAdapters_);
            IRExplorerExtensionPackage.RegisterMouseProcessor(instance);
            return instance;
        }
    }

    class EventProcessor : MouseProcessorBase
    {
        IWpfTextView view_;
        IVsTextView viewAdapter_;

        public EventProcessor(IWpfTextView wpfTextView, IVsEditorAdaptersFactoryService editorAdapters)
        {
            view_ = wpfTextView;
            viewAdapter_ = editorAdapters.GetViewAdapter(wpfTextView);

            wpfTextView.Caret.PositionChanged += Caret_PositionChanged;
        }

        private void Caret_PositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            
        }

        public event EventHandler<TextLineInfo> OnMouseUp;
        public event EventHandler<TextLineInfo> OnMouseMove;

        public override void PostprocessMouseUp(MouseButtonEventArgs e)
        {
            if (OnMouseUp != null)
            {
                var position = e.GetPosition(view_.VisualElement);
                var lineInfo = GetLineAndPositionInfo(viewAdapter_, view_, position);

                if (lineInfo != null)
                {
                    lineInfo.PressedMouseButton = e.ChangedButton;
                    lineInfo.PressedModifierKeys = Keyboard.Modifiers;
                    
                    OnMouseUp(this, lineInfo);
                    e.Handled = lineInfo.Handled;
                }
            }

            base.PostprocessMouseUp(e);
        }

        public override void PreprocessMouseMove(MouseEventArgs e)
        {
            if (OnMouseMove != null)
            {
                var position = e.GetPosition(view_.VisualElement);
                var lineInfo = GetLineAndPositionInfo(viewAdapter_, view_, position);

                if (lineInfo != null)
                {
                    lineInfo.PressedModifierKeys = Keyboard.Modifiers;

                    OnMouseMove(this, lineInfo);
                    e.Handled = lineInfo.Handled;
                }
            }

            base.PreprocessMouseMove(e);
        }

        TextLineInfo GetLineAndPositionInfo(IVsTextView view, IWpfTextView wpfTextView, Point wpfClientPoint) {
            ITextViewLine textViewLine =
                wpfTextView.TextViewLines.GetTextViewLineContainingYCoordinate(wpfClientPoint.Y + wpfTextView.ViewportTop);
            
            if (textViewLine == null) {
                return null;
            }

            double xCoordinate = wpfClientPoint.X + GetPhysicalLeftColumn(view);

            SnapshotPoint? bufferPositionFromXCoordinate =
                textViewLine.GetBufferPositionFromXCoordinate(xCoordinate);

            if (!bufferPositionFromXCoordinate.HasValue) {
                return new TextLineInfo(null, -1, -1, -1);
            }

            int lineNumber;
            int lineColumn;
            view.GetLineAndColumn(bufferPositionFromXCoordinate.Value.Position, out lineNumber, out lineColumn);
            var textLine = bufferPositionFromXCoordinate.Value.Snapshot.GetLineFromLineNumber(lineNumber);

            return new TextLineInfo(textLine.GetText(), lineNumber, lineColumn, 
                                bufferPositionFromXCoordinate.Value.Position);
        }

        public enum ScrollBarConstants { SB_HORZ, SB_VERT, SB_CTL, SB_BOTH }

        public static int GetPhysicalLeftColumn(IVsTextView view)
        {
            GetScrollInfo(view, ScrollBarConstants.SB_HORZ, out int num, out int num2, out int num3, out int num4);
            return num4;
        }

        private static void GetScrollInfo(IVsTextView view, ScrollBarConstants bar,
            out int minUnit, out int maxUnit, out int visibleLineCount, out int firstVisibleUnit)
        {
            view.GetScrollInfo((int)bar, out minUnit, out maxUnit, out visibleLineCount, out firstVisibleUnit);
        }
    }
}
