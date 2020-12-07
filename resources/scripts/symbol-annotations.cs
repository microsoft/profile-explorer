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
        [DisplayName("Mark volatile")]
        [Description("Mark operands having the volatile flag set")]
        public bool MarkVolatile { get; set; }

        [DisplayName("Mark write-through")]
        [Description("Mark operands having the write-through flag set")]
        public bool MarkWriteThrough { get; set; }

        [DisplayName("Mark \"Can't make SDSU\"")]
        [Description("Mark operands having the \"Can't make SDSU\" flag set")]
        public bool MarkCantMakeSDSU { get; set; }

        [DisplayName("Volatile marker color")]
        public Color VolatileMarkerColor { get; set; }

        [DisplayName("Write-through marker color")]
        public Color WriteThroughMarkerColor { get; set; }

        [DisplayName("\"Can't make SDSU\"marker color")]
        public Color CantMakeSDSUMarkerColor { get; set; }

        public Options() {
            Reset();
        }

        public void Reset() {
            MarkVolatile = true;
            MarkWriteThrough = true;
            MarkCantMakeSDSU = true;
            VolatileMarkerColor = Colors.Pink;
            WriteThroughMarkerColor = Colors.Gold;
            CantMakeSDSUMarkerColor = Colors.PaleGreen;
        }
    }

    public FunctionTaskInfo GetTaskInfo() {
        return new FunctionTaskInfo("Symbol annotation marking", "Some description") {
            HasOptionsPanel = true,
            OptionsType = typeof(Options)
        };
    }

    public bool Execute(ScriptSession s) {
        var func = s.CurrentFunction;
        var options = (Options)s.SessionObject;

        foreach(var element in func.AllElements) {
            var tag = element.GetTag<SymbolAnnotationTag>();
            if (tag == null) continue;

            if(tag.HasVolatile && options.MarkVolatile) {
                s.Mark(element, options.VolatileMarkerColor);
            }
            
            if(tag.HasWritethrough && options.MarkWriteThrough) {
                s.Mark(element, options.WriteThroughMarkerColor);
            }
            
            if(tag.HasCantMakeSDSU && options.MarkCantMakeSDSU) {
                s.Mark(element, options.CantMakeSDSUMarkerColor);
            }
        }

        return true;
    }
}
