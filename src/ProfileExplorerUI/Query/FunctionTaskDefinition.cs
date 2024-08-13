// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;

namespace ProfileExplorer.UI.Query;

public class FunctionTaskInfo {
  public FunctionTaskInfo(Guid id, string name, string description = "") {
    Name = name;
    Id = id;
    Description = description;
  }

  public Guid Id { get; set; }
  public string Name { get; set; }
  public string Description { get; set; }
  public bool HasOptionsPanel { get; set; }
  public Type OptionsType { get; set; }
  public string TargetCompilerIR { get; set; }

  public override bool Equals(object obj) {
    return obj is FunctionTaskInfo info &&
           Id.Equals(info.Id);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Id);
  }
}

public class FunctionTaskDefinition {
  private FunctionTaskInfo taskInfo_;
  private Type taskType_;
  private object optionalData_;

  public FunctionTaskDefinition(Type taskType, object optionalData = null) {
    taskType_ = taskType;
    optionalData_ = optionalData;
  }

  public FunctionTaskDefinition(Type taskType, FunctionTaskInfo taskInfo,
                                object optionalData = null) : this(taskType, optionalData) {
    taskInfo_ = taskInfo;
  }

  public FunctionTaskInfo TaskInfo => taskInfo_;

  public bool IsCompatibleWith(string compilerIR) {
    return string.IsNullOrEmpty(taskInfo_.TargetCompilerIR) ||
           taskInfo_.TargetCompilerIR == compilerIR;
  }

  public IFunctionTask CreateInstance(ISession session) {
    var actionInstance = (IFunctionTask)Activator.CreateInstance(taskType_);

    if (!actionInstance.Initialize(session, taskInfo_, optionalData_)) {
      return null;
    }

    return actionInstance;
  }
}