// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// Abstraction over locating symbol (PDB) and binary files for a module, given its
/// debug identity. This inverts the download dependency: the library's readers call
/// through this interface instead of owning the transport.
/// <para>
/// External consumers can rely on the library's vendored <see cref="SymbolServerClient"/>
/// (the default implementation). The Profile Explorer host instead implements this over its
/// existing TraceEvent <c>SymbolReader</c>/<c>BinaryFileLocator</c> so the mature download,
/// authentication, caching, and filtering behavior is preserved.
/// </para>
/// </summary>
public interface ISymbolFileLocator {
  /// <summary>
  /// Locate a PDB file for the given debug identity, returning a local file path or
  /// <c>null</c> if it cannot be found.
  /// </summary>
  /// <param name="pdbName">PDB file name (e.g., "ntdll.pdb").</param>
  /// <param name="guid">PDB GUID from the CodeView debug directory.</param>
  /// <param name="age">PDB Age from the CodeView debug directory.</param>
  /// <param name="ct">Cancellation token.</param>
  Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default);

  /// <summary>
  /// Locate a binary/executable for the given PE identity, returning a local file path or
  /// <c>null</c> if it cannot be found.
  /// </summary>
  /// <param name="binaryName">Binary file name (e.g., "ntdll.dll").</param>
  /// <param name="timeDateStamp">PE TimeDateStamp from the file header.</param>
  /// <param name="imageSize">PE ImageSize (SizeOfImage).</param>
  /// <param name="originalFileName">
  /// The module's own embedded original filename (from its version resource), when known and
  /// different from <paramref name="binaryName"/>. Some first-party binaries are indexed on the
  /// symbol server under this internal name rather than the on-disk name -- e.g. ntoskrnl.exe's
  /// OriginalFileName is "ntkrnlmp.exe", and the symbol server only has an entry under that name.
  /// Tried as a fallback key when the primary <paramref name="binaryName"/> lookup 404s. Null when
  /// unknown.
  /// </param>
  /// <param name="ct">Cancellation token.</param>
  Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                    string? originalFileName = null, CancellationToken ct = default);
}
