# Reverse Engineering Support in Profile Explorer

## Why this exists

CPU or memory problems often live in code the tool's user doesn't have private symbols or source for — regardless of who wrote it. In that situation, Profile Explorer's job is to turn "we have a hot address and no other information" into enough *semantic* evidence — control flow, resolved API calls, register data flow, PDB types when they exist — that an AI can reconstruct a function's behavior with real confidence, not a plausible-sounding guess.

**Requirement:** this capability needs access to the exact binary the address came from, either on disk or downloadable from a symbol/binary server. A private PDB is helpful when available but not required. With neither the binary nor a PDB, no analysis is possible at all (see the evidence-tier table below).

Profile Explorer includes a headless, non-GUI **Function Evidence Generator**: given that binary and one address (an RVA, or an absolute instruction pointer from a stack/allocation site), it produces a single deterministic evidence package an AI can use to write pseudocode and explain performance/memory behavior — with facts and inference kept strictly separate throughout.

**This is not a decompiler and not a Ghidra replacement.** The goal is narrower and more achievable: explain *one caller-selected function* well enough that AI-assisted analysis of unfamiliar or symbol-poor code is meaningfully more accurate than working from raw assembly alone.

## Profile Explorer vs. Ghidra

| | Profile Explorer | Ghidra |
|---|---|---|
| Function discovery (given an address) | ✅ Fast, deterministic, from PDB or `.pdata` | Requires loading/analyzing the whole binary first |
| Live-trace/ETW correlation (module+RVA→function) | ✅ Native | ❌ No |
| x64/ARM64 disassembly with register semantics | ✅ | ✅ |
| Control flow graph, dominators, loops | ✅ (per selected function) | ✅ (whole program) |
| Import/export/API resolution, no PDB required | ✅ | ✅ |
| PDB type/signature recovery | ✅ | ✅ (with PDB) |
| Whole-program analysis, cross-references, data types | ❌ | ✅ |
| Native decompiler (C-like pseudocode) | ❌ (AI-generated from evidence instead) | ✅ |
| Scripting, persistent project database | Limited | ✅ |
| Headless, scriptable API for one function at a time | ✅ Purpose-built for this | Possible, but heavier |

**Bottom line:** Ghidra is the right tool for open-ended, whole-binary reverse engineering. Profile Explorer's evidence generator is the right tool when you already know *which function* matters (from a CPU sample, a pool allocation site, etc.) and want fast, structured, trustworthy evidence about just that function — with no dependency on loading or annotating the entire binary.

## Feature list

- **Exact function bounds** from PDB/DIA symbols, or the PE exception directory (`.pdata`) when no PDB is available. Never guesses at a boundary; fails explicitly instead.
- **Semantic instruction decoding** (x64/ARM64): mnemonic, operands, and the *exact* combined implicit+explicit set of registers each instruction reads and writes (via Capstone, not text parsing).
- **Control-transfer classification**: call, unconditional jump, conditional branch, return — including cases the underlying disassembler engine doesn't classify by default (e.g. `je`, `b.eq`, `cbz`/`tbz`).
- **Control flow graph**: basic blocks, successor/predecessor edges, dominators, and natural loop detection — presented as structured text/JSON (never an image) so it's directly usable in an AI prompt.
- **Reference resolution**: resolves indirect calls through the IAT (e.g. `call qword ptr [rip+N]`) to a real `Module!Function` identity, resolves exports (including forwarders), and classifies data references as ASCII/UTF-16 strings — all from the PE alone, **with or without a PDB**.
- **Known API facts**: a small, curated table of common Windows/CRT API side effects (allocation, free, lock acquire/release, reference counting, bulk memory ops) attached automatically to a resolved import.
- **PDB/DIA function signatures**: return type, calling convention, and named/typed parameters, when a full private PDB is available.
- **Lightweight data flow**: function-local reaching definitions, a narrow constant-propagation check (direct immediate loads only), and bounded backward slicing from any instruction — reports ambiguity honestly at CFG merge points rather than guessing.
- **One evidence package, one call**: `FunctionAnalysisPackage` bundles all of the above for one function, with a ready-to-use Markdown rendering (`ToPromptMarkdown()`) and a fully structured object model for JSON serialization.
- **Suggested analysis questions**: `ToPromptMarkdown()` (at `Standard`/`Full` detail) appends one targeted question per accuracy dimension — behavior, control flow, calls/APIs, data accesses, loop bounds, error handling, externally visible effects — steering the AI toward a structured, per-aspect answer instead of an open-ended "explain this code," which research on LLM-assisted reverse engineering has found more reliable.
- **Benchmark/coverage harness**: run a corpus of (binary, function) cases and get coverage metrics stratified by symbol availability, so accuracy claims are never averaged across wildly different scenarios.

