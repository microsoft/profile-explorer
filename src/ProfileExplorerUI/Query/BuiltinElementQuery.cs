// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.UI.Query;

public class BuiltinElementQuery : IElementQuery {
  private IUISession session_;
  public IUISession Session => session_;

  public bool Initialize(IUISession session) {
    session_ = session;
    return true;
  }

  public bool Execute(QueryData data) {
    //MessageBox.Show("Execute BuiltinElementQuery");
    data.SetOutput("Output A", new Random().NextDouble() > 0.5);
    data.SetOutput("Output B", new Random().NextDouble() > 0.5);
    data.SetOutput("Output C", new Random().Next(10000));
    return true;
  }

  public QueryDefinition GetDefinition() {
    throw new NotImplementedException();
  }
}