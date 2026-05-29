// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;

namespace ProfileExplorer.Profiling;

/// <summary>
/// Configuration options for the function profiler.
/// </summary>
public class ProfilerOptions {
  /// <summary>
  /// Symbol server paths in standard format (e.g., "srv*C:\Symbols*https://symbolserver.example.com").
  /// </summary>
  public IReadOnlyList<string> SymbolPaths { get; set; } = [];

  /// <summary>Normal symbol download timeout in seconds.</summary>
  public int SymbolTimeoutSeconds { get; set; } = 30;

  /// <summary>Bellwether test timeout in seconds (quick health check before bulk downloads).</summary>
  public int BellwetherTimeoutSeconds { get; set; } = 5;

  /// <summary>Reduced timeout when the server has been detected as slow.</summary>
  public int DegradedTimeoutSeconds { get; set; } = 3;

  /// <summary>Only load symbols for modules matching this company name.</summary>
  public bool EnableCompanyFilter { get; set; } = true;

  /// <summary>Company name for the module filter.</summary>
  public string? CompanyName { get; set; } = "Microsoft";

  /// <summary>Skip download attempts for previously-failed files within the negative cache TTL.</summary>
  public bool EnableNegativeCache { get; set; } = true;

  /// <summary>Minimum self-time percent threshold for GetFunctionProfiles results.</summary>
  public double MinSelfPercent { get; set; } = 0.0;

  /// <summary>Minimum hot-line percent threshold for annotated assembly output.</summary>
  public double MinHotLinePercent { get; set; } = 1.0;

  /// <summary>Maximum number of hot lines per function in annotated assembly.</summary>
  public int MaxHotLines { get; set; } = 10;

  /// <summary>Target processor architecture.</summary>
  public ProcessorArchitecture Architecture { get; set; } = ProcessorArchitecture.Amd64;

  /// <summary>Whether to process PMU/PMC hardware counter data.</summary>
  public bool IncludePerformanceCounters { get; set; } = false;

  /// <summary>Whether to resolve managed (.NET) JIT methods.</summary>
  public bool IncludeManagedCode { get; set; } = true;

  /// <summary>Local cache directory for downloaded PDBs and binaries.</summary>
  public string? SymbolCacheDirectory { get; set; }

  /// <summary>
  /// Optional pre-authenticated bearer token for the symbol server.
  /// When set, the SymbolServerClient uses this token instead of trying Azure Identity.
  /// The consumer is responsible for obtaining the token (e.g., via SymwebHandler from TraceEvent).
  /// </summary>
  public string? SymwebBearerToken { get; set; }

  /// <summary>
  /// Validate options and throw if invalid.
  /// </summary>
  public void Validate() {
    if (SymbolPaths is not { Count: > 0 }) {
      throw new ArgumentException("At least one symbol path must be specified.", nameof(SymbolPaths));
    }

    if (SymbolTimeoutSeconds < 0) {
      throw new ArgumentOutOfRangeException(nameof(SymbolTimeoutSeconds), "Timeout must be non-negative.");
    }

    if (BellwetherTimeoutSeconds < 0) {
      throw new ArgumentOutOfRangeException(nameof(BellwetherTimeoutSeconds), "Timeout must be non-negative.");
    }

    MinSelfPercent = Math.Clamp(MinSelfPercent, 0, 100);
    MinHotLinePercent = Math.Clamp(MinHotLinePercent, 0, 100);
  }
}