## How to use the API

Everything lives in `ProfileExplorer.Profiling` (namespace `ProfileExplorer.Core.Binary`) and has no dependency on the GUI, TraceEvent, or a loaded trace. The entry point is:

```csharp
using ProfileExplorer.Core.Binary;

var result = FunctionAnalysisPackageBuilder.Build(
    binaryPath: @"C:\drivers\target.sys",
    address: 0x28897,                                   // module RVA, or an absolute IP
    addressForm: NativeAddressForm.ModuleRva,            // or AbsoluteInstructionPointer
    frameKind: FrameAddressKind.InstructionPointer,      // or ReturnAddress, for stack return slots
    symbolDebugInfo: null,                               // optional: a loaded PdbSymbolProvider
    detailLevel: FunctionAnalysisDetailLevel.Full);

if (!result.Success) {
    // result.FailureReason / result.AddressResolutionFailure / result.ErrorMessage
    // explain exactly why -- never a partial/fabricated package.
    return;
}

var package = result.Package!;
string aiPrompt = package.ToPromptMarkdown();   // ready to hand to an LLM, and renders correctly if displayed as Markdown
// or serialize `package` itself (blocks, instructions, facts, signature) as JSON.
```

To include PDB-derived signatures and named symbols, load a PDB first and pass it in:

```csharp
using ProfileExplorer.Profiling.Symbols;

using var pdb = new PdbSymbolProvider();
pdb.LoadDebugInfo(@"C:\symbols\target.pdb");

var result = FunctionAnalysisPackageBuilder.Build(binaryPath, address, symbolDebugInfo: pdb);
```

`FunctionAnalysisDetailLevel` (`Compact` / `Standard` / `Full`) controls how much per-instruction detail `ToPromptMarkdown()` includes, for callers with limited prompt budget.

For measuring coverage across many functions/binaries (e.g. before trusting the pipeline on a new symbol-poor binary), see `BenchmarkRunner.Run(IReadOnlyList<BenchmarkCase>, symbolProviderFactory)`, which reports success/signature-availability rates stratified by `SymbolAvailabilityTier` (`FullPdb` / `PublicSymbolsOnlyPdb` / `NoPdb`).

## What you get at each evidence tier

The single biggest driver of evidence quality is **what's available for the target binary**, not the code itself. There are three realistic tiers:

