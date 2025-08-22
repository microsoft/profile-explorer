// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ProfileExplorerCore2;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.SourceParser;

namespace ProfileExplorer.UI.Profile.Document;

public class ProfileSourceSyntaxNode {
  private static readonly IconDrawing LoopIcon;
  private static readonly IconDrawing ThenIcon;
  private static readonly IconDrawing ElseIcon;
  private static readonly IconDrawing SwitchCaseIcon;
  private static readonly IconDrawing SwitchIcon;

  static ProfileSourceSyntaxNode() {
    LoopIcon = IconDrawing.FromIconResource("LoopIcon");
    ThenIcon = IconDrawing.FromIconResource("ThenArrowIcon");
    ElseIcon = IconDrawing.FromIconResource("ElseArrowIcon");
    SwitchIcon = IconDrawing.FromIconResource("SwitchArrowIcon");
    SwitchCaseIcon = IconDrawing.FromIconResource("SwitchCaseArrowIcon");
  }

  public ProfileSourceSyntaxNode(SourceSyntaxNode syntaxNode) {
    SyntaxNode = syntaxNode;
    Weight = TimeSpan.Zero;
    Start = syntaxNode.Start;
    End = syntaxNode.End;
  }

  public SourceSyntaxNode SyntaxNode { get; set; }
  public int Level { get; set; }
  public ProfileSourceSyntaxNode Parent { get; set; }
  public IRElement StartElement { get; set; }
  public List<IRElement> Elements { get; set; }
  public TimeSpan Weight { get; set; }
  public TimeSpan BodyWeight { get; set; }
  public TimeSpan ConditionWeight { get; set; }
  public PerformanceCounterValueSet Counters { get; set; }
  public bool ShowInDocumentColumns { get; set; }
  public SourceSyntaxNodeKind Kind => SyntaxNode.Kind;
  public TextLocation Start { get; set; }
  public TextLocation End { get; set; }
  public int Length => End.Offset - Start.Offset;
  public bool IsMarkedNode => SyntaxNode.Kind == SourceSyntaxNodeKind.If ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Else ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.ElseIf ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Loop ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Switch ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.SwitchCase;

  public IconDrawing GetIcon() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => LoopIcon,
      SourceSyntaxNodeKind.If         => ThenIcon,
      SourceSyntaxNodeKind.Else       => ElseIcon,
      SourceSyntaxNodeKind.ElseIf     => ElseIcon,
      SourceSyntaxNodeKind.Switch     => SwitchIcon,
      SourceSyntaxNodeKind.SwitchCase => SwitchCaseIcon,
      _                               => null
    };
  }

  public string GetTextIcon() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => "\u2B6F",
      SourceSyntaxNodeKind.If         => "\u2BA7",
      SourceSyntaxNodeKind.Else       => "\u2BA6",
      SourceSyntaxNodeKind.ElseIf     => "\u2BA7",
      SourceSyntaxNodeKind.Switch     => "\u21C9",
      SourceSyntaxNodeKind.SwitchCase => "\u2BA3",
      _                               => ""
    };
  }

  public string GetKindText() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => "Loop",
      SourceSyntaxNodeKind.If         => "If",
      SourceSyntaxNodeKind.Else       => "Else",
      SourceSyntaxNodeKind.ElseIf     => "Else If",
      SourceSyntaxNodeKind.Switch     => "Switch",
      SourceSyntaxNodeKind.SwitchCase => "Switch Case",
      SourceSyntaxNodeKind.Call       => "Call",
      _                               => ""
    };
  }

  public void SetTextStyle(ProfileMenuItem value) {
    if (Kind == SourceSyntaxNodeKind.Loop) {
      value.TextColor = Brushes.DarkGreen;
      value.TextWeight = FontWeights.Bold;
    }
    else if (Kind == SourceSyntaxNodeKind.If ||
             Kind == SourceSyntaxNodeKind.Else ||
             Kind == SourceSyntaxNodeKind.ElseIf) {
      value.TextColor = Brushes.DarkBlue;
      value.TextWeight = FontWeights.SemiBold;
    }
  }

  public string GetTooltip(FunctionProfileData funcProfile) {
    var tooltip = new StringBuilder();
    tooltip.Append($"{GetKindText()} statement");
    tooltip.Append($"\nWeight: {funcProfile.ScaleWeight(Weight).AsPercentageString()}");
    tooltip.Append($" ({Weight.AsMillisecondsString()})");

    if ((SyntaxNode.Kind == SourceSyntaxNodeKind.If ||
         SyntaxNode.Kind == SourceSyntaxNodeKind.Loop) &&
        ConditionWeight != TimeSpan.Zero &&
        BodyWeight != TimeSpan.Zero) {
      tooltip.Append($"\n    Condition: {funcProfile.ScaleWeight(ConditionWeight).AsPercentageString()}");
      tooltip.Append($" ({ConditionWeight.AsMillisecondsString()})");

      tooltip.Append($"\n    Body: {funcProfile.ScaleWeight(BodyWeight).AsPercentageString()}");
      tooltip.Append($" ({BodyWeight.AsMillisecondsString()})");
    }

    return tooltip.ToString();
  }
}