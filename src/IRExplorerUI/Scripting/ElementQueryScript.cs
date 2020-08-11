﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using IRExplorerCore.IR;
using IRExplorerUI.Query;

namespace IRExplorerUI.Scripting {
    // Two parts: a static? method that returns a QueryData,
    // having the required input/output fields defined
    // The actual "execute" method that gets the QueryData,
    // runs the script and populates the output values

    public class ElementQueryScript : Script, IElementQuery {
        public ElementQueryScript(string code) : base(code) {

        }

        public ElementQueryDefinition Query { get; set; }

        public ElementQueryDefinition GetDefinition() {
            throw new NotImplementedException();
        }

        public bool Execute(QueryData data) {
            MessageBox.Show("Execute ElementQueryScript");
            return true;
        }

        public override bool Execute(ScriptSession session) {
            return base.Execute(session);
        }
    }


}