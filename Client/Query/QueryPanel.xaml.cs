﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Client.Scripting;

namespace Client.Query
{
    // This is mostly a workaround for an issue with how WPF updates the DataTemplates
    // used for the input/output values - the binding doesn't know an output value changed,
    // so this basically forces an update of the entire query panel.
    public class ElementQueryInfoView : INotifyPropertyChanged
    {
        private ElementQueryInfo view_;

        public ElementQueryInfo View
        {
            get => view_;
            set
            {
                if (view_ != value)
                {
                    if (view_ != null)
                    {
                        view_.PropertyChanged -= ViewPropertyChanged;
                    }

                    view_ = value;
                    view_.PropertyChanged += ViewPropertyChanged;
                    OnPropertyChange("View");
                }
            }
        }

        public ElementQueryInfoView(ElementQueryInfo value)
        {
            View = value;
            InputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.InputValues);
            OutputValues = new ObservableCollectionRefresh<QueryValue>(value.Data.OutputValues);
        }

        public ObservableCollectionRefresh<QueryValue> InputValues { get; set; }
        public ObservableCollectionRefresh<QueryValue> OutputValues { get; set; }

        private void ViewPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            InputValues.Clear();
            OutputValues.Clear();
            InputValues.AddRange(View.Data.InputValues);
            OutputValues.AddRange(View.Data.OutputValues);
            OnPropertyChange("View");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public partial class QueryPanel : UserControl
    {
        List<Tuple<string, IElementQuery>> registeredQueries_;
        List<Tuple<string, IElementQuery>> registeredUserQueries_;
        List<ElementQueryInfoView> activeQueries_;

        public QueryPanel()
        {
            InitializeComponent();
            registeredQueries_ = new List<Tuple<string, IElementQuery>>();
            registeredUserQueries_ = new List<Tuple<string, IElementQuery>>();
            activeQueries_ = new List<ElementQueryInfoView>();
        }

        public void RegisterQuery(string name, IElementQuery query, bool isBuiltin)
        {
            var pair = new Tuple<string, IElementQuery>(name, query);

            if(isBuiltin)
            {
                registeredQueries_.Add(pair);
            }
            else
            {
                registeredUserQueries_.Add(pair);
            }
        }

        public void AddQuery(ElementQueryInfo query)
        {
            var queryView = new ElementQueryInfoView(query);
            activeQueries_.Add(queryView);
            QueryViewList.ItemsSource = new CollectionView(activeQueries_);
        }
    }
}
