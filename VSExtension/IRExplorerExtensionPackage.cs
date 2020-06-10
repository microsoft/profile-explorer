using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;
using System.IO;
using System.Diagnostics;
using EnvDTE90;

namespace IRExplorerExtension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(IRExplorerExtensionPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    //[ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    public sealed class IRExplorerExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// IRExplorerExtensionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "c29cc20e-a6f8-412f-a266-3adc6f822594";
        public static IRExplorerExtensionPackage Instance;

        DTE2 dte_;
        EnvDTE.Debugger debugger_;
        EnvDTE.DebuggerEvents debuggerEvents_;
        IVsStatusbar statusBar_;


        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await AttachCommand.InitializeAsync(this);
            await MarkElementCommand.InitializeAsync(this);
            await MarkUsesCommand.InitializeAsync(this);
            await MarkExpressionCommand.InitializeAsync(this);
            await MarkReferencesCommand.InitializeAsync(this);
            await ShowReferencesCommand.InitializeAsync(this);
            await ShowExpressionGraphCommand.InitializeAsync(this);
            await EnableCommand.InitializeAsync(this);
            await UpdateIRCommand.InitializeAsync(this);

            // https://stackoverflow.com/questions/22570121/debuggerevents-onenterbreakmode-is-not-triggered-in-the-visual-studio-extension
            dte_ = (await GetServiceAsync(typeof(DTE))) as DTE2;
            statusBar_ = (await GetServiceAsync(typeof(SVsStatusbar))) as IVsStatusbar;

            debugger_ = dte_.Debugger;
            debuggerEvents_ = dte_.Events.DebuggerEvents;
            debuggerEvents_ .OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;
            debuggerEvents_.OnEnterRunMode += DebuggerEvents__OnEnterRunMode;
            debuggerEvents_.OnEnterDesignMode += DebuggerEvents_OnEnterDesignMode;
            debuggerEvents_.OnExceptionNotHandled += DebuggerEvents_OnExceptionNotHandled;

            Logger.Initialize(this, "IR Explorer");
            ClientInstance.Initialize(this);
            DebuggerInstance.Initialize(debugger_);
        }

        internal static void RegisterMouseProcessor(EventProcessor instance)
        {
            instance.OnMouseUp += MouseProcessor_OnMouseUp;
        }

        private static async void MouseProcessor_OnMouseUp(object sender, TextLineInfo e)
        {
            if(!ClientInstance.IsConnected)
            {
                return;
            }

            bool handled = false;

            if (e.HasTextInfo)
            {
                var expression = DebuggerExpression.Create(e);

                if (e.PressedMouseButton == System.Windows.Input.MouseButton.Left)
                {
                    if (e.PressedModifierKeys.HasFlag(System.Windows.Input.ModifierKeys.Alt))
                    {
                        bool result = await MarkElementCommand.Instance.Execute(expression, temporary: true);
                        handled = result;
                    }
                }
                else if (e.PressedMouseButton == System.Windows.Input.MouseButton.Middle)
                {
                    if (e.PressedModifierKeys.HasFlag(System.Windows.Input.ModifierKeys.Alt))
                    {
                        bool result = await MarkElementCommand.Instance.Execute(expression, temporary: true);
                        handled = result;
                    }
                    else if (e.PressedModifierKeys.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                    {
                        bool result = await ShowExpressionGraphCommand.Instance.Execute(expression);
                        handled = result;
                    }
                }
            }

            if(!handled)
            {
                // Deselect temporary highlighting.
                ClientInstance.ClearTemporaryHighlighting();
            }

            e.Handled = handled;
        }

        int exceptionCount = 0;
        int breakCount = 0;

        bool IsDebuggingUTC()
        {
            try
            {
                var processName = Path.GetFileName(debugger_.CurrentProcess.Name);
                return processName == "cl.exe" || processName == "link.exe";
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            if (!ClientInstance.IsEnabled)
            {
                return;
            }

            // https://stackoverflow.com/questions/13457948/how-to-display-waiting-popup-from-visual-studio-extension

            //if(breakCount == 0)
            //{
            //    object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_Deploy;

            //    statusBar_.Animation(1, ref icon);
            //    statusBar_.SetText("Starting IR Explorer");
            //}

            if(!IsDebuggingUTC())
            {
                return;
            }

            switch (Reason)
            {
                case dbgEventReason.dbgEventReasonStep:
                    {

                        //var textLine = GetCurrentTextLine();
                        //Debug.WriteLine("Current line: " + textLine);
                        //ClientInstance.UpdateCurrentStackFrame();
                        ClientInstance.UpdateIR();

                        // Tokenize and try to extract vars
                        // abc.xyz = def
                        break;
                    }
                case dbgEventReason.dbgEventReasonBreakpoint:
                case dbgEventReason.dbgEventReasonUserBreak:
                    {
                        //? This should check it's running cl/link and it's on a proper stack frame
                        ClientInstance.UpdateCurrentStackFrame();
                        ClientInstance.UpdateIR();

                        //ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                        break;
                    }
                case dbgEventReason.dbgEventReasonExceptionThrown:
                    {
                        ClientInstance.UpdateCurrentStackFrame();
                        ClientInstance.UpdateIR();

                        //if (exceptionCount >= 2)
                        //{
                        //    ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                        //}
                        break;
                    }
                
            }

            breakCount++;

        }


        private void DebuggerEvents_OnExceptionNotHandled(string ExceptionType, string Name, int Code, string Description, ref dbgExceptionAction ExceptionAction)
        {
            if (!ClientInstance.IsConnected)
            {
                return;
            }

            ClientInstance.Shutdown();
        }

        private void DebuggerEvents_OnEnterDesignMode(dbgEventReason Reason)
        {
            if (!ClientInstance.IsConnected)
            {
                return;
            }

            ClientInstance.Shutdown();
        }

        private void DebuggerEvents__OnEnterRunMode(dbgEventReason reason)
        {
            if (!ClientInstance.IsConnected)
            {
                return;
            }

            if (reason == dbgEventReason.dbgEventReasonGo)
            {
                // Pause client from handling events.
                ClientInstance.ClearTemporaryHighlighting();
                ClientInstance.PauseCurrentElementHandling();
            }
            else if (reason == dbgEventReason.dbgEventReasonStep)
            {
                // Resume client handling events.
                ClientInstance.ResumeCurrentElementHandling();
            }
        }


        private string GetCurrentTextLine()
        {
            if (dte_.ActiveDocument == null) {
                return "";
            }

            var textDocument = (EnvDTE.TextDocument)dte_.ActiveDocument.Object("TextDocument");
            var selection = textDocument.Selection;
            var activePoint = selection.ActivePoint;

            var lineOffset = activePoint.LineCharOffset - 1;
            return activePoint.CreateEditPoint().GetLines(activePoint.Line, activePoint.Line + 1);
        }


        #endregion
    }
}
