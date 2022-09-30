﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
using IRExplorerUI.Controls;

namespace IRExplorerUI.Profile {
    public class ProfileListViewItem : SearchableProfileItem {
        private bool prependModule_;
        public ProfileCallTreeNode CallTreeNode { get; set; }

        protected override bool ShouldPrependModule() {
            return prependModule_ && App.Settings.CallTreeSettings.PrependModuleToFunction;
        }

        public static ProfileListViewItem From(ProfileCallTreeNode node, ProfileData profileData) {
            return new ProfileListViewItem() {
                prependModule_ = true,
                CallTreeNode = node,
                FunctionName = node.FunctionName,
                ModuleName = node.ModuleName,
                Weight = node.Weight,
                ExclusiveWeight = node.ExclusiveWeight,
                Percentage = profileData.ScaleFunctionWeight(node.Weight),
                ExclusivePercentage = profileData.ScaleFunctionWeight(node.ExclusiveWeight),
            };
        }

        public static ProfileListViewItem From(ModuleProfileInfo node, ProfileData profileData) {
            return new ProfileListViewItem() {
                FunctionName = node.Name,
                Weight = node.Weight,
                Percentage = node.Percentage,
            };
        }
    }

    public partial class ProfileListView : UserControl, INotifyPropertyChanged {

        public ProfileListView() {
            InitializeComponent();
            DataContext = this;
        }


        public event PropertyChangedEventHandler PropertyChanged;

        public ISession Session { get; set; }

        private string nameColumnTitle_;
        public string NameColumnTitle {
            get => nameColumnTitle_;
            set => SetField(ref nameColumnTitle_, value);
        }


        private string timeColumnTitle_;
        public string TimeColumnTitle {
            get => timeColumnTitle_;
            set => SetField(ref timeColumnTitle_, value);
        }

        private string exclusiveTimeColumnTitle_;
        public string ExclusiveTimeColumnTitle {
            get => exclusiveTimeColumnTitle_;
            set => SetField(ref exclusiveTimeColumnTitle_, value);
        }

        private bool showExclusiveTimeColumn_;
        public bool ShowExclusiveTimeColumn {
            get => showExclusiveTimeColumn_;
            set => SetField(ref showExclusiveTimeColumn_, value);
        }

        public void Show(List<ProfileCallTreeNode> nodes) {
            var list = new List<ProfileListViewItem>(nodes.Count);
            nodes.ForEach(node => list.Add(ProfileListViewItem.From(node, Session.ProfileData)));
            ItemList.ItemsSource = new ListCollectionView(list);
        }
        public void Show(List<ModuleProfileInfo> nodes) {
            var list = new List<ProfileListViewItem>(nodes.Count);
            nodes.ForEach(node => list.Add(ProfileListViewItem.From(node, Session.ProfileData)));
            ItemList.ItemsSource = new ListCollectionView(list);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}