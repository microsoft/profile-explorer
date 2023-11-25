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
    return Name == other.Name && FilePath == other.FilePath;
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
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
    return $"{Name}, order {Order}, file {FilePath}";
  }
}

[ProtoContract(SkipConstructor = true)]
public class WorkspaceSettings {
  [ProtoMember(1)]
  public List<Workspace> Workspaces { get; set; }
  [ProtoMember(2)]
  public Workspace DefaultWorkspace { get; set; }
  [ProtoMember(3)]
  public Dictionary<string, Workspace> CompilerDefaultWorkspace { get; set; }

  public List<Workspace> SordedWorkspaces => Workspaces.OrderBy(w => w.Order).ToList();

  public WorkspaceSettings() {
    InitializeReferenceMembers();
    RestoreDefault();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    Workspaces ??= new List<Workspace>();
    CompilerDefaultWorkspace ??= new Dictionary<string, Workspace>();
  }

  public string GetBuiltinWorkspaceName(string compiler) {
    return compiler switch { 
      "ASM" => "Profiling",
      _ => "Profiling"
    };
  }

  public bool RestoreDefault() {
    if (!LoadFromDirectory(App.GetInternlWorkspacesPath())) {
      return false;
    }

    var wsName = GetBuiltinWorkspaceName(App.Settings.DefaultCompilerIR);
    DefaultWorkspace = Workspaces.FirstOrDefault(w => w.Name == wsName);
    return true;
  }

  private bool LoadFromDirectory(string path) {
    try {
      var workspacesPath = App.GetWorkspacesPath();
      App.CreateDirectories(workspacesPath);
      var files = Directory.GetFiles(path, "*.xml");
      int order = Workspaces.Count;

      foreach (var file in files) {
        var ws = new Workspace {
          FilePath = file,
          Name = Path.GetFileNameWithoutExtension(file)
        };

        if (!Workspaces.Contains(ws)) {
          ws.Order = order++; 
          Workspaces.Add(ws);
          File.Copy(file, Path.Combine(workspacesPath, Path.GetFileName(file)));
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
        var filePath = Path.Combine(path, Path.GetFileName(ws.FilePath));
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
      var settings = DeserializeWorkspaceSettings(Path.Combine(tempPath.FullName, "settings.proto"));
      settings.LoadFromDirectory(tempPath.FullName);

      //? Open settings file and clone it

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
      SerializeWorkspaceSettings(Path.Combine(tempPath.FullName, "settings.proto"));
      ZipFile.CreateFromDirectory(tempPath.FullName, filePath, CompressionLevel.Optimal, true);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to save workspaces to Zip {ex.Message}");
      return false;
    }
  }
}
