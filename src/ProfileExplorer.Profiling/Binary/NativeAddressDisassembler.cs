// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Whether a caller-supplied address is a module-relative RVA or an absolute instruction-pointer
/// value that must be converted to an RVA using a caller-supplied runtime image base.
/// </summary>
public enum NativeAddressForm {
  /// <summary>The address is already a module-relative RVA (image-base independent).</summary>
  ModuleRva,
  /// <summary>
  /// The address is an absolute instruction pointer captured at runtime (e.g. from a stack trace
  /// or CPU sample) and must be converted to an RVA using <see cref="NativeAddressDisassemblyRequest.ImageBase"/>
  /// — the *actual* runtime load address, which can differ from the PE's static preferred image
  /// base due to ASLR/rebasing.
  /// </summary>
  AbsoluteInstructionPointer
}

/// <summary>
/// Distinguishes an address that is itself the next instruction to execute (a program counter /
/// instruction pointer) from an address recovered from a stack frame's return slot (a return
/// address, which points to the instruction *after* a call and must be treated differently when
/// resolving "the calling instruction").
/// </summary>
public enum FrameAddressKind {
  InstructionPointer,
  ReturnAddress
}

/// <summary>
/// How the resolved function range for a request was determined.
/// </summary>
public enum FunctionBoundaryProvenance {
  /// <summary>No trustworthy bounds could be determined (see the result's FailureReason).</summary>
  Unresolved,
  /// <summary>Bounds came from PDB/DIA symbols (<see cref="ISymbolDebugInfo.FindFunctionByRVA"/>).</summary>
  PdbSymbols,
  /// <summary>
  /// Bounds came from the PE exception directory (.pdata/unwind info), used only when PDB/DIA
  /// symbols were not supplied or didn't resolve the address.
  /// </summary>
  ExceptionDirectory
}

/// <summary>
/// Explicit, enumerated failure reasons for <see cref="NativeAddressDisassembler.Resolve"/>. The
/// API never guesses or silently degrades: any condition it can't resolve deterministically is
/// reported here instead of producing a best-effort/partial result.
/// </summary>
public enum NativeAddressDisassemblyFailure {
  None,
  BinaryPathInvalid,
  BinaryNotFound,
  InvalidPeImage,
  ImageIdentityMismatch,
  UnsupportedArchitecture,
  ImageBaseRequired,
  AddressBelowImageBase,
  RvaOutOfImageRange,
  RvaNotInCodeSection,
  FunctionBoundsNotResolved,
  DisassemblyProducedNoInstructions,
  DisassemblerInitializationFailed
}

/// <summary>
/// Request for <see cref="NativeAddressDisassembler.Resolve"/>: an exact binary identity plus a
/// single address to resolve and disassemble around, with explicit frame semantics.
/// </summary>
public sealed class NativeAddressDisassemblyRequest {
  /// <summary>Exact path to the PE binary (.dll/.exe) on disk.</summary>
  public string BinaryPath { get; init; } = null!; // Runtime-validated (non-null/non-empty) in Resolve().

  /// <summary>
  /// Optional identity check: when supplied, the binary at <see cref="BinaryPath"/> must match
  /// (name/timestamp/size, via <see cref="BinaryFileDescriptor.Equals(BinaryFileDescriptor)"/>) —
  /// guards against resolving addresses against a different build than the one the address was
  /// captured from.
  /// </summary>
  public BinaryFileDescriptor? ExpectedImageIdentity { get; init; }

  /// <summary>The address to resolve: a module RVA or an absolute IP, per <see cref="AddressForm"/>.</summary>
  public long Address { get; init; }

  public NativeAddressForm AddressForm { get; init; } = NativeAddressForm.ModuleRva;

  /// <summary>
  /// Required (no silent default) when <see cref="AddressForm"/> is
  /// <see cref="NativeAddressForm.AbsoluteInstructionPointer"/>: the actual runtime load address
  /// of the module, used to convert <see cref="Address"/> to an RVA. Ignored (optional) for
  /// <see cref="NativeAddressForm.ModuleRva"/>, where it is only used to compute a diagnostic
  /// absolute address in the result if supplied.
  /// </summary>
  public long? ImageBase { get; init; }

  public FrameAddressKind FrameKind { get; init; } = FrameAddressKind.InstructionPointer;

  /// <summary>
  /// Optional, caller-supplied, already-loaded PDB/DIA symbol provider. Never auto-loaded or
  /// downloaded by this API — determinism requires the caller to supply (or omit) debug info
  /// explicitly, rather than this API silently reaching out to a symbol server.
  /// </summary>
  public ISymbolDebugInfo? SymbolDebugInfo { get; init; }

