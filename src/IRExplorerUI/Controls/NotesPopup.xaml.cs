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
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Controls {
    /// <summary>
    /// Interaction logic for NotesPopup.xaml
    /// </summary>
    public partial class NotesPopup : DraggablePopup, INotifyPropertyChanged {
        private string panelTitle_;

        public event PropertyChangedEventHandler PropertyChanged;
        
        public NotesPopup(Point position, double width, double height,
                          UIElement referenceElement, ISession session) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            Session = session;
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;
        }

        public ISession Session {
            get => TextView.Session;
            set => TextView.Session = value;
        }

        public void SetText(string text) {
            TextView.Text = text;
        }

        public async Task SetText(string text, FunctionIR function, IRTextSection section,
                                  IRDocument associatedDocument) {
            await TextView.SetText(text, function, section, associatedDocument);
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
        private void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            ClosePopup();
        }
    }
}
