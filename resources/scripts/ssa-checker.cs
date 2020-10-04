using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.Analysis;
using IRExplorerCore.UTC;
using IRExplorerUI.Query;
using IRExplorerUI;
using IRExplorerUI.Scripting;
using System.Collections.Generic;
using System;
using System.Windows.Media;
using System.ComponentModel;

public class Script {
    class Options : IFunctionTaskOptions {
        [DisplayName("Check value definition dominance")]
        [Description("Checks that the definition of each source value dominates the instruction")]
        public bool CheckDominance { get; set; }

        [DisplayName("Check live range overlap")]
        [Description("Checks that there is no live range overlap of multiple SSA values of the same symbol")]
        public bool CheckLiveRangeOverlap { get; set; }

        [DisplayName("Error marker color")]
        [Description("Color to be used for marking potention errors")]
        public Color MarkerColor { get; set; }

        public Options() {
            Reset();
        }

        public void Reset() {
            CheckDominance = true;
            CheckLiveRangeOverlap = true;
            MarkerColor = Colors.Pink;
        }
    }

    public FunctionTaskInfo GetTaskInfo() {
        return new FunctionTaskInfo("SSA form checks", "Some description") {
            HasOptionsPanel = true,
            OptionsType = typeof(Options)
        };
    }

    public bool Execute(ScriptSession s) {
    	var func = s.CurrentFunction;

        s.WriteLine($"Before cast has object {s.SessionObject != null}");

        var options = (Options)s.SessionObject;
    	
        s.WriteLine("After cast");

    	foreach(var instr in func.AllInstructions) {
    		if(instr.Sources.Count == 1) {
    			s.Mark(instr, options.MarkerColor);	
    		}
    	}

        s.SetSessionResult(true, "All fine");

        return true;
    }
}
