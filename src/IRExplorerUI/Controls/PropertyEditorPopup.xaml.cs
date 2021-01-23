using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Controls {
    public partial class PropertyEditorPopup : DraggablePopup, INotifyPropertyChanged {
        private PropertyValueManager valueManager_;

        public event PropertyChangedEventHandler PropertyChanged;
        
        public PropertyEditorPopup(PropertyValueManager valueManager,
                                    Point position, double width, double height,
                                    UIElement referenceElement) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;

            valueManager_ = valueManager;
            Editor.ValueManager = valueManager;
        }

        public PropertyValueManager ValueManager => valueManager_;

        public string PanelTitle {
            get => valueManager_.EditorTitle;
            set {
                if (valueManager_.EditorTitle != value) {
                    valueManager_.EditorTitle = value;
                    OnPropertyChange(nameof(PanelTitle));
                }
            }
        }

        private void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            valueManager_.SaveValues(Editor.Values);
            ClosePopup();
        }
    }
}
