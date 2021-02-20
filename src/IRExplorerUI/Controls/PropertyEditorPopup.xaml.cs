using System;
using System.Collections.Generic;
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
using DiffPlex.Model;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.OptionsPanels;

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

        public static PropertyEditorPopup 
            ShowOverPanel(OptionsPanelBase panel, PropertyValueManager valueManager, 
                          string title, double width, double height) {
            if (panel.Parent is OptionsPanelHostWindow popup) {
                popup.StaysOpen = true;
            }

            var position = panel.ParentPosition;
            var editorPopup = new PropertyEditorPopup(valueManager, position, width, height, null);
            editorPopup.PanelTitle = title;
            editorPopup.StaysOpen = true;
            editorPopup.IsOpen = true;

            editorPopup.PopupClosed += (sender, e) => {
                if (panel.Parent is OptionsPanelHostWindow popup) {
                    popup.StaysOpen = false;
                }

                editorPopup.IsOpen = false;
            };

            return editorPopup;
        }

        public PropertyValueManager ValueManager => valueManager_;
        public List<object> Values => Editor.Values;

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
            ClosePopup();
        }
    }
}
