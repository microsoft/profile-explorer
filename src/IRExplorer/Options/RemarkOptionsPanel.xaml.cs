using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace Client.Options
{
    /// <summary>
    /// Interaction logic for RemarkOptionsPanel.xaml
    /// </summary>
    public partial class RemarkOptionsPanel : UserControl
    {
        public RemarkOptionsPanel()
        {
            InitializeComponent();
            PreviewMouseUp += RemarkOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += RemarkOptionsPanel_PreviewKeyUp;

            kindCheckboxes_ = new List<CheckBox>();
            kindCheckboxes_.Add(OptimizationCheckbox);
            kindCheckboxes_.Add(AnalysisCheckbox);
            kindCheckboxes_.Add(StandardCheckbox);
            kindCheckboxes_.Add(VerboseCheckbox);
            kindCheckboxes_.Add(TraceCheckbox);
        }
        
        private List<CheckBox> kindCheckboxes_;
        private List<CheckBox> categoryCheckboxes_;

        private void SetCheckboxesState(List<CheckBox> list, bool state)
        {
            list.ForEach((item) => item.IsChecked = state);
        }

        private void RemarkOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            NotifySettingsChanged();
        }

        private void RemarkOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            NotifySettingsChanged();
        }

        public ICompilerInfoProvider CompilerInfo { get; set; }

        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler SettingsChanged;

        public void Initialize() {
            PopulateCategoryList();
        }

        private bool PopulateCategoryList() {
            var remarkSettings = (RemarkSettings)DataContext;
            categoryCheckboxes_ = new List<CheckBox>();

            bool initialLoad = !remarkSettings.HasCategoryFilters;
            var categories = CompilerInfo.RemarkProvider.LoadRemarkCategories();

            if(categories == null) {
                MessageBox.Show("Failed to load remark settings file,\ncheck JSON for any syntax errors!", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            CategoriesList.Children.Clear();

            foreach (var category in categories) {
                if (!string.IsNullOrEmpty(category.Title)) {
                    var checkbox = new CheckBox();
                    checkbox.Content = category.Title;
                    checkbox.Margin = new Thickness(0, 0, 0, 2);

                    if (category.AddTextMark) {
                        checkbox.Background = ColorBrushes.GetBrush(category.TextMarkBorderColor);
                    }
                    else if (category.AddLeftMarginMark) {
                        checkbox.Background = ColorBrushes.GetBrush(category.MarkColor);
                    }

                    if (initialLoad) {
                        checkbox.IsChecked = true;
                    }
                    else if (remarkSettings.CategoryFilter.TryGetValue(category.Title, out bool state)) {
                        checkbox.IsChecked = state;
                    }

                    checkbox.Tag = category;
                    checkbox.Checked += Checkbox_CheckedChanged;
                    checkbox.Unchecked += Checkbox_CheckedChanged;
                    CategoriesList.Children.Add(checkbox);
                    categoryCheckboxes_.Add(checkbox);
                }
            }

            return true;
        }

        private void Checkbox_CheckedChanged(object sender, RoutedEventArgs e) {
            UpdateCategoryFilter();
            NotifySettingsChanged();
        }

        private void UpdateCategoryFilter() {
            var remarkSettings = (RemarkSettings)DataContext;
            remarkSettings.CategoryFilter = new Dictionary<string, bool>();

            foreach(var checkbox in categoryCheckboxes_) {
                var category = (RemarkCategory)checkbox.Tag;
                remarkSettings.CategoryFilter[category.Title] = checkbox.IsChecked.HasValue && checkbox.IsChecked.Value;
            }
        }

        private void NotifySettingsChanged() {
            if (SettingsChanged != null) {
                DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                    SettingsChanged(this, null);
                });
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            UpdateCategoryFilter();
            PanelClosed?.Invoke(this, new EventArgs());
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            UpdateCategoryFilter();
            PanelReset?.Invoke(this, new EventArgs());
        }

        private void SetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e)
        {
            SetCheckboxesState(kindCheckboxes_, true);
        }

        private void ResetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e)
        {
            SetCheckboxesState(kindCheckboxes_, false);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            var settingsPath = App.GetRemarksDefinitionFilePath("utc");

            if (settingsPath == null) {
                MessageBox.Show($"Failed to setup settings file at\n{settingsPath}", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try {
                var psi = new ProcessStartInfo(settingsPath) {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to open settings file\n{ex.Message}", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetAllCategoryCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(categoryCheckboxes_, true);
        }

        private void ResetAllCategoryCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(categoryCheckboxes_, false);
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            PopulateCategoryList();
        }
    }
}
