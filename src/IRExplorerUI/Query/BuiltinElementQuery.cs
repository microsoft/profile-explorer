﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public class BuiltinElementQuery : IElementQuery {
        public ElementQueryDefinition GetDefinition() {
            throw new NotImplementedException();
        }

        public bool Execute(QueryData data) {
            //MessageBox.Show("Execute BuiltinElementQuery");
            data.SetOutput("Output A", new Random().NextDouble() > 0.5);
            data.SetOutput("Output B", new Random().NextDouble() > 0.5);
            data.SetOutput("Output C", new Random().Next(10000));
            return true;
        }
    }
}