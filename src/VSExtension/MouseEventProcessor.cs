using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace IRExplorerExtension {
    public class TextLineInfo {
        public int DocumentOffset;
        public bool Handled;
        public int LineColumn;
        public int LineNumber;
        public string LineText;
        public ModifierKeys PressedModifierKeys;
        public MouseButton PressedMouseButton;

        public TextLineInfo(string lineText, int lineNumber, int lineColumn,
                            int documentOffset) {
            LineText = lineText;
            LineNumber = lineNumber;
            LineColumn = lineColumn;
            DocumentOffset = documentOffset;
        }

        public bool HasTextInfo => LineText != null;

        public override string ToString() {
            return $"{LineNumber}:{LineColumn} ({DocumentOffset}) {LineText}";
        }
    }

    [Export(typeof(IMouseProcessorProvider))]
    [Name("IrxMouseProcessor")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [Order(Before = "VisualStudioMouseProcessor")]
    class MouseProcessorProvider : IMouseProcessorProvider {
        private IVsEditorAdaptersFactoryService editorAdapters_;

        [ImportingConstructor]
        public MouseProcessorProvider(IVsEditorAdaptersFactoryService editorAdapters) {
            editorAdapters_ = editorAdapters;
        }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView) {
            var instance = new MouseEventProcessor(wpfTextView, editorAdapters_);
            IRExplorerExtensionPackage.RegisterMouseProcessor(instance);
            return instance;
        }
    }

    class MouseEventProcessor : MouseProcessorBase {
        public enum ScrollBarConstants {
            SB_HORZ,
            SB_VERT,
            SB_CTL,
            SB_BOTH
        }

        private IWpfTextView view_;
        private IVsTextView viewAdapter_;

        public MouseEventProcessor(IWpfTextView wpfTextView,
                                   IVsEditorAdaptersFactoryService editorAdapters) {
            view_ = wpfTextView;
            viewAdapter_ = editorAdapters.GetViewAdapter(wpfTextView);
        }

        public event EventHandler<TextLineInfo> OnMouseUp;

        public override void PostprocessMouseUp(MouseButtonEventArgs e) {
            if (OnMouseUp != null && ClientInstance.IsConnected) {
                var position = e.GetPosition(view_.VisualElement);
                var lineInfo = GetLineAndPositionInfo(viewAdapter_, view_, position);

                if (lineInfo != null) {
                    lineInfo.PressedMouseButton = e.ChangedButton;
                    lineInfo.PressedModifierKeys = Keyboard.Modifiers;
                    OnMouseUp(this, lineInfo);
                    e.Handled = lineInfo.Handled;
                }
            }

            base.PostprocessMouseUp(e);
        }

        private TextLineInfo GetLineAndPositionInfo(IVsTextView view, IWpfTextView wpfTextView,
                                                    Point wpfClientPoint) {
            var textViewLine =
                wpfTextView.TextViewLines.GetTextViewLineContainingYCoordinate(
                    wpfClientPoint.Y + wpfTextView.ViewportTop);

            if (textViewLine == null) {
                return null;
            }

            double xCoordinate = wpfClientPoint.X + GetPhysicalLeftColumn(view);

            var bufferPositionFromXCoordinate =
                textViewLine.GetBufferPositionFromXCoordinate(xCoordinate);

            if (!bufferPositionFromXCoordinate.HasValue) {
                return new TextLineInfo(null, -1, -1, -1);
            }

            view.GetLineAndColumn(bufferPositionFromXCoordinate.Value.Position,
                                  out int lineNumber, out int lineColumn);

            var textLine =
                bufferPositionFromXCoordinate.Value.Snapshot.GetLineFromLineNumber(lineNumber);

            return new TextLineInfo(textLine.GetText(), lineNumber, lineColumn,
                                    bufferPositionFromXCoordinate.Value.Position);
        }

        public static int GetPhysicalLeftColumn(IVsTextView view) {
            GetScrollInfo(view, ScrollBarConstants.SB_HORZ, out int num, out int num2,
                          out int num3, out int num4);

            return num4;
        }

        private static void GetScrollInfo(IVsTextView view, ScrollBarConstants bar,
                                          out int minUnit, out int maxUnit,
                                          out int visibleLineCount, out int firstVisibleUnit) {
            view.GetScrollInfo((int) bar, out minUnit, out maxUnit, out visibleLineCount,
                               out firstVisibleUnit);
        }
    }
}
