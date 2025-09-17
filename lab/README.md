# MVVM Lab (Hands-On Learning)

This lab is an intentionally *small* WPF application that mirrors the architectural concepts in the main project (base `UserControl` inheritance, typed settings, generic popup factory) so you can experiment safely.

## Goals
1. Understand why we create a base class that inherits `UserControl` (composition of shared wiring: settings, session, lifecycle, save/close handlers).
2. See how a generic factory method can create and host panels in a popup window.
3. Practice separating View (XAML), ViewModel (state/logic), and Model/Settings.
4. Learn step-by-step by modifying and extending the sample.

## Project Layout
```
lab/
  Lab.sln
  src/MvvmLabApp/
    MvvmLabApp.csproj
    App.xaml / App.xaml.cs          (WPF application startup)
    MainWindow.xaml / .cs           (Hosts inline panels and launches popups)
    MvvmInfrastructure.cs           (SettingsBase, ViewModelBase, LabPanelBase, generic layer)
    PanelHostPopup.xaml / .cs       (Popup host + generic factory method Create<TPanel,TSettings>)
    CounterPanel.xaml / .cs         (Example panel: View + ViewModel + typed Settings)
```

## Key Classes & Responsibilities
- `SettingsBase`: change-notifying state objects (persistable configuration or transient edits).
- `ViewModelBase`: implements `INotifyPropertyChanged` for UI binding.
- `LabPanelBase`: non-generic base deriving `UserControl` providing lifecycle + common delegates.
- `LabPanelBase<TSettings>`: generic typed convenience (casts `SettingsBase` to concrete type without repeated boilerplate).
- `PanelHostPopup.Create<TPanel,TSettings>()`: generic factory that constructs a panel, initializes it, hosts it in a popup `Window`, and wires save/close handlers.
- `CounterPanel`: concrete example; exposes a `CounterViewModel` (runtime state) and `CounterSettings` (user-editable configuration affecting behavior).

## Why a Base UserControl?
In larger apps you have MANY panels that all need:
- Access to settings (load, edit, save)
- Access to session/services (logging, DI, theme, telemetry, etc.)
- Standard lifecycle (initialize, close, save)
- Uniform save / apply semantics

A base class centralizes this wiring so each concrete panel focuses only on its domain logic/UI.

## Exercise 1: Trace Control Flow
1. Start in `MainWindow.xaml.cs` constructor.
2. Find where we populate `Panels` and see the lambda that calls `PanelHostPopup.Create<CounterPanel, CounterSettings>(...)`.
3. Jump to `PanelHostPopup.Create` and read how the panel is instantiated and initialized.
4. Inside `Create`, inspect how the generic save handler is adapted (`SaveAdapter`).
5. Follow the call to `panel.Initialize(...)` → open `LabPanelBase`.
6. Observe how `Initialize` stores dependencies then calls `OnInitialized()` (overridable hook).
7. Open `CounterPanel.xaml.cs` and note `OnInitialized()` override (currently empty).

Takeaway: Generic method orchestrates creation; base panel standardizes handshake.

## Exercise 2: Add Another Panel
Goal: Create a Temperature Converter panel.
1. Create `TemperatureSettings` (inherits `SettingsBase`) with a property `bool UseFahrenheit`.
2. Create `TemperatureViewModel` with properties: `double Input`, `double Output` and a method to compute.
3. Create `TemperaturePanel.xaml` + `.xaml.cs` inheriting `LabPanelBase<TemperatureSettings>`.
4. Bind TextBoxes to `VM.Input` and show computed `Output` (update on button click or property change).
5. Register the panel in `MainWindow` like the Counter panel.
6. Run and open it inline and as popup.

Concept Reinforced: Reuse of base wiring & factory by only adding minimal new code.

## Exercise 3: Implement a Save Operation
Currently the save delegate just returns the same settings.
1. In `MainWindow.xaml.cs`, change the panel registration delegate so `newSettingsHandler` increments `IncrementAmount` when saved.
2. Add a `TextBlock` in `CounterPanel` to show `Settings.IncrementAmount` live (already bound in settings section!).
3. Click Save repeatedly and watch the value change.

Concept: The panel doesn't know persistence details; it calls a delegate.

## Exercise 4: Close Semantics
1. Put a breakpoint (or log) inside `panelClosedHandler` passed to `Create`.
2. Close the popup window via the panel's Close button.
3. Observe the handler firing twice or once? (We call `NotifyClosed` in multiple paths—experiment removing one to understand responsibility boundaries.)
4. Decide on a single authoritative close path and refactor.

Concept: Ownership and lifecycle consistency.

## Exercise 5: Inline vs Popup Hosting
1. Note how inline embedding simply sets `InlineHost.Content = panel` without a `Window`.
2. Experiment: host the same `CounterPanel` in both inline and popup simultaneously. Observe shared vs separate state when using separate `CounterSettings` instances.
3. Pass same settings instance to both hosts—watch property changes propagate.

Concept: Separation of view instance vs model instance; MVVM encourages explicit state sharing decisions.

## Exercise 6: ViewModel vs Settings
1. Add a Reset button to `CounterPanel` that zeros `VM.Value` but leaves `Settings.IncrementAmount`.
2. Discuss: Why not store `Value` in settings? (Transient runtime state vs persisted configuration.)

Concept: Distinguish runtime ephemeral state (ViewModel) from durable configurable state (Settings).

## Exercise 7: Strongly Typed Save Flow
1. Modify `PanelHostPopup.Create` to only allow closing if save returns successfully when `close == true`.
2. Add a "Save & Close" button that calls `await SaveAsync(true)` then closes.
3. Simulate validation: In `newSettingsHandler`, reject if `IncrementAmount <= 0` (throw or return modified). Handle errors gracefully (MessageBox).

Concept: Encapsulating validation and persistence outside control; panel stays lean.

## Exercise 8 (Stretch): Commands
1. Replace button Click handlers with `ICommand` instances on the ViewModel.
2. Introduce a lightweight `RelayCommand` implementation.
3. Bind `Button.Command` in XAML.

Concept: Further decoupling for testability and consistency with MVVM patterns.

## Mental Model Diagram (Simplified)
```
[MainWindow]
    creates → PanelHostPopup
        hosts → [CounterPanel : LabPanelBase<CounterSettings>]
            has → CounterViewModel (runtime state)
            has → CounterSettings (config state)

LabPanelBase (UserControl)
  + SettingsBase (model)
  + Session (services)
  + Save delegate
  + Closed delegate
```

## Suggested Next Comparisons with Real Project
| Lab Concept | Real Project Analog |
|-------------|---------------------|
| LabPanelBase | OptionsPanelBase / OptionsPaneBase |
| SettingsBase | Concrete settings/option models |
| PanelHostPopup.Create | OptionsPanelHostPopup.Create | 
| ViewModelBase | Panels using internal view models / data contexts |

## Further Experiments
- Add DI container (e.g., Microsoft.Extensions.DependencyInjection) to resolve panels.
- Introduce an interface `ILabPanel` for discovery.
- Add a navigation service that tracks open panels.
- Serialize settings to JSON on Save.

## How to Run
Open `Lab.sln` in Visual Studio 2022 (or run build via CLI):
```
dotnet build lab/Lab.sln
```
Press F5 / run. Interact with Counter panel inline and as popup.

---
Feel free to iterate; each exercise builds understanding of the MVVM layering and generic factory pattern.
