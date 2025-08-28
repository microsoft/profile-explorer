// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ProfileExplorer.Core.Profile.Timeline;

public record SampleTimeRangeInfo(
  TimeSpan StartTime,
  TimeSpan EndTime,
  int StartSampleIndex,
  int EndSampleIndex,
  int ThreadId);

public record struct SampleTimePointInfo(TimeSpan Time, int SampleIndex, int ThreadId);

//? TODO: Use SampleIndex in SampleTimePointInfo/Range
public record struct SampleIndex(int Index, TimeSpan Time);