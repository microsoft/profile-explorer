# MCP Server UI Entry Points (ProfileExplorerUI)

Succinct reference for wiring an MCP server to trigger key profiling UI actions programmatically.

## 1. "Profiling" Menu Item (Top Menu Bar)
- Location: `src/ProfileExplorerUI/MainWindow.xaml`
- XAML Snippet (lines ~498-505): `<MenuItem Header="Profiling"> ... </MenuItem>`
- Implementation: Pure XAML container; no direct Click handler. Child menu items use `RoutedUICommand`s defined in `AppCommand` (see `MainWindow.xaml.cs`).
- Command Definition: `public static readonly RoutedUICommand LoadProfile` inside `AppCommand` (`MainWindow.xaml.cs`, lines ~38-70 region). Other profiling-related commands nearby: `RecordProfile`, `ViewProfileReport`.
- Programmatic Trigger: Obtain `MainWindow` instance and execute the command: `AppCommand.LoadProfile.Execute(null, mainWindow);`

## 2. "Load Profile" Menu Item (Under Profiling)
- Location: `src/ProfileExplorerUI/MainWindow.xaml` line ~501.
- XAML: `<MenuItem Command="local:AppCommand.LoadProfile" Header="Load Profile" />`
- Command Binding Declaration: In the same XAML file near top (lines ~120-127) `<CommandBinding Command="local:AppCommand.LoadProfile" CanExecute="CanExecuteLoadProfileCommand" Executed="LoadProfileExecuted" PreviewCanExecute="CanExecuteLoadProfileCommand" />`
- Handler (Executed): `LoadProfileExecuted` in `MainWindowProfiling.cs` (line ~500) which awaits private `LoadProfile()`.
- Core Flow:
  1. `LoadProfileExecuted(object, ExecutedRoutedEventArgs)` -> `await LoadProfile();`
  2. `LoadProfile()` creates and shows `ProfileLoadWindow` (`new ProfileLoadWindow(this, false)`), sets `Owner`, waits for `ShowDialog()`.
  3. On success, calls `SetupLoadedProfile()` to refresh panels.
- Alternate Invocation: Call `await mainWindow.GetType().GetMethod("LoadProfile", BindingFlags.NonPublic|BindingFlags.Instance).Invoke(mainWindow, null);` or simpler: execute the command (preferred). For automation, dispatch on UI thread.

## 3. Entry Point: Setting / Pasting "Profile data file" Path (Load Profile Dialog)
- Dialog Class: `ProfileLoadWindow` (`src/ProfileExplorerUI/Windows/ProfileLoadWindow.xaml` & `.xaml.cs`). Title: `Load profile trace`.
- UI Control: `FileSystemTextBox` named `ProfileAutocompleteBox` (XAML lines ~560-590 & ~577 specifically) bound TwoWay to `ProfileFilePath` property.
- Data Property: `public string ProfileFilePath { get; set; }` (backing field `profileFilePath_`) in `ProfileLoadWindow.xaml.cs` lines ~80-87 triggers `OnPropertyChange`.
- Validation & Load Path Normalization: Inside `LoadProfileTraceFile(...)` (lines ~338-399): `ProfileFilePath = Utils.CleanupPath(ProfileFilePath);` then `Utils.ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile", this)`.
- Reactive Process List Population: `ProfileAutocompleteBox_TextChanged` (line ~726) eventually leads to loading process summaries (lines ~732-739 and `LoadProcessList(ProfileFilePath)` at ~735) which calls `ETWProfileDataProvider.FindTraceProcesses`.
- Programmatic Path Injection (before showing processes):
  ```csharp
  var win = new ProfileLoadWindow(mainWindow, false);
  win.ProfileFilePath = pathToTrace; // triggers binding update
  // Optionally force process enumeration:
  await win.Dispatcher.InvokeAsync(() => win.GetType()
      .GetMethod("ProfileAutocompleteBox_TextChanged", BindingFlags.NonPublic|BindingFlags.Instance)
      ?.Invoke(win, new object?[]{ win, new RoutedEventArgs() }));
  ```
  Better: simulate TextChanged by setting property then calling private async path load sequence via reflection, or simply let the user press Load (automation can directly invoke Load, see next section).
