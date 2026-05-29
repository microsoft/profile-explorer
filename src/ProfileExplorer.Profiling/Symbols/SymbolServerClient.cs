// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Azure.Identity;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// HTTP-based symbol server client for downloading PDBs and binaries.
/// Vendored implementation — replaces TraceEvent's SymbolReader to avoid the large transitive dependency.
/// Implements the standard symbol server protocol: GET /{name}/{hash}/{name}
/// </summary>
public class SymbolServerClient : IDisposable {
  private readonly HttpClient httpClient_;
  private readonly List<SymbolServerInfo> servers_ = [];
  private readonly string? localCachePath_;
  private readonly TimeSpan timeout_;
  private readonly TimeSpan degradedTimeout_;
  private bool isDegraded_;
  private readonly HashSet<string> negativeCache_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly object negativeCacheLock_ = new();
  private readonly bool enableNegativeCache_;
  private string? effectiveLocalCachePath_;
  private readonly string? injectedBearerToken_;

  public SymbolServerClient(ProfilerOptions options) {
    timeout_ = TimeSpan.FromSeconds(options.SymbolTimeoutSeconds);
    degradedTimeout_ = TimeSpan.FromSeconds(options.DegradedTimeoutSeconds);
    enableNegativeCache_ = options.EnableNegativeCache;
    injectedBearerToken_ = options.SymwebBearerToken;

    httpClient_ = new HttpClient {
      Timeout = timeout_
    };

    foreach (string path in options.SymbolPaths) {
      ParseSymbolPath(path);
    }

    localCachePath_ = options.SymbolCacheDirectory;
  }

