﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using IRExplorerUI.Query;

namespace IRExplorerUI.Scripting;
// Two parts: a static? method that returns a QueryData,
// having the required input/output fields defined
// The actual "execute" method that gets the QueryData,
// runs the script and populates the output values

public class ElementQueryScript : Script, IElementQuery {
  private ISession session_;

  public ElementQueryScript(string code) : base(code) {
  }

  public QueryDefinition Query { get; set; }
  public ISession Session => session_;

  public QueryDefinition GetDefinition() {
    throw new NotImplementedException();
  }

  public bool Initialize(ISession session) {
    session_ = session;
    return true;
  }

  public bool Execute(QueryData data) {
    MessageBox.Show("Execute ElementQueryScript");
    return true;
  }

  //public override bool Execute(ScriptSession session) {
  //    return base.Execute(session);
  //}
}
