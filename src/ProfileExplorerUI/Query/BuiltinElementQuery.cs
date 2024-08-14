// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.UI.Query;

public class BuiltinElementQuery : IElementQuery {
  private ISession session_;
  public ISession Session => session_;

  public QueryDefinition GetDefinition() {
    throw new NotImplementedException();
  }

  public bool Initialize(ISession session) {
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
}