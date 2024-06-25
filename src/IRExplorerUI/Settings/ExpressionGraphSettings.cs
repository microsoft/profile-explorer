// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using IRExplorerCore.Graph;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ExpressionGraphSettings : GraphSettings {
  public ExpressionGraphSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue("#FFFACD")]
  public Color UnaryInstructionNodeColor { get; set; }
  [ProtoMember(2)][OptionValue("#FFE4C4")]
  public Color BinaryInstructionNodeColor { get; set; }
  [ProtoMember(3)][OptionValue("#F5F5F5")]
  public Color CopyInstructionNodeColor { get; set; }
  [ProtoMember(4)][OptionValue("B6E8DE")]
  public Color PhiInstructionNodeColor { get; set; }
  [ProtoMember(5)][OptionValue("#D3F8D5")]
  public Color OperandNodeColor { get; set; }
  [ProtoMember(6)][OptionValue("#c6def1")]
  public Color NumberOperandNodeColor { get; set; }
  [ProtoMember(7)][OptionValue("#b8bedd")]
  public Color IndirectionOperandNodeColor { get; set; }
  [ProtoMember(8)][OptionValue("#D8BFD8")]
  public Color AddressOperandNodeColor { get; set; }
  [ProtoMember(9)][OptionValue("#178D1F")]
  public Color LoopPhiBackedgeColor { get; set; }
  [ProtoMember(10)][OptionValue(true)]
  public bool PrintVariableNames { get; set; }
  [ProtoMember(11)][OptionValue(true)]
  public bool PrintSSANumbers { get; set; }
  [ProtoMember(12)][OptionValue(true)]
  public bool GroupInstructions { get; set; }
  [ProtoMember(13)][OptionValue(false)]
  public bool PrintBottomUp { get; set; }
  [ProtoMember(14)][OptionValue(8)]
  public int MaxExpressionDepth { get; set; }
  [ProtoMember(15)][OptionValue(false)]
  public bool SkipCopyInstructions { get; set; }
  [ProtoMember(16)][OptionValue("#FFCAD1")]
  public Color LoadStoreInstructionNodeColor { get; set; }
  [ProtoMember(17)][OptionValue("#F0E68C")]
  public Color CallInstructionNodeColor { get; set; }

  public ExpressionGraphPrinterOptions GetGraphPrinterOptions() {
    return new ExpressionGraphPrinterOptions {
      PrintVariableNames = PrintVariableNames,
      PrintSSANumbers = PrintSSANumbers,
      GroupInstructions = GroupInstructions,
      PrintBottomUp = PrintBottomUp,
      SkipCopyInstructions = SkipCopyInstructions,
      MaxExpressionDepth = MaxExpressionDepth
    };
  }

  public override void Reset() {
    base.Reset();
    ResetAllOptions(this);
  }

  public ExpressionGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ExpressionGraphSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is ExpressionGraphSettings settings &&
           base.Equals(settings) &&
           AreOptionsEqual(this, settings);
  }

  protected override GraphSettings MakeClone() {
    return Clone();
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
