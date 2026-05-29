// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Resolves instruction pointers to module/function pairs using registered images and debug info.
/// Shared infrastructure used by SampleAggregator and CounterAggregator.
/// </summary>
internal class IpResolver {
  private readonly SortedList<long, ImageInfo> imagesByBaseAddress_ = [];
  private readonly Dictionary<string, List<FunctionDebugInfo>> sortedFunctionsByModule_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly ManagedMethodResolver? managedResolver_;

  public IpResolver(ManagedMethodResolver? managedResolver = null) {
    managedResolver_ = managedResolver;
  }

  /// <summary>
  /// Register a loaded image with its base address.
  /// </summary>
  public void AddImage(string imageName, long baseAddress, int size) {
    imagesByBaseAddress_[baseAddress] = new ImageInfo(imageName, baseAddress, size);
  }

  /// <summary>
  /// Register sorted function debug info for a module.
  /// </summary>
  public void SetFunctions(string moduleName, List<FunctionDebugInfo> sortedFunctions) {
    sortedFunctionsByModule_[moduleName] = sortedFunctions;
  }

  /// <summary>
  /// Resolve an instruction pointer to a module name and RVA within that module.
  /// </summary>
  public ResolvedIp? Resolve(long ip) {
    // Try managed method resolution first (if enabled).
    if (managedResolver_ != null) {
      var managed = managedResolver_.FindMethod(ip);
      if (managed != null) {
        long rva = ip - managed.NativeStartAddress;
        return new ResolvedIp(managed.ModuleName ?? "[managed]", rva, managed.MethodName, ip, rva, managed.NativeSize, true);
      }
    }

    // Find the module that contains this IP.
    var image = FindImage(ip);
    if (image == null) return null;

    long moduleRva = ip - image.BaseAddress;

    // Find the function within the module.
    if (sortedFunctionsByModule_.TryGetValue(image.Name, out var functions)) {
      var func = FunctionDebugInfo.BinarySearch(functions, moduleRva);
      if (func != null) {
        return new ResolvedIp(image.Name, func.RVA, func.Name, ip,
          moduleRva - func.RVA,
          (int)func.Size);
      }
    }

    // Module found but function not resolved.
    return new ResolvedIp(image.Name, moduleRva, null, ip);
  }

  private ImageInfo? FindImage(long ip) {
    // Binary search for the image with the largest base address <= ip.
    var keys = imagesByBaseAddress_.Keys;
    int low = 0;
    int high = keys.Count - 1;
    ImageInfo? best = null;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      long baseAddr = keys[mid];

      if (baseAddr <= ip) {
        var candidate = imagesByBaseAddress_[baseAddr];
        if (ip < baseAddr + candidate.Size) {
          best = candidate;
        }

        low = mid + 1;
      }
      else {
        high = mid - 1;
      }
    }

    return best;
  }
}

/// <summary>
/// Result of resolving an instruction pointer.
/// </summary>
internal record ResolvedIp(
  string ModuleName,
  long Rva,
  string? FunctionName,
  long OriginalIp,
  long InstructionOffset = 0,
  int FunctionSize = 0,
  bool IsManaged = false);

/// <summary>
/// Information about a loaded image/module.
/// </summary>
internal record ImageInfo(string Name, long BaseAddress, int Size);