| You have | Function bounds | Disassembly + CFG | Register data flow | API/import resolution | Signature & parameter names |
|---|---|---|---|---|---|
| **Binary + full private PDB** | ✅ Exact, from symbols | ✅ | ✅ | ✅ | ✅ Full (return type, calling convention, parameter names/types) |
| **Binary + public-symbols-only PDB** (common for shipped/released binaries you don't own the source for) | ✅ From symbols | ✅ | ✅ | ✅ | ❌ No — public PDBs typically carry no function-type records at all |
| **Binary, no PDB at all** | ✅ Usually, from `.pdata` (x64/ARM64) — see gotcha below | ✅ | ✅ | ✅ | ❌ No |
| **No binary accessible** (not on disk, and not downloadable from a symbol/binary server) | ❌ | ❌ | ❌ | ❌ | ❌ — nothing is possible; the address stays an opaque `module!<unknown+0xRVA>` |

The practical takeaway: **getting the raw binary matters more than getting the PDB.** A binary with no PDB still gets full disassembly, CFG, and (crucially) resolved API calls/strings — often enough to write accurate pseudocode. No binary at all means nothing beyond the bare address.

## Limitations

- **This is function-scoped, not whole-program.** There is no cross-function type database, no persistent project, no interactive renaming/annotation. Each call analyzes one function in isolation (plus its immediate call/reference targets by name only).
- **No native decompiler.** Pseudocode is produced by an AI reading the evidence package, not by this codebase. Accuracy depends on both the evidence and the model.
- **No multi-hop constant/copy propagation.** Only a direct "load an immediate into a register" pattern is recognized as a provable constant. A value copied through several registers is *not* resolved back to its origin automatically — use `BackwardSlice` to see the dependency chain instead of a folded constant.
- **CFG merges with disagreeing definitions are reported as ambiguous, not guessed.** If two branches set a register to different values before a shared use, the tool will say so explicitly rather than picking one.
- **No inlined-callee frame recovery or ICF (identical-code-folding) disambiguation yet.** Heavily optimized code that inlines a helper, or folds two identical functions into one, is not yet specially handled — you may see one name/bounds when several source-level functions are really involved.
- **No demangled-name matching for `KnownApiFacts`.** C++ mangled import names (e.g. `operator new`) won't match the known-API table today; only plain C-style names do.
- **ARM64EC is only partially understood.** The pipeline can detect that a binary is ARM64EC, but does not yet disambiguate which regions are native ARM64 vs. x64-emulated code within one image.
- **The benchmark/accuracy-rubric infrastructure measures *coverage*, not AI accuracy.** It confirms how much evidence the static pipeline can produce for a given binary/PDB combination. Actually scoring generated pseudocode against real source requires a human/LLM review step that has to be run separately.

## Gotchas and things to watch for

- **Trivial leaf functions can have zero unwind (`.pdata`) entries on x64/ARM64.** A function with no stack frame and no calls (e.g. a one-line arithmetic helper) may not need unwind info at all under the ABI — confirmed with `dumpbin /unwindinfo`. If you have no PDB, such a function may be **unlocatable by RVA alone**, even though the CPU sample/allocation site pointed straight at it. This is a real, not-yet-worked-around gap for the smallest hot functions.
- **Public-symbols-only PDBs are common and give you names but no types.** Many shipped, publicly-released binaries — regardless of vendor, and this includes cases where you don't have private symbols for OS-provided code either — carry a PDB with function names and source lines but zero `SymTagFunction`/type records. You'll get a resolved name, but `Signature` will always be `null`. Don't treat a missing signature as a bug; check whether the PDB actually has private symbols first.
- **x64 and ARM64 both use a single unified calling convention.** `__stdcall`/`__fastcall` source annotations are no-ops on 64-bit targets, so don't be surprised when the recovered calling convention always reads `__cdecl`-equivalent there — x86 is the only architecture where the distinction is real.
- **Return addresses need `FrameAddressKind.ReturnAddress`, not `InstructionPointer`.** An address taken from a stack frame's return slot points *after* the call, not at the instruction that matters — pass the right `FrameAddressKind` or normalization/attribution will be off by one instruction.
- **A binary you fetch may not exactly match the one that produced your trace/stack.** Always pass the expected image identity (timestamp/size) when you have it, so a mismatched binary fails loudly instead of producing confidently-wrong analysis.
- **Absence of evidence is reported, not hidden.** Every package includes a `Facts` list with explicit notes like "no PDB signature available" or "block ends in an unresolved indirect call" — read it. A clean-looking package with no facts about missing data is more trustworthy than one that silently omitted the gaps.
