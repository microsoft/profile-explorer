using System;
using System.Diagnostics;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace IRExplorerExtension {
    class CommandBase {
        //? TODO: Use timeout everywhere
        private DTE2 dte_;
        private bool initialized_;
        private AsyncPackage package_;

        public CommandBase(AsyncPackage package) {
            package_ = package;
        }

        private async Task Initialize() {
            if (!initialized_) {
                dte_ =
                    await package_.GetServiceAsync(typeof(DTE)).ConfigureAwait(false) as DTE2;

                initialized_ = true;
            }
        }

        public bool SetupDebugSession() {
            Initialize().Wait();
            return ClientInstance.SetupDebugSession();
        }

        public async Task<string> GetCaretDebugExpression() {
            await Initialize();

            if (dte_.ActiveDocument == null) {
                return null;
            }

            try {
                var textDocument = (TextDocument) dte_.ActiveDocument.Object("TextDocument");
                var selection = textDocument.Selection;

                if (selection != null) {
                    var activePoint = selection.ActivePoint;
                    int lineOffset = activePoint.LineCharOffset - 1;

                    string text = activePoint
                                  .CreateEditPoint()
                                  .GetLines(activePoint.Line, activePoint.Line + 1);

                    return DebuggerExpression.Create(
                        new TextLineInfo(text, activePoint.Line, lineOffset, lineOffset));
                }
            }
            catch (Exception ex) {
                Debug.WriteLine("Debugger expression exception: {0}", ex);
            }

            return null;
        }
    }
}
