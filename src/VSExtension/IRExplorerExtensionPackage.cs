// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Constants = Microsoft.VisualStudio.Shell.Interop.Constants;
using Task = System.Threading.Tasks.Task;
using Thread = EnvDTE.Thread;

namespace IRExplorerExtension {
    /// <summary>
    ///     This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The minimum requirement for a class to be considered a valid package for Visual Studio
    ///         is to implement the IVsPackage interface and register itself with the shell.
    ///         This package uses the helper classes defined inside the Managed Package Framework (MPF)
    ///         to do it: it derives from the Package class that provides the implementation of the
    ///         IVsPackage interface and uses the registration attributes defined in the framework to
    ///         register itself and its components with the shell. These attributes tell the pkgdef
    ///         creation
    ///         utility what data to put into .pkgdef file.
    ///     </para>
    ///     <para>
    ///         To get loaded into VS, the package must be referred by &lt;Asset
    ///         Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    ///     </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(IRExplorerExtension.Windows.ExpressionToolWindow))]

    //[ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    public sealed class IRExplorerExtensionPackage : AsyncPackage {
        /// <summary>
        ///     IRExplorerExtensionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "c29cc20e-a6f8-412f-a266-3adc6f822594";
        public static IRExplorerExtensionPackage Instance;
        private static IVsStatusbar statusBar_;
        private Debugger debugger_;
        private DebuggerEvents debuggerEvents_;

        private DTE2 dte_;

        public static void SetStatusBar(string text, bool animated = false) {
            object icon = (short)Constants.SBAI_General;
            statusBar_.Animation(animated ? 1 : 0, ref icon);
            statusBar_.SetText(text);
        }

        #region Package Members