  /// <summary>
  /// Download a PDB file from the symbol server.
  /// </summary>
  /// <param name="pdbName">PDB file name (e.g., "ntdll.pdb").</param>
  /// <param name="guid">PDB GUID from the CodeView debug directory.</param>
  /// <param name="age">PDB Age from the CodeView debug directory.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Local file path to the downloaded PDB, or null if not found.</returns>
  public async Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default) {
    string hash = $"{guid:N}{age}".ToUpperInvariant();
    return await DownloadFileAsync(pdbName, hash, ct);
  }

  /// <summary>
  /// Download a binary/executable from the symbol server.
  /// </summary>
  /// <param name="binaryName">Binary file name (e.g., "ntdll.dll").</param>
  /// <param name="timeDateStamp">PE TimeDateStamp from the file header.</param>
  /// <param name="imageSize">PE ImageSize (SizeOfImage).</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>Local file path to the downloaded binary, or null if not found.</returns>
  public async Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                                  CancellationToken ct = default) {
    string hash = $"{timeDateStamp:X8}{imageSize:x}".ToUpperInvariant();
    return await DownloadFileAsync(binaryName, hash, ct);
  }

  /// <summary>
  /// Perform a bellwether test to check symbol server health.
  /// </summary>
  public async Task<bool> TestServerHealthAsync(string testPdbName, Guid testGuid, int testAge,
                                                 TimeSpan timeout, CancellationToken ct = default) {
    string hash = $"{testGuid:N}{testAge}".ToUpperInvariant();
    string key = $"{testPdbName}/{hash}";

    foreach (var server in servers_.Where(s => s.IsRemote)) {
      string url = $"{server.Url}/{testPdbName}/{hash}/{testPdbName}";
      try {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        await ApplyAuthAsync(request, server);

        using var response = await httpClient_.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (response.IsSuccessStatusCode) return true;
      }
      catch {
        // Server unreachable or timeout.
      }
    }

    isDegraded_ = true;
    return false;
  }

  private async Task<string?> DownloadFileAsync(string fileName, string hash, CancellationToken ct) {
    string key = $"{fileName}/{hash}";

    // Check negative cache.
    if (enableNegativeCache_) {
      lock (negativeCacheLock_) {
        if (negativeCache_.Contains(key)) return null;
      }
    }

    // Check local cache first.
    string? cachedPath = FindInLocalCache(fileName, hash);
    if (cachedPath != null) return cachedPath;

    // Try each server.
    foreach (var server in servers_) {
      if (!server.IsRemote) {
        // Local path server — check if file exists directly.
        string localPath = Path.Combine(server.Url, fileName, hash, fileName);
        if (File.Exists(localPath)) return localPath;
        continue;
      }

      string url = $"{server.Url}/{fileName}/{hash}/{fileName}";
      try {
        var effectiveTimeout = isDegraded_ ? degradedTimeout_ : timeout_;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await ApplyAuthAsync(request, server);

        using var response = await httpClient_.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        if (response.StatusCode == HttpStatusCode.NotFound) {
          continue; // Try next server.
        }

        if (!response.IsSuccessStatusCode) {
          Console.Error.WriteLine($"  Symbol download {response.StatusCode}: {url}");
          continue;
        }

        // Save to local cache.
        string targetDir = GetCachePath(fileName, hash);
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, fileName);

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cts.Token);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(fileStream, cts.Token);

        return targetPath;
      }
      catch (TaskCanceledException) {
        // Timeout — mark as degraded and try next server.
        isDegraded_ = true;
      }
      catch (HttpRequestException) {
        // Network error — try next server.
      }
    }

    // All servers failed — add to negative cache.
    if (enableNegativeCache_) {
      lock (negativeCacheLock_) {
        negativeCache_.Add(key);
      }
    }

    return null;
  }

  private string? FindInLocalCache(string fileName, string hash) {
    string? cachePath = effectiveLocalCachePath_ ?? localCachePath_;
    if (cachePath == null) return null;
    string path = Path.Combine(cachePath, fileName, hash, fileName);
    return File.Exists(path) ? path : null;
  }

  private string GetCachePath(string fileName, string hash) {
    string basePath = effectiveLocalCachePath_ ?? localCachePath_ ?? Path.Combine(Path.GetTempPath(), "ProfileExplorer.Profiling", "symbols");
    return Path.Combine(basePath, fileName, hash);
  }

  private static Azure.Core.AccessToken? cachedToken_;
  private static bool authFailed_;
  private static readonly SemaphoreSlim authLock_ = new(1, 1);

  private static async Task ApplyAuthAsync(HttpRequestMessage request, SymbolServerInfo server) {
    if (!server.RequiresAuth) return;
    if (authFailed_) return; // Don't retry auth after permanent failure.

    // Use injected token if available (from consumer via ProfilerOptions.SymwebBearerToken).
    if (server.InjectedToken != null) {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.InjectedToken);
      return;
    }

    try {
      // Use cached token if still valid (tokens last ~1 hour).
      if (cachedToken_.HasValue && cachedToken_.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken_.Value.Token);
        return;
      }

      await authLock_.WaitAsync();
      try {
        // Double-check after acquiring lock.
        if (cachedToken_.HasValue && cachedToken_.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5)) {
          request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken_.Value.Token);
          return;
        }

        if (authFailed_) return;

        // Symweb uses Azure AD with resource https://microsoft.com.
        // Try non-interactive creds first, then browser as last resort.
        var credential = new ChainedTokenCredential(
          new SharedTokenCacheCredential(new SharedTokenCacheCredentialOptions { TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47" }),
          new VisualStudioCredential(),
          new AzureCliCredential(),
          new AzurePowerShellCredential(),
          new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions {
            TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47",
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "ProfileExplorer.Profiling" }
          }));

        var tokenContext = new Azure.Core.TokenRequestContext(["https://microsoft.com/.default"]);
        cachedToken_ = await credential.GetTokenAsync(tokenContext);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken_.Value.Token);
      }
      finally {
        authLock_.Release();
      }
    }
    catch (Exception ex) {
      authFailed_ = true;
      Console.Error.WriteLine($"  Symweb auth failed (will not retry): {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
  }

  /// <summary>
  /// Parse a symbol path string into server entries.
  /// Format: "srv*[localcache*]serverUrl" or "localPath"
  /// Multiple entries separated by semicolons.
  /// </summary>
  internal void ParseSymbolPath(string symbolPath) {
    // Split by semicolons for multiple entries.
    foreach (string entry in symbolPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      if (entry.StartsWith("srv*", StringComparison.OrdinalIgnoreCase) ||
          entry.StartsWith("SRV*", StringComparison.OrdinalIgnoreCase)) {
        // srv*[cache*]url
        string[] parts = entry[4..].Split('*');
        if (parts.Length == 1) {
          // srv*url — no local cache specified.
          servers_.Add(SymbolServerInfo.Remote(parts[0], injectedBearerToken_));
        }
        else if (parts.Length >= 2) {
          // srv*cache*url
          servers_.Add(SymbolServerInfo.Remote(parts[^1], injectedBearerToken_));

          // Also register the local cache as a search path.
          if (Directory.Exists(parts[0]) || !parts[0].StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
            effectiveLocalCachePath_ ??= parts[0]; // Use first cache path as default.
            servers_.Insert(0, SymbolServerInfo.Local(parts[0]));
          }
        }
      }
      else if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
        servers_.Add(SymbolServerInfo.Remote(entry, injectedBearerToken_));
      }
      else {
        // Local directory path.
        servers_.Insert(0, SymbolServerInfo.Local(entry));
      }
    }
  }

  internal IReadOnlyList<SymbolServerInfo> Servers => servers_;

  public void Dispose() {
    httpClient_.Dispose();
  }
}

/// <summary>
/// Describes a symbol server endpoint (local or remote).
/// </summary>
internal class SymbolServerInfo {
  public string Url { get; }
  public bool IsRemote { get; }
  public bool RequiresAuth { get; }
  public string? InjectedToken { get; }

  private SymbolServerInfo(string url, bool isRemote, bool requiresAuth, string? injectedToken = null) {
    Url = url.TrimEnd('/');
    IsRemote = isRemote;
    RequiresAuth = requiresAuth;
    InjectedToken = injectedToken;
  }

  public static SymbolServerInfo Remote(string url, string? injectedToken = null) {
    bool requiresAuth = url.Contains("symweb", StringComparison.OrdinalIgnoreCase);
    return new SymbolServerInfo(url, true, requiresAuth, requiresAuth ? injectedToken : null);
  }

  public static SymbolServerInfo Local(string path) {
    return new SymbolServerInfo(path, false, false);
  }

  public override string ToString() => $"{(IsRemote ? "Remote" : "Local")}: {Url}";
}
