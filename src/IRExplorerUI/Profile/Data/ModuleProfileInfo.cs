using System;
using System.Collections.Generic;

namespace IRExplorerUI.Profile;

public class ModuleProfileInfo {
  public ModuleProfileInfo() { }

  public ModuleProfileInfo(string name) {
    Name = name;
    Functions = new List<ProfileCallTreeNode>();
  }

  public string Name { get; set; }
  public double Percentage { get; set; }
  public TimeSpan Weight { get; set; }
  public List<ProfileCallTreeNode> Functions { get; set; }
}
