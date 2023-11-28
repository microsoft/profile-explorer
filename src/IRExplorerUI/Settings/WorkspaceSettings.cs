// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.ASM;
using ProtoBuf;

namespace IRExplorerUI.Settings;

[ProtoContract(SkipConstructor = true)]
public class Workspace : IEquatable<Workspace> {
  public bool Equals(Workspace other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Name == other.Name ||
           Utils.TryGetFileName(FilePath) == Utils.TryGetFileName(other.FilePath);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != GetType())
      return false;
    return Equals((Workspace)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Name, FilePath);
  }

  public static bool operator ==(Workspace left, Workspace right) {
    return Equals(left, right);
  }

  public static bool operator !=(Workspace left, Workspace right) {
    return !Equals(left, right);
  }

  [ProtoMember(1)]
  public string Name { get; set; }
  [ProtoMember(2)]
  public string Description { get; set; }
  [ProtoMember(3)]
  public string FilePath { get; set; }
  [ProtoMember(4)]
  public int Order { get; set; }
  public Workspace() { }

  public override string ToString() {
    return Name;
  }
}

[ProtoContract(SkipConstructor = true)]
public class WorkspaceSettings {
  private static readonly string SettingsFileName = "settings.proto";
  [ProtoMember(1)]
  public List<Workspace> Workspaces { get; set; }
  [ProtoMember(2)]
  public Workspace ActiveWorkspace { get; set; }
  [ProtoMember(3)]
  public Dictionary<string, Workspace> CompilerDefaultWorkspace { get; set; }

  public WorkspaceSettings() {
    InitializeReferenceMembers();
    RestoreDefaultWorkspaces();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    Workspaces ??= new List<Workspace>();
    CompilerDefaultWorkspace ??= new Dictionary<string, Workspace>();

    // Sync settings with files on disk.
    CleanupWorkspaces();
    LoadFromDirectory(App.GetWorkspacesPath());
  }

  public string GetBuiltinWorkspaceName(string compiler) {
    return compiler switch {
      "ASM" => "Profiling",
      _ => "Profiling"
    };
  }

  public bool RestoreDefaultWorkspaces() {
    if (!LoadFromDirectory(App.GetInternlWorkspacesPath())) {
      return false;
    }

    string wsName = GetBuiltinWorkspaceName("");
    ActiveWorkspace = Workspaces.FirstOrDefault(w => w.Name == wsName);
    SortWorkspaces();
    RenumberWorkspaces();
    return true;
  }

  public bool RestoreDefaultActiveWorkspace() {
    string wsName = GetBuiltinWorkspaceName(App.Settings.DefaultCompilerIR);
    var defaultWs = Workspaces.FirstOrDefault(w => w.Name == wsName);

    if (ActiveWorkspace == null || ActiveWorkspace != defaultWs) {
      if (defaultWs == null) {
        defaultWs = CreateWorkspace("Default");
      }
      
      ActiveWorkspace = defaultWs;
      return true;
    }

    return false;
  }

  public bool RenumberWorkspaces() {
    int order = 0;

    foreach (var ws in Workspaces) {
      ws.Order = order++;
    }

    return true;
  }

  public Workspace CreateWorkspace(string name) {
    var existing = Workspaces.FirstOrDefault(w => w.Name == name);
    
    if (existing != null) {
      return existing;
    }
    
    string fileName = $"{Guid.NewGuid()}.xml";
    var ws = new Workspace {
      Name = name,
      FilePath = Path.Combine(App.GetWorkspacesPath(), fileName),
      Order = Workspaces.Count
    };

    Workspaces.Add(ws);
    return ws;
  }

  public void RemoveWorkspace(Workspace ws) {
    if (Workspaces.Remove(ws)) {
      if (ws == ActiveWorkspace) {
        ActiveWorkspace = null;

        if (Workspaces.Count > 0) {
          ActiveWorkspace = Workspaces[0];
        }
      }

      try {
        File.Delete(ws.FilePath);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to remove workspace file {ws.FilePath}", ex.Message);
      }

      RenumberWorkspaces();
    }
  }

  private void CleanupWorkspaces() {
    // Remove any workspaces that don't have a file.
    try {
      if (Workspaces.RemoveAll(w => !File.Exists(w.FilePath)) > 0) {
        RenumberWorkspaces();
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to cleanup workspaces: {ex.Message}");
    }
  }

  public bool HasWorkspace(string name) {
    return Workspaces.Any(w => w.Name == name);
  }

  private void SortWorkspaces() {
    Workspaces = Workspaces.OrderBy(w => w.Order).ToList();
  }

  private bool LoadFromDirectory(string path) {
    try {
      string workspacesPath = App.GetWorkspacesPath();
      App.CreateDirectories(workspacesPath);
      string[] files = Directory.GetFiles(path, "*.xml");
      int order = Workspaces.Count;

      foreach (string file in files) {
        var ws = new Workspace {
          FilePath = file,
          Name = Path.GetFileNameWithoutExtension(file)
        };

        if (!Workspaces.Contains(ws)) {
          ws.Order = order++;
          Workspaces.Add(ws);

          string destFile = Path.Combine(workspacesPath, Path.GetFileName(file));
          File.Copy(file, destFile, true);
        }
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to load workspaces from directory {ex.Message}");
      return false;
    }
  }

  private bool CopyToDirectory(string path) {
    try {
      foreach (var ws in Workspaces) {
        string filePath = Path.Combine(path, Path.GetFileName(ws.FilePath));
        File.Copy(ws.FilePath, filePath, true);
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to save workspaces to directory {ex.Message}");
      return false;
    }
  }

  private bool SerializeWorkspaceSettings(string filePath) {
    try {
      using var file = File.Create(filePath);
      Serializer.Serialize(file, this);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to save workspace settings {ex.Message}");
      return false;
    }
  }

  private static WorkspaceSettings DeserializeWorkspaceSettings(string filePath) {
    try {
      using var file = File.OpenRead(filePath);
      return Serializer.Deserialize<WorkspaceSettings>(file);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to load workspace settings {ex.Message}");
      return null;
    }
  }

  public static WorkspaceSettings LoadFromArchive(string filePath) {
    try {
      var tempPath = Directory.CreateTempSubdirectory("irx");
      ZipFile.ExtractToDirectory(filePath, tempPath.FullName, true);
      var settings = DeserializeWorkspaceSettings(Path.Combine(tempPath.FullName, SettingsFileName));
      settings.LoadFromDirectory(tempPath.FullName);
      return settings;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to load workspaces from Zip {ex.Message}");
      return null;
    }
  }

  public bool SaveToArchive(string filePath) {
    try {
      var tempPath = Directory.CreateTempSubdirectory("irx");
      CopyToDirectory(tempPath.FullName);
      SerializeWorkspaceSettings(Path.Combine(tempPath.FullName, SettingsFileName));

      if (File.Exists(filePath)) {
        File.Delete(filePath);
      }

      ZipFile.CreateFromDirectory(tempPath.FullName, filePath, CompressionLevel.Optimal, false);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to save workspaces to Zip {ex.Message}");
      return false;
    }
  }
}