  /// <summary>Optional symbol name formatter (e.g. C++ demangling), applied the same way as <see cref="Disassembler"/>.</summary>
  public FunctionNameFormatter? NameFormatter { get; init; }
}

/// <summary>
/// A resolved function's address range and how it was determined.
/// </summary>
public readonly record struct ResolvedFunctionRange(long StartRva, long EndRva,
                                                    FunctionBoundaryProvenance Provenance,
                                                    string? FunctionName,
                                                    RuntimeFunctionKind? RuntimeFunctionKind) {
  public long Size => EndRva - StartRva;
}

/// <summary>
/// Result of <see cref="NativeAddressDisassembler.Resolve"/>. Always carries as much resolved
/// context as was determined, even on failure (e.g. <see cref="Architecture"/> is populated once
/// the image is opened, regardless of whether bounds/disassembly later fail) — <see cref="Success"/>
/// and <see cref="FailureReason"/> are the authoritative signal for whether <see cref="Instructions"/>
/// should be trusted.
/// </summary>
public sealed class NativeAddressDisassemblyResult {
  public bool Success { get; init; }
  public NativeAddressDisassemblyFailure FailureReason { get; init; } = NativeAddressDisassemblyFailure.None;
  public string? ErrorMessage { get; init; }

  public string? ImagePath { get; init; }
  public string? ModuleName { get; init; }
  public Machine? Architecture { get; init; }

  /// <summary>The RVA as originally requested (after AbsoluteInstructionPointer -> RVA conversion, if applicable).</summary>
  public long RequestedRva { get; init; }
  /// <summary>Absolute address for <see cref="RequestedRva"/>, computed from the request/PE image base.</summary>
  public long RequestedAddress { get; init; }

  /// <summary>
  /// The RVA after return-address normalization (preceding-call-instruction adjustment). Equal to
  /// <see cref="RequestedRva"/> when <see cref="WasNormalized"/> is false (frame is an instruction
  /// pointer, or normalization wasn't deterministically possible).
  /// </summary>
  public long NormalizedRva { get; init; }
  public long NormalizedAddress { get; init; }
  public bool WasNormalized { get; init; }

  /// <summary>The resolved function range containing <see cref="NormalizedRva"/>, or null if unresolved.</summary>
  public ResolvedFunctionRange? Range { get; init; }

  /// <summary>
  /// Stable identity string for the resolved range: "{Module}!{FunctionName}" when a name was
  /// resolved (PDB), otherwise the stable unresolved form "{Module}!&lt;unknown+0x{StartRva:X}&gt;"
  /// (function bounds known from .pdata, but no name available). Null when bounds are unresolved.
  /// </summary>
  public string? QualifiedName { get; init; }

  /// <summary>
  /// Structured disassembly of the full resolved function range (bounded — never more than
  /// [Range.StartRva, Range.EndRva)). Empty when bounds could not be resolved or disassembly
  /// produced no instructions.
  /// </summary>
  public List<DisassembledInstruction> Instructions { get; init; } = new();

  public static NativeAddressDisassemblyResult Failure(NativeAddressDisassemblyFailure reason, string message,
                                                        string? imagePath = null, Machine? architecture = null,
                                                        long requestedRva = 0, long requestedAddress = 0) {
    return new NativeAddressDisassemblyResult {
      Success = false,
      FailureReason = reason,
      ErrorMessage = message,
      ImagePath = imagePath,
      Architecture = architecture,
      RequestedRva = requestedRva,
      RequestedAddress = requestedAddress,
      NormalizedRva = requestedRva,
      NormalizedAddress = requestedAddress
    };
  }
}

