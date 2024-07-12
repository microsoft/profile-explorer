// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class GeneralSettings : SettingsBase {
  private static int MinCpuCoreLimit = 1;
  private static int MaxCpuCoreLimit = 16;
  private static readonly double MinZoomAmount = 0.5;
  private static readonly double MaxZoomAmount = 2;
  private static readonly double ZoomStep = 0.05;
  public static readonly double DefaultCpuCoreLimit = 0.75;

  public GeneralSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(false)]
  public bool DisableHardwareRendering { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool CheckForUpdates { get; set; }
  [ProtoMember(3)][OptionValue(1.0)]
  public double WindowScaling { get; set; }
  [ProtoMember(4)][OptionValue(0)]
  public int ThemeIndex { get; set; }
  [ProtoMember(5)][OptionValue(0.75)]
  public double CpuCoreLimit { get; set; }

  public int CurrentCpuCoreLimit => (int)Math.Clamp(Math.Floor(CpuCoreLimit * Environment.ProcessorCount),
                                                    MinCpuCoreLimit, MaxCpuCoreLimit);

  public override void Reset() {
    ResetAllOptions(this);
  }

  public double ZoomInWindow() {
    WindowScaling = Math.Clamp(WindowScaling + ZoomStep, MinZoomAmount, MaxZoomAmount);
    return WindowScaling;
  }

  public double ZoomOutWindow() {
    WindowScaling = Math.Clamp(WindowScaling - ZoomStep, MinZoomAmount, MaxZoomAmount);
    return WindowScaling;
  }

  public double ResetWindowZoom() {
    WindowScaling = 1.0;
    return WindowScaling;
  }

  public GeneralSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<GeneralSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}