// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Query;

// This is mostly a workaround for an issue with how WPF updates the DataTemplates
// used for the input/output values - the binding doesn't know an output value changed,
// so this basically forces an update of the entire query panel.
public class ElementQueryInfoView : INotifyPropertyChanged {
  private QueryDefinition view_;

  public ElementQueryInfoView(QueryDefinition value) {
    View = value;
    InputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.InputValues);
    OutputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.OutputValues);
    Buttons = new ObservableCollectionRefresh<QueryButton>(value.Data.Buttons);
  }

  public event EventHandler Closed;
  public event PropertyChangedEventHandler PropertyChanged;

  public QueryDefinition View {
    get => view_;
    set {
      if (view_ != value) {
        if (view_ != null) {
          view_.PropertyChanged -= ViewPropertyChanged;
        }

        view_ = value;
        view_.PropertyChanged += ViewPropertyChanged;
        OnPropertyChange("View");
      }
    }
  }

  public ObservableCollectionRefresh<QueryValue> InputValues { get; set; }
  public ObservableCollectionRefresh<QueryValue> OutputValues { get; set; }
  public ObservableCollectionRefresh<QueryButton> Buttons { get; set; }
  public bool HasButtons => Buttons.Count > 0;

  public void OnClose(object sender, EventArgs e) {
    Closed?.Invoke(this, e);
  }

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void ViewPropertyChanged(object sender, PropertyChangedEventArgs e) {
    InputValues.Clear();
    OutputValues.Clear();
    Buttons.Clear();
    InputValues.AddRange(View.Data.InputValues);
    OutputValues.AddRange(View.Data.OutputValues);
    Buttons.AddRange(View.Data.Buttons);
    OnPropertyChange("View");
  }
}

public partial class QueryPanel : DraggablePopup, INotifyPropertyChanged {
  public const double DefaultHeight = 270;
  public const double MinimumHeight = 100;
  public const double DefaultWidth = 300;
  public const double MinimumWidth = 100;
  private List<ElementQueryInfoView> activeQueries_;
  private List<QueryDefinition> registeredQueries_;
  private List<QueryDefinition> registeredUserQueries_;
  private string panelTitle_;
  private bool showAddButton_;
  private bool isActivePanel_;

  public QueryPanel(Point position, double width, double height,
                    UIElement referenceElement, ISession session) {
    InitializeComponent();
    Initialize(position, width, height, referenceElement);
    PanelResizeGrip.ResizedControl = this;
    Session = session;

    registeredQueries_ = new List<QueryDefinition>();
    registeredUserQueries_ = new List<QueryDefinition>();
    activeQueries_ = new List<ElementQueryInfoView>();

    PreviewMouseLeftButtonDown += QueryPanel_PreviewMouseLeftButtonDown;
    PreviewGotKeyboardFocus += QueryPanel_PreviewGotKeyboardFocus;
    GotFocus += QueryPanel_GotFocus;

    // Populate with the available queries.
    //var queries = Session.CompilerInfo.BuiltinQueries;

    //foreach (var query in queries) {
    //    RegisterQuery(query, true);
    //}

    //UpdateContextMenu();
    DataContext = this;
  }

  public event EventHandler PanelActivated;
  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }

  public string PanelTitle {
    get => panelTitle_;
    set {
      if (panelTitle_ != value) {
        panelTitle_ = value;
        OnPropertyChange(nameof(PanelTitle));
      }
    }
  }

  public bool ShowAddButton {
    get => showAddButton_;
    set {
      if (showAddButton_ != value) {
        showAddButton_ = value;
        OnPropertyChange(nameof(ShowAddButton));
      }
    }
  }

  public bool IsActivePanel {
    get => isActivePanel_;
    set {
      if (isActivePanel_ != value) {
        isActivePanel_ = value;
        OnPropertyChange(nameof(IsActivePanel));
      }
    }
  }

  public int QueryCount => activeQueries_.Count;

  public void RegisterQuery(QueryDefinition query, bool isBuiltin) {
    if (isBuiltin) {
      registeredQueries_.Add(query);
    }
    else {
      registeredUserQueries_.Add(query);
    }
  }

  public void AddQuery(QueryDefinition query) {
    var queryView = new ElementQueryInfoView(query);
    query.CreateQueryInstance(Session);
    queryView.Closed += QueryView_Closed;
    activeQueries_.Add(queryView);
    QueryViewList.ItemsSource = new CollectionView(activeQueries_);
  }

  public void RemoveQuery(QueryDefinition query) {
    foreach (var queryView in activeQueries_) {
      if (queryView.View == query) {
        queryView.Closed -= QueryView_Closed;
        activeQueries_.Remove(queryView);
        QueryViewList.ItemsSource = new CollectionView(activeQueries_);
        break;
      }
    }
  }

  public QueryDefinition GetQueryAt(int index) {
    return activeQueries_[index].View;
  }

  public void UpdateContextMenu() {
    //foreach (MenuItem item in QueryContextMenu.Items) {
    //    item.Click -= ContextMenuItem_Click;
    //}

    //QueryContextMenu.Items.Clear();

    //foreach (var query in registeredQueries_) {
    //    QueryContextMenu.Items.Add(CreateContextMenuItem(query));
    //}

    //if (registeredUserQueries_.Count > 0) {
    //    QueryContextMenu.Items.Add(new Separator());

    //    foreach (var query in registeredUserQueries_) {
    //        QueryContextMenu.Items.Add(CreateContextMenuItem(query));
    //    }
    //}
  }

  private void QueryView_Closed(object sender, EventArgs e) {
    var queryView = (ElementQueryInfoView)sender;
    queryView.Closed -= QueryView_Closed;
    activeQueries_.Remove(queryView);
    QueryViewList.ItemsSource = new CollectionView(activeQueries_);
  }

  private MenuItem CreateContextMenuItem(QueryDefinition query) {
    var item = new MenuItem {
      Header = query.Name,
      ToolTip = query.Description,
      Tag = query
    };

    item.Click += ContextMenuItem_Click;
    return item;
  }

  private void ContextMenuItem_Click(object sender, RoutedEventArgs e) {
    var menuItem = (MenuItem)sender;
    AddQuery((QueryDefinition)menuItem.Tag);
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    ClosePopup();
  }

  private void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void QueryPanel_GotFocus(object sender, RoutedEventArgs e) {
    PanelActivated?.Invoke(this, e);
  }

  private void QueryPanel_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
    PanelActivated?.Invoke(this, e);
  }

  private void QueryPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    PanelActivated?.Invoke(this, e);
  }
}
