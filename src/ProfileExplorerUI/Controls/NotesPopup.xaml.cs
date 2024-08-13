// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;

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

  public event PropertyChangedEventHandler PropertyChanged;
  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  public ISession Session {
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

  public void SetText(string text) {
    TextView.SetText(text);
    //? ProfileTextView.EnableIRSyntaxHighlighting();
  }

  public async Task SetText(string text, FunctionIR function, IRTextSection section,
                            IRDocument associatedDocument, ISession session) {
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