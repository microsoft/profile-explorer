using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;
using Core.Lexer;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace IRExplorerExtension
{
    internal class CommandBase
    {
        DTE2 dte_;
        AsyncPackage package_;
        bool initialized_;

        //? TODO: Use timeout everywhere
        static readonly int DefaultDebuggerTimeout = 3000;

        public CommandBase(AsyncPackage package)
        {
            package_ = package;
        }

        async Task Initialize()
        {
            if (!initialized_)
            {
                dte_ = await package_.GetServiceAsync(typeof(DTE)).ConfigureAwait(false) as DTE2;
                initialized_ = true;
            }
        }

        public bool SetupDebugSession()
        {
            Initialize().Wait();
            return ClientInstance.SetupDebugSession();
        }

        public async Task<string> GetCaretDebugExpression()
        {
            await Initialize();

            if (dte_.ActiveDocument == null)
            {
                return null;
            }

            try
            {
                EnvDTE.TextDocument textDocument = (EnvDTE.TextDocument)dte_.ActiveDocument.Object("TextDocument");
                var selection = textDocument.Selection;

                if (selection != null)
                {
                    var activePoint = selection.ActivePoint;

                    var lineOffset = activePoint.LineCharOffset - 1;
                    var text = activePoint.CreateEditPoint().GetLines(activePoint.Line, activePoint.Line + 1);
                    return DebuggerExpression.Create(new TextLineInfo(text, activePoint.Line, lineOffset, lineOffset));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Debugger expression exception: {0}", ex);
            }

            return null;
        }
    }
}
