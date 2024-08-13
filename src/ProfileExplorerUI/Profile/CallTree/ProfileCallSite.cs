// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProfileExplorer.UI.Profile;

public class ProfileCallSite : IEquatable<ProfileCallSite> {
  public ProfileCallSite(long rva) {
    Targets = new List<(ProfileCallTreeNode NodeId, TimeSpan Weight)>();
    RVA = rva;
    Weight = TimeSpan.Zero;
  }

  public long RVA { get; set; }
  public TimeSpan Weight { get; set; }
  //? TODO: Consider using TinyList
  public List<(ProfileCallTreeNode Node, TimeSpan Weight)> Targets { get; set; }

  public List<(ProfileCallTreeNode Node, TimeSpan Weight)> SortedTargets {
    get {
      if (!HasSingleTarget) {
        Targets.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      }

      return Targets;
    }
  }

  public bool HasSingleTarget => Targets.Count == 1;

  public static bool operator ==(ProfileCallSite left, ProfileCallSite right) {
    return Equals(left, right);
  }

  public static bool operator !=(ProfileCallSite left, ProfileCallSite right) {
    return !Equals(left, right);
  }

  public double ScaleWeight(TimeSpan weight) {
    return weight.Ticks / (double)Weight.Ticks;
  }

  public void AddTarget(ProfileCallTreeNode node, TimeSpan weight) {
    Weight += weight; // Total weight of targets.
    int index = -1;

    // Don't use FindIndex because it allocates a lambda on each invocation.
    for (int i = 0; i < Targets.Count; i++) {
      if (Targets[i].Node.Equals(node.Function)) {
        index = i;
        break;
      }
    }

    if (index != -1) {
      var span = CollectionsMarshal.AsSpan(Targets);
      span[index].Weight += weight; // Modify in-place per-target weight.
    }
    else {
      Targets.Add((node, weight));
    }
  }

  public void MergeWith(ProfileCallSite otherCallSite) {
    Weight += otherCallSite.Weight;

    foreach (var target in otherCallSite.Targets) {
      AddTarget(target.Node, target.Weight);
    }
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
      return false;
    }

    return Equals((ProfileCallSite)obj);
  }

  public override int GetHashCode() {
    return RVA.GetHashCode();
  }

  public bool Equals(ProfileCallSite other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return RVA == other.RVA;
  }

  public override string ToString() {
    return $"RVA: {RVA}, Weight: {Weight.TotalMilliseconds}, Targets: {Targets.Count}";
  }
}