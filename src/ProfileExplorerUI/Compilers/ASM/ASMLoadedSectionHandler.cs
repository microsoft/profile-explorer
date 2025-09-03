// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Compilers.LLVM;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Compilers.Default;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Query;
using ProfileExplorerUI.Session;

namespace ProfileExplorer.UI.Compilers.ASM;

public class ASMLoadedSectionHandler : ILoadedSectionHandler {
  private ICompilerInfoProvider compilerInfo_;

  public ASMLoadedSectionHandler(ICompilerInfoProvider compilerInfo) {
    compilerInfo_ = compilerInfo;
  }

  public async Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    // Since the ASM blocks don't have a number in the text,
    // attach an overlay label next to the first instr. in the block.
    CreateBlockLabelOverlays(document, function);

    // Annotate instrs. with source line numbers if debug info is available.
    var sourceMarker = new SourceDocumentMarker(App.Settings.DocumentSettings.SourceMarkerSettings, compilerInfo_);
    await sourceMarker.Mark(document, function);
  }

  private static void CreateBlockLabelOverlays(IRDocument document, FunctionIR function) {
    double overlayHeight = document.TextArea.TextView.DefaultLineHeight;
    var options = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var blockPen = ColorPens.GetPen(options.BlockOverlayBorderColor,
                                    options.BlockOverlayBorderThickness);
    document.SuspendUpdate();

    foreach (var block in function.Blocks) {
      if (block.Tuples.Count <= 0) {
        continue;
      }

      string label = $"B{block.Number}";
      var overlay = document.AddIconElementOverlay(block, null, 0, overlayHeight, label, null,
                                                   HorizontalAlignment.Left);
      overlay.MarginX = -8;
      overlay.Padding = 4;
      overlay.ShowOnMarkerBar = false;
      overlay.IsLabelPinned = true;
      overlay.AllowLabelEditing = false;
      overlay.TextWeight = FontWeights.Bold;
      overlay.TextColor = options.BlockOverlayTextColor.AsBrush();

      var backColor = block.HasEvenIndexInFunction ?
        App.Settings.DocumentSettings.BackgroundColor :
        App.Settings.DocumentSettings.AlternateBackgroundColor;
      overlay.Background = ColorBrushes.GetBrush(backColor);
      overlay.Border = blockPen;

      overlay.ShowBackgroundOnMouseOverOnly = false;
      overlay.ShowBorderOnMouseOverOnly = false;
      overlay.UseLabelBackground = true;
    }

    document.ResumeUpdate();
  }
}