        /// <summary>
        ///     Initialization of the package; this method is called right after the package is sited, so this
        ///     is the place
        ///     where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">
        ///     A cancellation token to monitor for initialization cancellation,
        ///     which can occur when VS is shutting down.
        /// </param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>
        ///     A task representing the async work of package initialization, or an already completed task
        ///     if there is none. Do not return null from this method.
        /// </returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken,
                                                      IProgress<ServiceProgressData>
                                                          progress) {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await AttachCommand.InitializeAsync(this);
            await MarkElementCommand.InitializeAsync(this);
            await MarkUsesCommand.InitializeAsync(this);
            await MarkExpressionCommand.InitializeAsync(this);
            await MarkReferencesCommand.InitializeAsync(this);
            await ShowReferencesCommand.InitializeAsync(this);
            await ShowExpressionGraphCommand.InitializeAsync(this);
            await EnableCommand.InitializeAsync(this);
            await UpdateIRCommand.InitializeAsync(this);

            await IRExplorerExtension.Windows.ExpressionToolWindowCommand.InitializeAsync(this);

            // https://stackoverflow.com/questions/22570121/debuggerevents-onenterbreakmode-is-not-triggered-in-the-visual-studio-extension
            dte_ = await GetServiceAsync(typeof(DTE)) as DTE2;
            statusBar_ = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            debugger_ = dte_.Debugger;
            debuggerEvents_ = dte_.Events.DebuggerEvents;

            // dte_.Events.BuildEvents.OnBuildDone += BuildEvents_OnBuildDone;
            debuggerEvents_.OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;
            debuggerEvents_.OnEnterRunMode += DebuggerEvents__OnEnterRunMode;
            debuggerEvents_.OnEnterDesignMode += DebuggerEvents_OnEnterDesignMode;
            debuggerEvents_.OnExceptionNotHandled += DebuggerEvents_OnExceptionNotHandled;
            debuggerEvents_.OnContextChanged += DebuggerEvents__OnContextChanged;
            Logger.Initialize(this, "IR Explorer");
            ClientInstance.Initialize(this);
            DebuggerInstance.Initialize(debugger_);
        }

        private void BuildEvents_OnBuildDone(vsBuildScope Scope, vsBuildAction Action) {
            throw new NotImplementedException();
        }

        private void DebuggerEvents__OnContextChanged(Process NewProcess, Program NewProgram,
                                                      Thread NewThread,
                                                      EnvDTE.StackFrame NewStackFrame) { }

        internal static void RegisterMouseProcessor(MouseEventProcessor instance) {
            instance.OnMouseUp += MouseProcessor_OnMouseUp;
        }

        private static async void MouseProcessor_OnMouseUp(object sender, TextLineInfo e) {
            if (!ClientInstance.IsConnected) {
                return;
            }

            //Logger.Log($"> OnMouseUp, {e.PressedMouseButton}, {e.PressedModifierKeys}");
            //Logger.Log($"    text {e.LineNumber}:{e.LineColumn}: {e.LineText}");
            bool handled = false;

            if (e.HasTextInfo) {
                //Logger.Log(">> Create debugger expression");
                string expression = DebuggerExpression.Create(e);
                //Logger.Log($">> Debugger expression: {expression}");

                if (e.PressedMouseButton == MouseButton.Left) {
                    if (e.PressedModifierKeys.HasFlag(ModifierKeys.Alt)) {
                        bool result =
                            await MarkElementCommand.Instance.Execute(expression, true);

                        handled = result;
                    }
                }
                else if (e.PressedMouseButton == MouseButton.Middle) {
                    if (e.PressedModifierKeys.HasFlag(ModifierKeys.Alt)) {
                        bool result =
                            await MarkElementCommand.Instance.Execute(expression, true);

                        handled = result;
                    }
                    else if (e.PressedModifierKeys.HasFlag(ModifierKeys.Shift)) {
                        bool result =
                            await ShowExpressionGraphCommand.Instance.Execute(expression);

                        handled = result;
                    }
                }
            }

            if (!handled) {
                // Deselect temporary highlighting.
                //Logger.Log("> OnMouseUp: not handled");
                ClientInstance.ClearTemporaryHighlighting();
            }
            else {
                //Logger.Log("> OnMouseUp: handled");
            }

            e.Handled = handled;
        }

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason,
                                                     ref dbgExecutionAction ExecutionAction) {
            if (!ClientInstance.IsEnabled) {
                Logger.Log("IR Explorer not enabled");
                return;
            }

            if (!DebuggerInstance.IsDebuggingUTC) {
                Logger.Log("Not debugging UTC");
                return;
            }
            else if (!ClientInstance.IsConnected && !ClientInstance.AutoAttach) {
                Logger.Log("IR Explorer no auto-attach");
                return;
            }

            switch (Reason) {
                case dbgEventReason.dbgEventReasonStep: {
                    //var textLine = GetCurrentTextLine();
                    //Debug.WriteLine("Current line: " + textLine);
                    ClientInstance.UpdateCurrentStackFrame();
                    ClientInstance.UpdateIR();

                    // Tokenize and try to extract vars
                    // abc.xyz = def
                    break;
                }
                case dbgEventReason.dbgEventReasonBreakpoint:
                case dbgEventReason.dbgEventReasonUserBreak: {
                    //? This should check it's running cl/link and it's on a proper stack frame
                    ClientInstance.UpdateCurrentStackFrame();
                    ClientInstance.UpdateIR();

                    //ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                    break;
                }
                case dbgEventReason.dbgEventReasonExceptionThrown: {
                    ClientInstance.UpdateCurrentStackFrame();
                    ClientInstance.UpdateIR();

                    //if (exceptionCount >= 2)
                    //{
                    //    ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                    //}
                    break;
                }
            }
        }

        private void DebuggerEvents_OnExceptionNotHandled(
            string ExceptionType, string Name, int Code, string Description,
            ref dbgExceptionAction ExceptionAction) {
            if (!ClientInstance.IsConnected) {
                return;
            }

            JoinableTaskFactory.Run(() => ClientInstance.Shutdown());
        }

        private async void DebuggerEvents_OnEnterDesignMode(dbgEventReason Reason) {
            if (!ClientInstance.IsConnected) {
                return;
            }

            await ClientInstance.Shutdown();
        }

        private void DebuggerEvents__OnEnterRunMode(dbgEventReason reason) {
            if (!ClientInstance.IsConnected) {
                return;
            }

            if (reason == dbgEventReason.dbgEventReasonGo) {
                // Pause client from handling events.
                ClientInstance.ClearTemporaryHighlighting();
                ClientInstance.PauseCurrentElementHandling();
            }
            else if (reason == dbgEventReason.dbgEventReasonStep) {
                // Resume client handling events.
                ClientInstance.ResumeCurrentElementHandling();
            }
        }

        private string GetCurrentTextLine() {
            if (dte_.ActiveDocument == null) {
                return "";
            }

            var textDocument = (TextDocument)dte_.ActiveDocument.Object("TextDocument");
            var selection = textDocument.Selection;
            var activePoint = selection.ActivePoint;
            int lineOffset = activePoint.LineCharOffset - 1;

            return activePoint.CreateEditPoint()
                              .GetLines(activePoint.Line, activePoint.Line + 1);
        }

        #endregion
    }
}
