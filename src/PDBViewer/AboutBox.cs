// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PDBViewer;
partial class AboutBox : Form {
  public AboutBox() {
    InitializeComponent();
    this.labelProductName.Text = AssemblyProduct;
    this.labelVersion.Text = String.Format("Version {0}", AssemblyVersion);
    this.labelCopyright.Text = AssemblyCopyright;
  }

  public string AssemblyVersion {
    get {
      return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }
  }

  public string AssemblyProduct {
    get {
      object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
      if (attributes.Length == 0) {
        return "";
      }
      return ((AssemblyProductAttribute)attributes[0]).Product;
    }
  }

  public string AssemblyCopyright {
    get {
      object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
      if (attributes.Length == 0) {
        return "";
      }
      return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
    }
  }
}
