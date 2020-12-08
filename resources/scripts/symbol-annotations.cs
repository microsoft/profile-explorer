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

        [DisplayName("Use icon markers")]
        [Description("Attach icons to flagged elements")]
        public bool UseIconMarkers { get; set; }
        
        [DisplayName("Use color markers")]
        [Description("Highlight flagged elements")]
        public bool UseColorMarkers { get; set; }

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
            UseIconMarkers = true;
            UseColorMarkers = false;
            VolatileMarkerColor = Colors.Pink;
            WriteThroughMarkerColor = Colors.Gold;
            CantMakeSDSUMarkerColor = Colors.PaleGreen;
        }
    }

    public FunctionTaskInfo GetTaskInfo() {
        return new FunctionTaskInfo(Guid.Parse("88190F36-562E-44A3-8364-815FB236D4AD"),
                                    "Symbol annotation marking", "Some description") {
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
				if(options.UseColorMarkers) {
					s.Mark(element, options.VolatileMarkerColor);
                }

                if(options.UseIconMarkers) {
                    s.AddWarningIcon(element, "Volatile");
                }
            }
            
            if(tag.HasWritethrough && options.MarkWriteThrough) {
				if(options.UseColorMarkers) {
					s.Mark(element, options.WriteThroughMarkerColor);
				}

                if(options.UseIconMarkers) {
                    s.AddWarningIcon(element, "Write-through SEH");
                }
            }
            
            if(tag.HasCantMakeSDSU && options.MarkCantMakeSDSU) {
				if(options.UseColorMarkers) {
					s.Mark(element, options.CantMakeSDSUMarkerColor);
				}

                if(options.UseIconMarkers) {
                    s.AddWarningIcon(element, "Can't make SDSU");
                }
            }
        }

        return true;
    }
}
