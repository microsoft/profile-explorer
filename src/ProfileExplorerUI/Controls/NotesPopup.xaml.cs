// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProfileExplorerCore2;
using ProfileExplorerCore2.IR;

namespace ProfileExplorer.UI.Controls;

public partial class NotesPopup : DraggablePopup, INotifyPropertyChanged {
  private string panelTitle_;

  public NotesPopup(Point position, double width, double height,
                    UIElement referenceElement) {
    InitializeComponent();
    Initialize(position, width, height, referenceElement);
    PanelResizeGrip.ResizedControl = this;
    DataContext = this;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  public IUISession Session {
    get => TextView.Session;
    set => TextView.Session = value;
  }

  public string PanelTitle {
    get => panelTitle_;
    set {
      if (panelTitle_ != value) {
        panelTitle_ = value;
        OnPropertyChange(nameof(PanelTitle));
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public void SetText(string text) {
    TextView.SetText(text);
    //? ProfileTextView.EnableIRSyntaxHighlighting();
  }

  public async Task SetText(string text, FunctionIR function, IRTextSection section,
                            IRDocument associatedDocument, IUISession session) {
    await TextView.SetText(text, function, section, associatedDocument, session);
  }

  public override bool ShouldStartDragging(MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
      if (!IsDetached) {
        DetachPopup();
      }

      return true;
    }

    return false;
  }

  private void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    ClosePopup();
  }
}