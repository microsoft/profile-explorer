// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProfileExplorer.UI.Profile;

public class IpToImageCache {
  private List<ProfileImage> images_;
  private long lowestBaseAddress_;

  public IpToImageCache(IEnumerable<ProfileImage> images, long lowestBaseAddress) {
    lowestBaseAddress_ = lowestBaseAddress;
    images_ = new List<ProfileImage>(images);
    images_.Sort();
  }

  public static IpToImageCache Create(IEnumerable<ProfileImage> images) {
    long lowestAddr = long.MaxValue;

    foreach (var image in images) {
      lowestAddr = Math.Min(lowestAddr, image.BaseAddress);
    }

    return new IpToImageCache(images, lowestAddr);
  }

  public bool IsValidAddres(long ip) {
    return ip >= lowestBaseAddress_;
  }

  public ProfileImage Find(long ip) {
    Debug.Assert(IsValidAddres(ip));
    return BinarySearch(images_, ip);
  }

  private ProfileImage BinarySearch(List<ProfileImage> ranges, long value) {
    int min = 0;
    int max = ranges.Count - 1;

    while (min <= max) {
      int mid = (min + max) / 2;
      var range = ranges[mid];
      int comparison = range.CompareTo(value);

      if (comparison == 0) {
        return range;
      }

      if (comparison < 0) {
        min = mid + 1;
      }
      else {
        max = mid - 1;
      }
    }

    return null;
  }
}