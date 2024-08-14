// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core;

public class IRTextSummary : IEquatable<IRTextSummary> {
  private Dictionary<string, IRTextFunction> functionNameMap_;
  private Dictionary<string, IRTextFunction> unmangledFunctionNameMap_;
  private Dictionary<int, IRTextFunction> functionMap_;
  private Dictionary<int, IRTextSection> sectionMap_;
  private int hashCode_;

  public IRTextSummary(string moduleName = null) {
    SetModuleName(moduleName);
    Functions = new List<IRTextFunction>();
    functionNameMap_ = new Dictionary<string, IRTextFunction>();
    functionMap_ = new Dictionary<int, IRTextFunction>();
    sectionMap_ = new Dictionary<int, IRTextSection>();
  }

  public Guid Id { get; set; }
  public string ModuleName { get; private set; }
  public List<IRTextFunction> Functions { get; set; }

  public void SetModuleName(string moduleName) {
    ModuleName = moduleName != null ? string.Intern(moduleName) : null;
  }

  public void AddFunction(IRTextFunction function) {
    if (functionNameMap_.ContainsKey(function.Name)) {
      return; //? remove
    }

    function.Number = Functions.Count;
    Functions.Add(function);
    functionNameMap_.Add(function.Name, function);
    functionMap_.Add(function.Number, function);
    function.ParentSummary = this;
  }

  public void AddSection(IRTextSection section) {
    section.Id = sectionMap_.Count;
    sectionMap_.Add(sectionMap_.Count, section);
  }

  public IRTextSection GetSectionWithId(int id) {
    return sectionMap_.TryGetValue(id, out var value) ? value : null;
  }

  public IRTextFunction GetFunctionWithId(int id) {
    return functionMap_.TryGetValue(id, out var result) ? result : null;
  }

  public IRTextFunction FindFunction(string name) {
    return functionNameMap_.TryGetValue(name, out var result) ? result : null;
  }

  public IRTextFunction FindFunction(string name, Func<string, string> matchCheck) {
    if (unmangledFunctionNameMap_ == null) {
      ComputeUnmangledFunctionNameMap(matchCheck);
    }

    return unmangledFunctionNameMap_.GetValueOrNull(name);
  }

  public void ComputeUnmangledFunctionNameMap(Func<string, string> funcNameFormatter) {
    lock (this) {
      if (unmangledFunctionNameMap_ != null) {
        return;
      }

      unmangledFunctionNameMap_ = new Dictionary<string, IRTextFunction>();

      foreach (var function in Functions) {
        string unmangledName = funcNameFormatter(function.Name);
        unmangledFunctionNameMap_[unmangledName] = function;
      }
    }
  }

  public List<IRTextFunction> FindFunctions(Func<string, bool> matchCheck) {
    var list = new List<IRTextFunction>();

    foreach (var function in Functions) {
      if (matchCheck(function.Name)) {
        list.Add(function);
      }
    }

    return list;
  }

  public List<IRTextFunction> FindAllFunctions(string nameSubstring) {
    return Functions.FindAll(func => func.Name.Contains(nameSubstring, StringComparison.Ordinal));
  }

  public List<IRTextFunction> FindAllFunctions(string[] nameSubstrings) {
    return Functions.FindAll(func => {
      foreach (string name in nameSubstrings) {
        if (!func.Name.Contains(name, StringComparison.Ordinal)) {
          return false;
        }
      }

      return true;
    });
  }

  public IRTextFunction FindFunction(IRTextFunction function) {
    return functionNameMap_.TryGetValue(function.Name, out var result) ? result : null;
  }

  public override int GetHashCode() {
    return (ModuleName != null ? ModuleName.GetHashCode() : 0);
  }

  public override string ToString() {
    var sb = new StringBuilder();
    sb.AppendLine($"Summary: {ModuleName}");
    sb.AppendLine($"Functions: {Functions.Count}");
    return sb.ToString();
  }

  public bool Equals(IRTextSummary other) {
    if (ReferenceEquals(null, other)) return false;
    if (ReferenceEquals(this, other)) return true;
    return ModuleName.Equals(other.ModuleName, StringComparison.Ordinal);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) return false;
    if (ReferenceEquals(this, obj)) return true;
    if (obj.GetType() != this.GetType()) return false;
    return Equals((IRTextSummary)obj);
  }
}