// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Profile.Spgo;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.UI.Profile.Document;

public static class SptAssemblyColumns {
  private const string IpColumnName = "SptIpSamples";
  private const string LbrColumnName = "SptLbrObservations";

  public static bool IsSptColumn(OptionalColumn column) =>
    column.ColumnName is IpColumnName or LbrColumnName;

  public static IRDocumentColumnData Build(PreparedSptProfile attachment, FunctionIR function,
                                          OptionalColumnSettings settings, CancellationToken token = default) {
    var result = new IRDocumentColumnData(function.InstructionCount);
    var heatValues = new Dictionary<OptionalColumn, List<(ElementColumnValue Cell, ulong Count, uint Rva)>>();
    var index = attachment.Samples;
    var ipColumn = index.HasIpSamples ? AddColumn(IpColumnName, "SPT IP samples") : null;
    var lbrColumn = index.HasBranches ? AddColumn(LbrColumnName, "SPT LBR observations") : null;
    var metadata = function.GetTag<AssemblyMetadataTag>();
    var instructionRvas = new Dictionary<IRElement, uint>();
    if (metadata != null) {
      foreach (var pair in metadata.AddressToElementMap) {
        long offset = unchecked(pair.Key - index.ImageBase);
        if (offset > 0 && offset < index.ImageSize && offset <= uint.MaxValue) {
          instructionRvas[pair.Value] = (uint)offset;
        }
      }
    }

    ulong matchedIp = 0, matchedLbr = 0, interiorIp = 0, interiorLbr = 0;
    foreach (var instruction in function.AllInstructions) {
      token.ThrowIfCancellationRequested();
      bool hasRva = instructionRvas.TryGetValue(instruction, out uint rva);
      if (ipColumn != null) {
        index.IpSamples.TryGetValue(rva, out var samples);
        bool matched = hasRva && samples != null;
        ulong count = matched ? samples.Count : 0;
        matchedIp = checked(matchedIp + count);
        AddValue(instruction, ipColumn, count, matched
          ? DescribeIp(samples) : EmptyCell(hasRva));
      }
      if (lbrColumn != null) {
        index.Branches.TryGetValue(rva, out var branches);
        bool matched = hasRva && branches != null;
        ulong count = matched ? branches.Count : 0;
        matchedLbr = checked(matchedLbr + count);
        AddValue(instruction, lbrColumn, count, matched
          ? DescribeBranches(branches, index) : EmptyCell(hasRva));
      }
      if (hasRva && metadata.ElementSizeMap.TryGetValue(instruction, out int size) && size > 1) {
        interiorIp = checked(interiorIp + InteriorCount(index.SortedIpRvas, rva, size,
                                                        address => index.IpSamples[address].Count));
        interiorLbr = checked(interiorLbr + InteriorCount(index.SortedBranchRvas, rva, size,
                                                         address => index.Branches[address].Count));
      }
    }
    SetHeatValues(ipColumn, matchedIp);
    SetHeatValues(lbrColumn, matchedLbr);
    SetHeader(ipColumn, matchedIp, interiorIp, index.UnmappedIpCount);
    SetHeader(lbrColumn, matchedLbr, interiorLbr, index.UnmappedBranchCount);
    return result;

    OptionalColumn AddColumn(string name, string title) {
      var column = OptionalColumn.Template($"[{name}]", "TimePercentageColumnValueTemplate", name, title,
                                           width: 110, columnStyle: "TimeColumnHeaderTemplate");
      column.Style = settings.GetColumnStyle(column) ?? OptionalColumnSettings.DefaultSptColumnStyle;
      column.IsVisible = settings.IsColumnVisible(column);
      var addedColumn = result.AddColumn(column);
      heatValues[addedColumn] = new();
      return addedColumn;
    }

    void AddValue(IRElement instruction, OptionalColumn column, ulong count, string tooltip) {
      // Keep exact unsigned counts outside the signed time/counter Value; only the share is floating-point.
      var cell = new ElementColumnValue(count == 0 ? "" : count.ToString("N0"), tooltip: tooltip) {
        CanShowBackgroundColor = count != 0,
        CanShowIcon = count != 0,
        CanShowPercentageBar = count != 0,
        TextColor = column.Style.TextColor.AsBrush(),
        TextWeight = FontWeights.Normal
      };
      result.AddValue(cell, instruction, column);
      if (count != 0) {
        heatValues[column].Add((cell, count, instructionRvas[instruction]));
      }
    }

    void SetHeatValues(OptionalColumn column, ulong total) {
      if (column == null || total == 0) {
        return;
      }
      var values = heatValues[column];
      values.Sort((a, b) => {
        int order = b.Count.CompareTo(a.Count);
        return order != 0 ? order : a.Rva.CompareTo(b.Rva);
      });
      for (int i = 0; i < values.Count; i++) {
        values[i].Cell.ValuePercentage = values[i].Count / (double)total;
        values[i].Cell.ValueOrder = i;
      }
    }

    void SetHeader(OptionalColumn column, ulong count, ulong interior, ulong outsideImage) {
      if (column == null) {
        return;
      }
      column.Tooltip = $"{column.Title} - {Path.GetFileName(attachment.Inputs[0].Path)}\n" +
                       "Click to color assembly by observation count.";
      if (interior != 0) {
        column.Tooltip += $"\nNot mapped in this function: {interior:N0}.";
      }
      if (outsideImage != 0) {
        column.Tooltip += $"\nUnknown/external sources in binary: {outsideImage:N0}.";
      }
      if (metadata == null || instructionRvas.Count == 0) {
        column.Tooltip += "\nAddress unavailable.";
      }
      else if (count == 0) {
        column.Tooltip += "\nNo observations in this function.";
      }
      // OptionalColumn formats header tooltips with string.Format.
      column.Tooltip = column.Tooltip.Replace("{", "{{").Replace("}", "}}");
    }
  }

