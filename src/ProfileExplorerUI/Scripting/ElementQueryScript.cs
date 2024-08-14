// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using ProfileExplorer.UI.Query;

namespace ProfileExplorer.UI.Scripting;
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