/// <summary>
/// Deterministic, structured, address-based native (x64/ARM64) disassembly API, independent of
/// Profile Explorer's CPU-sample-aggregation pipeline (<c>SampleAggregator</c>/<c>CallTreeBuilder</c>/
/// <c>IpResolver</c>). Given an exact binary identity and a single requested address (module RVA or
/// absolute IP, with explicit instruction-pointer-vs-return-address frame semantics), this:
/// <list type="number">
/// <item>Resolves trustworthy function bounds from PDB/DIA symbols when supplied, otherwise from
/// the PE exception directory (.pdata/unwind info via <see cref="RuntimeFunctionTable"/>) for
/// x64/ARM64 — never an unbounded scan of .text, and never a guess when neither source resolves.</item>
/// <item>Normalizes return addresses to the preceding call instruction when that's deterministically
/// possible (always for ARM64's fixed 4-byte instructions; for x64 only when resolved bounds allow a
/// bounded forward disassembly that lands exactly on the return address, since x86/x64's
/// variable-width encoding makes backward instruction-boundary discovery otherwise ambiguous).</item>
/// <item>Returns the requested/normalized IP/RVA, the resolved range with provenance, a stable
/// unresolved identity ("module!&lt;unknown+0xStartRVA&gt;") when no name is available, and a fully
/// structured instruction/target list for the resolved range — or an explicit failure.</item>
/// </list>
/// </summary>
public static class NativeAddressDisassembler {
  public static NativeAddressDisassemblyResult Resolve(NativeAddressDisassemblyRequest request) {
    ArgumentNullException.ThrowIfNull(request);

    if (string.IsNullOrWhiteSpace(request.BinaryPath)) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.BinaryPathInvalid,
        "BinaryPath is null, empty, or whitespace.");
    }

    if (!File.Exists(request.BinaryPath)) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.BinaryNotFound,
        $"Binary file not found: {request.BinaryPath}", request.BinaryPath);
    }

    using var peInfo = new PEBinaryInfoProvider(request.BinaryPath);

    if (!peInfo.Initialize()) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.InvalidPeImage,
        $"Failed to parse PE image (bad/unreadable PE headers): {request.BinaryPath}", request.BinaryPath);
    }

    var binaryInfo = peInfo.BinaryFileInfo;

    if (binaryInfo is null) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.InvalidPeImage,
        $"PE image has no optional header: {request.BinaryPath}", request.BinaryPath);
    }

    if (request.ExpectedImageIdentity is not null && !request.ExpectedImageIdentity.Equals(binaryInfo)) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.ImageIdentityMismatch,
        $"Expected image identity ({request.ExpectedImageIdentity}) does not match the binary on disk " +
        $"({binaryInfo}).", request.BinaryPath, binaryInfo.Architecture);
    }

    if (binaryInfo.Architecture != Machine.Amd64 && binaryInfo.Architecture != Machine.Arm64) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.UnsupportedArchitecture,
        $"Architecture {binaryInfo.Architecture} is not supported; only x64 (Amd64) and ARM64 are.",
        request.BinaryPath, binaryInfo.Architecture);
    }

    // Resolve the caller's address (module RVA or absolute IP) to a module-relative RVA.
    long requestedRva;
    long effectiveImageBase;

    if (request.AddressForm == NativeAddressForm.AbsoluteInstructionPointer) {
      if (request.ImageBase is not long suppliedBase) {
        return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.ImageBaseRequired,
          "ImageBase is required when AddressForm is AbsoluteInstructionPointer (the static PE " +
          "image base cannot be assumed to match the runtime load address).",
          request.BinaryPath, binaryInfo.Architecture);
      }

      effectiveImageBase = suppliedBase;

      if (request.Address < effectiveImageBase) {
        return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.AddressBelowImageBase,
          $"Address 0x{request.Address:X} is below the supplied image base 0x{effectiveImageBase:X}.",
          request.BinaryPath, binaryInfo.Architecture);
      }

      requestedRva = request.Address - effectiveImageBase;
    }
    else {
      requestedRva = request.Address;
      effectiveImageBase = request.ImageBase ?? binaryInfo.ImageBase;

      if (requestedRva < 0) {
        return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.AddressBelowImageBase,
          $"RVA 0x{requestedRva:X} is negative.", request.BinaryPath, binaryInfo.Architecture);
      }
    }

    long requestedAddress = effectiveImageBase + requestedRva;

    if (requestedRva >= binaryInfo.ImageSize) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.RvaOutOfImageRange,
        $"RVA 0x{requestedRva:X} is outside the image (size 0x{binaryInfo.ImageSize:X}).",
        request.BinaryPath, binaryInfo.Architecture, requestedRva, requestedAddress);
    }

    if (!IsRvaInCodeSection(peInfo, requestedRva)) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.RvaNotInCodeSection,
        $"RVA 0x{requestedRva:X} is not within any executable PE section.",
        request.BinaryPath, binaryInfo.Architecture, requestedRva, requestedAddress);
    }

    // Return addresses can land exactly on the start of the *next* function (e.g. a noreturn/tail
    // call), so resolve bounds using rva-1 -- the standard stack-unwinder trick for attributing a
    // return address to the calling function rather than the callee that never returns to it.
    long lookupRva = request.FrameKind == FrameAddressKind.ReturnAddress ? requestedRva - 1 : requestedRva;
    if (lookupRva < 0) lookupRva = 0;

    List<RuntimeFunctionRange>? runtimeFunctions = null; // Built lazily, only if/when pdata fallback is needed.

    var initialBounds = ResolveFunctionBounds(peInfo, binaryInfo.Architecture, request.SymbolDebugInfo,
                                              lookupRva, ref runtimeFunctions);

    if (initialBounds == null) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.FunctionBoundsNotResolved,
        $"Could not resolve function bounds for RVA 0x{lookupRva:X} from PDB symbols or the PE " +
        "exception directory (.pdata) -- refusing to disassemble an unbounded range.",
        request.BinaryPath, binaryInfo.Architecture, requestedRva, requestedAddress);
    }

    using var disassembler = Disassembler.CreateForBinary(request.BinaryPath, request.SymbolDebugInfo!, request.NameFormatter!);

    if (disassembler == null) {
      return NativeAddressDisassemblyResult.Failure(NativeAddressDisassemblyFailure.DisassemblerInitializationFailed,
        $"Failed to initialize the disassembler for: {request.BinaryPath}",
        request.BinaryPath, binaryInfo.Architecture, requestedRva, requestedAddress);
    }

    var (normalizedRva, wasNormalized) = NormalizeReturnAddress(request.FrameKind, binaryInfo.Architecture,
                                                                requestedRva, initialBounds.Value, disassembler);

    // Re-resolve the authoritative range from the *normalized* RVA so the contract "Range always
    // contains NormalizedRva" holds self-consistently (guards the edge case where the rva-1 lookup
    // key and the rva-4 ARM64 normalization straddle a boundary between two tiny/adjacent functions).
    var finalBounds = ResolveFunctionBounds(peInfo, binaryInfo.Architecture, request.SymbolDebugInfo,
                                            normalizedRva, ref runtimeFunctions) ?? initialBounds.Value;

    string qualifiedName = BuildQualifiedName(binaryInfo, finalBounds);
    var range = new ResolvedFunctionRange(finalBounds.StartRva, finalBounds.EndRva, finalBounds.Provenance,
                                          finalBounds.FunctionName, finalBounds.RuntimeKind);

    var instructions = disassembler.DisassembleToStructuredList(finalBounds.StartRva, finalBounds.Size);

    if (instructions.Count == 0) {
      return new NativeAddressDisassemblyResult {
        Success = false,
        FailureReason = NativeAddressDisassemblyFailure.DisassemblyProducedNoInstructions,
        ErrorMessage = $"Disassembly of resolved range [0x{finalBounds.StartRva:X}, 0x{finalBounds.EndRva:X}) " +
                       "produced no instructions.",
        ImagePath = request.BinaryPath,
        ModuleName = binaryInfo.ImageName,
        Architecture = binaryInfo.Architecture,
        RequestedRva = requestedRva,
        RequestedAddress = requestedAddress,
        NormalizedRva = normalizedRva,
        NormalizedAddress = effectiveImageBase + normalizedRva,
        WasNormalized = wasNormalized,
        Range = range,
        QualifiedName = qualifiedName,
        Instructions = instructions
      };
    }

    return new NativeAddressDisassemblyResult {
      Success = true,
      FailureReason = NativeAddressDisassemblyFailure.None,
      ImagePath = request.BinaryPath,
      ModuleName = binaryInfo.ImageName,
      Architecture = binaryInfo.Architecture,
      RequestedRva = requestedRva,
      RequestedAddress = requestedAddress,
      NormalizedRva = normalizedRva,
      NormalizedAddress = effectiveImageBase + normalizedRva,
      WasNormalized = wasNormalized,
      Range = range,
      QualifiedName = qualifiedName,
      Instructions = instructions
    };
  }

  private static bool IsRvaInCodeSection(PEBinaryInfoProvider peInfo, long rva) {
    foreach (var section in peInfo.CodeSectionHeaders) {
      long size = Math.Max(section.VirtualSize, section.SizeOfRawData);

      if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size) {
        return true;
      }
    }

    return false;
  }

  private readonly record struct FunctionBounds(long StartRva, long EndRva, FunctionBoundaryProvenance Provenance,
                                                string? FunctionName, RuntimeFunctionKind? RuntimeKind) {
    public long Size => EndRva - StartRva;
  }

  /// <summary>
  /// Resolve function bounds for <paramref name="rva"/>: PDB/DIA symbols first (when supplied),
  /// then the PE exception directory (parsed once per request into <paramref name="runtimeFunctions"/>
  /// and reused). Returns null when neither source resolves the address -- never guesses, never
  /// falls back to scanning code.
  /// </summary>
  private static FunctionBounds? ResolveFunctionBounds(PEBinaryInfoProvider peInfo, Machine architecture,
                                                       ISymbolDebugInfo? symbolDebugInfo, long rva,
                                                       ref List<RuntimeFunctionRange>? runtimeFunctions) {
    if (symbolDebugInfo != null) {
      var func = symbolDebugInfo.FindFunctionByRVA(rva);

      if (func != null) {
        return new FunctionBounds(func.StartRVA, func.StartRVA + func.Size, FunctionBoundaryProvenance.PdbSymbols,
                                  func.Name, null);
      }
    }

    runtimeFunctions ??= ParseRuntimeFunctions(peInfo, architecture);
    var range = RuntimeFunctionTable.Find(runtimeFunctions, rva);

    if (range != null) {
      return new FunctionBounds(range.Value.StartRva, range.Value.EndRva, FunctionBoundaryProvenance.ExceptionDirectory,
                                null, range.Value.Kind);
    }

    return null;
  }

  private static List<RuntimeFunctionRange> ParseRuntimeFunctions(PEBinaryInfoProvider peInfo, Machine architecture) {
    var exceptionDirectory = peInfo.GetExceptionDirectoryData();

    if (exceptionDirectory.IsEmpty) {
      return new List<RuntimeFunctionRange>();
    }

    if (architecture == Machine.Amd64) {
      return RuntimeFunctionTable.ParseAmd64(exceptionDirectory.Span);
    }

    if (architecture == Machine.Arm64) {
      return RuntimeFunctionTable.ParseArm64(exceptionDirectory.Span, xdataRva => {
        if (peInfo.TryReadRvaData(xdataRva, sizeof(uint), out var headerBytes)) {
          return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Span);
        }

        return null;
      });
    }

    return new List<RuntimeFunctionRange>();
  }

  private static string BuildQualifiedName(BinaryFileDescriptor binaryInfo, FunctionBounds bounds) {
    string functionPart = bounds.FunctionName ?? $"<unknown+0x{bounds.StartRva:X}>";
    return $"{binaryInfo.ImageName}!{functionPart}";
  }

  /// <summary>
  /// Normalize a return address to the RVA of the preceding call instruction, when that's
  /// deterministically possible. See the type-level doc comment for the per-architecture rationale.
  /// Returns (requestedRva, false) whenever normalization can't be confirmed deterministically --
  /// never a guess.
  /// </summary>
  private static (long NormalizedRva, bool WasNormalized) NormalizeReturnAddress(
      FrameAddressKind frameKind, Machine architecture, long requestedRva, FunctionBounds containingRange,
      Disassembler disassembler) {
    if (frameKind != FrameAddressKind.ReturnAddress) {
      return (requestedRva, false);
    }

    if (architecture == Machine.Arm64) {
      // ARM64 instructions are always exactly 4 bytes, so the instruction immediately preceding a
      // return address is always at (returnAddress - 4) -- mechanically deterministic regardless
      // of whether function bounds are even known. Cross-check that the candidate instruction is
      // actually a call (bl/blr) before trusting it, guarding against a caller-supplied address
      // that wasn't really a return address.
      long candidate = requestedRva - 4;

      if (candidate < 0) {
        return (requestedRva, false);
      }

      var candidateInstructions = disassembler.DisassembleToStructuredList(candidate, 4);

      if (candidateInstructions.Count == 1) {
        var (isCall, _) = Disassembler.ClassifyBranch(Machine.Arm64, candidateInstructions[0].Mnemonic ?? "");

        if (isCall) {
          return (candidate, true);
        }
      }

      return (requestedRva, false);
    }

    if (architecture == Machine.Amd64) {
      // x86/x64 instructions are variable-length, so the only deterministic way to find the
      // instruction boundary immediately preceding a return address is to linearly disassemble
      // forward from a *known, trustworthy* function start up to the return address and check
      // whether decoding lands exactly on it (and that instruction is a call). This requires
      // resolved bounds -- there is no ISA-level guarantee to fall back on, so when bounds aren't
      // available (or decoding doesn't land exactly on the boundary), no guess is made.
      long start = containingRange.StartRva;
      long scanSize = requestedRva - start;

      if (scanSize <= 0 || scanSize > int.MaxValue) {
        return (requestedRva, false);
      }

      var instructions = disassembler.DisassembleToStructuredList(start, scanSize);

      if (instructions.Count == 0) {
        return (requestedRva, false);
      }

      var last = instructions[^1];

      if (last.Rva + last.Size != requestedRva) {
        return (requestedRva, false); // Decode didn't land exactly on the boundary -- no guess.
      }

      var (isCall, _) = Disassembler.ClassifyBranch(Machine.Amd64, last.Mnemonic ?? "");

      return isCall ? (last.Rva, true) : (requestedRva, false);
    }

    return (requestedRva, false);
  }
}