  private static string EmptyCell(bool hasRva) => hasRva
    ? "No observations."
    : "Address unavailable.";

  private static string DescribeIp(SptIpSamples samples) {
    var parts = new List<string>();
    foreach (var pair in samples.Sources.OrderBy(pair => pair.Key == SptEventKind.InstructionPointer ? -1 : (int)pair.Key)) {
      if (pair.Value == 0) {
        continue;
      }
      string name = pair.Key switch {
        SptEventKind.InstructionPointer => "timer",
        SptEventKind.UnhaltedCycles => "unhalted-cycle",
        SptEventKind.RetiredInstructions => "retired-instruction",
        SptEventKind.RetiredBranches => "retired-branch",
        SptEventKind.L1DataCacheMisses => "L1 data-cache-miss",
        SptEventKind.L1InstructionCacheMisses => "L1 instruction-cache-miss",
        _ => throw new InvalidOperationException($"Unexpected SPT IP event kind: {pair.Key}.")
      };
      parts.Add($"{pair.Value:N0} {name}");
    }
    return $"Samples: {string.Join(", ", parts)}";
  }

  private static string DescribeBranches(SptBranchSamples samples, SptSampleIndex index) {
    var parts = new List<string>();
    var destinations = samples.Destinations.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToList();
    ulong shown = 0;
    foreach (var pair in destinations.Take(10)) {
      string target = pair.Key == 0 ? "unknown/external" :
        $"0x{pair.Key:X}" + (index.IsImageRva(pair.Key) ? "" : " external");
      parts.Add($"{target} ({pair.Value:N0})");
      shown = checked(shown + pair.Value);
    }
    if (destinations.Count > 10) {
      parts.Add($"+{destinations.Count - 10:N0} targets ({samples.Count - shown:N0})");
    }
    return $"Targets: {string.Join(", ", parts)}";
  }

  private static ulong InteriorCount(IReadOnlyList<uint> addresses, uint start, int size, Func<uint, ulong> count) {
    int lo = 0, hi = addresses.Count;
    while (lo < hi) {
      int mid = lo + (hi - lo) / 2;
      if (addresses[mid] <= start) lo = mid + 1;
      else hi = mid;
    }
    ulong total = 0;
    long end = (long)start + size;
    for (int i = lo; i < addresses.Count && addresses[i] < end; i++) {
      total = checked(total + count(addresses[i]));
    }
    return total;
  }
}
