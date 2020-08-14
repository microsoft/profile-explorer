using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IRExplorerUI.Controls;
using IRExplorerUI.Scripting;

namespace IRExplorerUI.Query {
    // This is mostly a workaround for an issue with how WPF updates the DataTemplates
    // used for the input/output values - the binding doesn't know an output value changed,
    // so this basically forces an update of the entire query panel.
    public class ElementQueryInfoView : INotifyPropertyChanged {
        private ElementQueryDefinition view_;

        public ElementQueryInfoView(ElementQueryDefinition value) {
            View = value;
            InputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.InputValues);
            OutputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.OutputValues);
            Buttons = new ObservableCollectionRefresh<QueryButton>(value.Data.Buttons);
        }

        public ElementQueryDefinition View {
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

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Closed;

        public void OnClose(object sender, EventArgs e) {
            Closed?.Invoke(this, e);
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

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public partial class QueryPanel : DraggablePopup {
        public const double DefaultHeight = 200;
        public const double MinimumHeight = 100;
        public const double DefaultWidth = 250;
        public const double MinimumWidth = 100;

        private List<ElementQueryInfoView> activeQueries_;
        private List<ElementQueryDefinition> registeredQueries_;
        private List<ElementQueryDefinition> registeredUserQueries_;

        public QueryPanel(Point position, double width, double height,
                          UIElement referenceElement, ISessionManager session) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;
            Session = session;

            registeredQueries_ = new List<ElementQueryDefinition>();
            registeredUserQueries_ = new List<ElementQueryDefinition>();
            activeQueries_ = new List<ElementQueryInfoView>();

            // Populate with the available queries.
            var queries = Session.CompilerInfo.BuiltinQueries;

            foreach (var query in queries) {
                RegisterQuery(query, true);
            }

            UpdateContextMenu();
        }

        public ISessionManager Session { get; set; }

        public void RegisterQuery(ElementQueryDefinition query, bool isBuiltin) {
            if (isBuiltin) {
                registeredQueries_.Add(query);
            }
            else {
                registeredUserQueries_.Add(query);
            }
        }

        public void AddQuery(ElementQueryDefinition query) {
            var queryView = new ElementQueryInfoView(query);
            query.CreateQueryInstance(Session);
            queryView.Closed += QueryView_Closed;
            activeQueries_.Add(queryView);
            QueryViewList.ItemsSource = new CollectionView(activeQueries_);
        }

        private void QueryView_Closed(object sender, EventArgs e) {
            var queryView = (ElementQueryInfoView)sender;
            queryView.Closed -= QueryView_Closed;
            activeQueries_.Remove(queryView);
            QueryViewList.ItemsSource = new CollectionView(activeQueries_);
        }

        public void UpdateContextMenu() {
            foreach (MenuItem item in QueryContextMenu.Items) {
                item.Click -= ContextMenuItem_Click;
            }

            QueryContextMenu.Items.Clear();

            foreach (var query in registeredQueries_) {
                QueryContextMenu.Items.Add(CreateContextMenuItem(query));
            }

            if (registeredUserQueries_.Count > 0) {
                QueryContextMenu.Items.Add(new Separator());

                foreach (var query in registeredUserQueries_) {
                    QueryContextMenu.Items.Add(CreateContextMenuItem(query));
                }
            }
        }

        private MenuItem CreateContextMenuItem(ElementQueryDefinition query) {
            var item = new MenuItem() {
                Header = query.Name,
                ToolTip = query.Description,
                Tag = query
            };

            item.Click += ContextMenuItem_Click;
            return item;
        }

        private void ContextMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
            var menuItem = (MenuItem)sender;
            AddQuery((ElementQueryDefinition)menuItem.Tag);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            ClosePopup();
        }
    }
}
