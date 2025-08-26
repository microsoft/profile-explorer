// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfileExplorer.Core.Utilities;

// Shorter way to have property notifications for data-binding.
// Based on code from https://www.danrigby.com/2012/04/01/inotifypropertychanged-the-net-4-5-way-revisited/
public class BindableObject : INotifyPropertyChanged {
  public event PropertyChangedEventHandler PropertyChanged;

  public void NotifyPropertyChanged(string propertyName) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetAndNotify<T>(ref T storage, T value,
                                 [CallerMemberName] string propertyName = null) {
    if (Equals(storage, value)) {
      return false;
    }

    storage = value;
    NotifyPropertyChanged(propertyName);
    return true;
  }

  protected void Notify(string propertyName) {
    NotifyPropertyChanged(propertyName);
  }
}