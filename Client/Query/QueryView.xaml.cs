using System;
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
    public partial class QueryView : UserControl
    {
        public QueryView()
        {
            InitializeComponent();
        }

        private ElementQueryInfo query_;
        public ElementQueryInfo Query
        {
            get => query_;
            set
            {
                if (value != query_)
                {
                    query_ = value;
                    DataContext = query_;

                    InputElementList.ItemsSource = new CollectionView(query_.Data.InputValues);
                    OutputElementList.ItemsSource = new CollectionView(query_.Data.OutputValues);
                }
            }
        }
    }
}
