// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;

namespace PDBViewer;

partial class AboutBox : Form {
  public AboutBox() {
    InitializeComponent();
    labelProductName.Text = AssemblyProduct;
    labelVersion.Text = string.Format("Version {0}", AssemblyVersion);
    labelCopyright.Text = AssemblyCopyright;
  }

  public string AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

  public string AssemblyProduct {
    get {
      object[] attributes =
        Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);

      if (attributes.Length == 0) {
        return "";
      }

      return ((AssemblyProductAttribute)attributes[0]).Product;
    }
  }

  public string AssemblyCopyright {
    get {
      object[] attributes = Assembly.GetExecutingAssembly().
        GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);

      if (attributes.Length == 0) {
        return "";
      }

      return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
    }
  }
}