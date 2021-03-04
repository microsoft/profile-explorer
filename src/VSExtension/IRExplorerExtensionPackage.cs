// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using EnvDTE;
using EnvDTE80;
using IRExplorerCore.Lexer;
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
                        bool result = await MarkElementCommand.Instance.Execute(expression, true);

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
                        bool result = await ShowExpressionGraphCommand.Instance.Execute(expression);

                        handled = result;
                    }
                }
            }

            if (!handled) {
                // Deselect temporary highlighting.
                //Logger.Log("> OnMouseUp: not handled");
                await ClientInstance.ClearTemporaryHighlighting();
            }
            else {
                //Logger.Log("> OnMouseUp: handled");
            }

            e.Handled = handled;
        }

        private static List<Token> TokenizeString(string value) {
            var lexer = new Lexer(value);
            var tokens = new List<Token>();

            while (true) {
                var token = lexer.NextToken();

                if (token.IsLineEnd() || token.IsEOF()) {
                    break;
                }

                tokens.Add(token);
            }

            return tokens;
        }

        int FindEqualsOnRight(List<Token> tokens, int i) {
            for (i = i + 1; i < tokens.Count; i++) {
                if (tokens[i].Kind == TokenKind.Equal) {
                    return i;
                }
            }

            return -1;
        }

        //? keep stack, restore vars
        //?   also show local vars, maybe with arrows
        //?   as a sidebar on the right, pointing to the tuples?
        private Dictionary<long, long> funcVariables_;

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
                        var textLine = GetCurrentTextLine();
                        Logger.Log("Current line: " + textLine);

                        AutoMarkVariables(textLine);

                        var prevTextLine = GetPreviousTextLine();

                        if (!string.IsNullOrEmpty(prevTextLine)) {
                            AutoMarkVariables(prevTextLine);
                        }

                        var locals = DebuggerInstance.GetLocalVariables();

                        foreach (var localVar in locals) {
                            Logger.Log($"Local var: {localVar}");
                        }

                        JoinableTaskFactory.Run(() => ClientInstance.UpdateCurrentStackFrame());
                        JoinableTaskFactory.Run(() => ClientInstance.UpdateIR());

                        // Tokenize and try to extract vars
                        // abc.xyz = def
                        break;
                    }
                case dbgEventReason.dbgEventReasonBreakpoint:
                case dbgEventReason.dbgEventReasonUserBreak: {
                        //? This should check it's running cl/link and it's on a proper stack frame
                        JoinableTaskFactory.Run(() => ClientInstance.UpdateCurrentStackFrame());
                        JoinableTaskFactory.Run(() => ClientInstance.UpdateIR());

                        //ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                        break;
                    }
                case dbgEventReason.dbgEventReasonExceptionThrown: {
                        JoinableTaskFactory.Run(() => ClientInstance.UpdateCurrentStackFrame());
                        JoinableTaskFactory.Run(() => ClientInstance.UpdateIR());

                        //if (exceptionCount >= 2)
                        //{
                        //    ExecutionAction = dbgExecutionAction.dbgExecutionActionStepOut;
                        //}
                        break;
                    }
            }
        }

        private void AutoMarkVariables(string textLine) {
            var tokens = TokenizeString(textLine);

            for (int i = 0; i < tokens.Count; i++) {
                if (tokens[i].IsIdentifier()) {
                    var name = tokens[i].Data.ToString();
                    bool isSource = false;

                    if (DebuggerInstance.IsReservedKeyword(name)) {
                        Logger.Log($"< reserved: {name}");
                        continue;
                    }

                    if (FindEqualsOnRight(tokens, i) != -1) {
                        Logger.Log($"Dest: {name}");
                    }
                    else {
                        Logger.Log($"Source: {name}");
                        isSource = true;
                    }

                    var variable = DebuggerExpression.Create(
                        new TextLineInfo(textLine, 0, tokens[i].Location.Offset,
                            tokens[i].Location.Offset));
                    bool temporary = false;
                    var sourceColor = new RGBColor() {R = 100, G = 255, B = 255};
                    var destColor = new RGBColor() { R = 255, G = 255, B = 100 };

                    if (variable != null) {
                        long elementAddress = DebuggerInstance.ReadElementAddress(variable);
                        Logger.Log($"Mark references for {elementAddress:x}: {variable}");

                        if (elementAddress == 0) {
                            continue;
                        }

                        funcVariables_ ??= new Dictionary<long, long>();
                        long varAddress = DebuggerInstance.GetVariableAddress(variable);

                        // Unmark previous pointed element.
                        if (varAddress != 0 &&
                            funcVariables_.TryGetValue(varAddress, out var currentElementAddress) &&
                            currentElementAddress != elementAddress) {
                            ClientInstance.RunClientCommand(
                                () => ClientInstance.debugClient_.ExecuteCommand(new ElementCommandRequest {
                                    Command = ElementCommand.ClearMarker,
                                    ElementAddress = currentElementAddress,
                                })).Wait();
                        }

                        funcVariables_[varAddress] = elementAddress;

                        //? use  a list of common types to exclude
                        //? - when entering a calee, make caller vars translucent, then back to 100% on return
                        //? - if new IR version created, copy over the markers

                        ClientInstance.RunClientCommand(
                            () => ClientInstance.debugClient_.MarkElement(new MarkElementRequest {
                                ElementAddress = elementAddress,
                                Label = variable,
                                Color = isSource ? sourceColor : destColor,
                                Highlighting = temporary
                                    ? HighlightingType.Temporary
                                    : HighlightingType.Permanent
                            })).Wait();
                    }
                }
            }
        }

        private void DebuggerEvents_OnExceptionNotHandled(
            string ExceptionType, string Name, int Code, string Description,
            ref dbgExceptionAction ExceptionAction) {
            if (!ClientInstance.IsConnected) {
                return;
            }

            JoinableTaskFactory.Run(async () => await ClientInstance.Shutdown());
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
                JoinableTaskFactory.Run(() => ClientInstance.ClearTemporaryHighlighting());
                JoinableTaskFactory.Run(() => ClientInstance.PauseCurrentElementHandling());
            }
            else if (reason == dbgEventReason.dbgEventReasonStep) {
                // Resume client handling events.
                JoinableTaskFactory.Run(() => ClientInstance.ResumeCurrentElementHandling());
            }
        }

        private string GetCurrentTextLine() {
            if (dte_.ActiveDocument == null) {
                return "";
            }

            var textDocument = (TextDocument)dte_.ActiveDocument.Object("TextDocument");
            var selection = textDocument.Selection;
            var activePoint = selection.ActivePoint;

            return activePoint.CreateEditPoint()
                              .GetLines(activePoint.Line, activePoint.Line + 1);
        }

        private string GetPreviousTextLine() {
            if (dte_.ActiveDocument == null) {
                return "";
            }

            var textDocument = (TextDocument)dte_.ActiveDocument.Object("TextDocument");
            var selection = textDocument.Selection;
            var activePoint = selection.ActivePoint;

            if (activePoint.Line == 0) {
                return null;
            }

            return activePoint.CreateEditPoint()
                .GetLines(activePoint.Line - 1, activePoint.Line);
        }

        #endregion
    }
}