- Direct Load Without UI Interaction: Call private `LoadProfileTraceFileAndCloseWindow(SymbolFileSourceSettings)` (line ~335) after populating `symbolSettings_` and `selectedProcSummary_`. You must also set `selectedProcSummary_` (private) or simulate process selection—otherwise it fails (`if (selectedProcSummary_ == null) return false;`). See section 4 for selecting processes.

## 4. Process Selection (After Trace Path Provided)
- Process List Control: `ListView x:Name="ProcessList"` (XAML lines ~620-690) `SelectionMode="Extended"` with handler `SelectionChanged="ProcessList_OnSelectionChanged"` and `MouseDoubleClick` mapped to `LoadButton_Click` (loads profile immediately).
- Selection Handler: `ProcessList_OnSelectionChanged` in `ProfileLoadWindow.xaml.cs` (line ~596) sets internal list:
  ```csharp
  selectedProcSummary_ = new List<ProcessSummary>(ProcessList.SelectedItems.OfType<ProcessSummary>());
  BinaryFilePath = selectedProcSummary_[0].Process.Name;
  ```
- Double-Click / Enter Activation: The `EventSetter MouseDoubleClick -> LoadButton_Click` (same XAML region), and `PreviewKeyDown -> ProcessList_PreviewKeyDown` (not detailed here) allow immediate load via same path as pressing the Load button.
- Load Button Handler: `LoadButton_Click` (line ~330 region -> actual at ~320-340 earlier snippet) calls `LoadProfileTraceFileAndCloseWindow(symbolSettings_)` -> `LoadProfileTraceFile` -> `Session.LoadProfileData(...)` then closes window on success.
- Programmatic Selection:
  ```csharp
  // After process list is populated:
  ProcessList.SelectedItems.Clear();
  foreach (var item in ProcessList.Items.Cast<ProcessSummary>().Where(p => desiredIds.Contains(p.Process.ProcessId))) {
      ProcessList.SelectedItems.Add(item);
  }
  // Trigger handler if needed:
  ProcessList_OnSelectionChanged(ProcessList, new SelectionChangedEventArgs(ListView.SelectionChangedEvent, new object[0], ProcessList.SelectedItems.Cast<object>().ToList()));
  // Then simulate double-click or directly invoke load:
  LoadButton_Click(LoadButton, new RoutedEventArgs());
  ```
- Direct Backend Load (Bypass UI): Use `MainWindow.LoadProfileData(string profileFilePath, List<int> processIds, ...)` defined in `MainWindowProfiling.cs` (public `Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProcessingOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProgressCallback, CancelableTaskInstance task)`). Exposed through the `IUISession` interface. This is the cleanest MCP entrypoint avoiding UI automation.

## Summary Table
| Action | UI Element | Handler / API | File | Notes |
|--------|------------|---------------|------|-------|
| Open Profiling menu | `<MenuItem Header="Profiling">` | (n/a container) | MainWindow.xaml | Contains profiling commands. |
| Click Load Profile | MenuItem Command | `LoadProfileExecuted` -> `LoadProfile()` | MainWindowProfiling.cs | Routed command binding. |
| Set trace path | `ProfileAutocompleteBox` | `ProfileFilePath` property; `ProfileAutocompleteBox_TextChanged` | ProfileLoadWindow.xaml/.cs | Setting property triggers process enumeration. |
| Select process(es) | `ProcessList` | `ProcessList_OnSelectionChanged` | ProfileLoadWindow.xaml.cs | Updates `selectedProcSummary_`. |
| Double-click process | `ProcessList` Item | `LoadButton_Click` | ProfileLoadWindow.xaml.cs | Invokes load & closes window. |
| Backend direct load | (no UI) | `MainWindow.LoadProfileData(...)` | MainWindowProfiling.cs | Preferred programmatic entry. |
## Implementation Guidance (Practical)
The codebase currently couples UI workflow with backend state setup, so MCP automation will likely need to simulate:
1. Executing `AppCommand.LoadProfile` (opens dialog).
2. Setting `ProfileLoadWindow.ProfileFilePath` (triggers process discovery).
3. Selecting items in `ProcessList` (fires `ProcessList_OnSelectionChanged`).
4. Invoking `LoadButton_Click` (or double-clicking a process) to commit.

Direct backend calls (`MainWindow.LoadProfileData`) are available but may bypass important UI-initialized state; use only if you ensure equivalent setup.

---
Generated: 2025-09-12
