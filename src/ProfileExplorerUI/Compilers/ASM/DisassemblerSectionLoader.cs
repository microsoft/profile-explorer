// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorerCore2;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Providers;

namespace ProfileExplorer.UI.Compilers;

public sealed class DisassemblerSectionLoader : IRTextSectionLoader {
  private IRTextSummary summary_;
  private string binaryFilePath_;
  private Disassembler disassembler_;
  private IDebugInfoProvider debugInfo_;
  private ICompilerInfoProvider compilerInfo_;
  private Dictionary<IRTextFunction, FunctionDebugInfo> funcToDebugInfoMap_;
  private bool isManagedImage_;
  private bool preloadFunctions_;
  private DebugFileSearchResult debugInfoFile_;
  private bool resolveCallTargetNames_;

  public DisassemblerSectionLoader(string binaryFilePath, ICompilerInfoProvider compilerInfo,
                                   IDebugInfoProvider debugInfo, bool preloadFunctions = true) {
    Initialize(compilerInfo.IR, false);
    binaryFilePath_ = binaryFilePath;
    compilerInfo_ = compilerInfo;
    debugInfo_ = debugInfo;
    preloadFunctions_ = preloadFunctions;
    isManagedImage_ = debugInfo != null;
    summary_ = new IRTextSummary();
    funcToDebugInfoMap_ = new Dictionary<IRTextFunction, FunctionDebugInfo>();
  }

  public IDebugInfoProvider DebugInfo {
    get => debugInfo_;
    set => debugInfo_ = value;
  }

  public DebugFileSearchResult DebugInfoFile {
    get => debugInfoFile_;
    set => debugInfoFile_ = value;
  }

  public bool ResolveCallTargetNames {
    get => resolveCallTargetNames_;
    set {
      resolveCallTargetNames_ = value;

      if (disassembler_ != null) {
        if (value) {
          disassembler_.UseSymbolNameResolver(null);
        }
        else {
          disassembler_.UseSymbolNameResolver((value) => null);
        }
      }
    }
  }

  public void RegisterFunction(IRTextFunction function, FunctionDebugInfo debugInfo) {
    funcToDebugInfoMap_[function] = debugInfo;
  }

  public void Initialize(IDebugInfoProvider debugInfo) {
    debugInfo_ = debugInfo;
    InitializeDisassembler();
  }

  public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
    progressHandler?.Invoke(null, new SectionReaderProgressInfo(true));

    if (debugInfo_ == null) {
      if (preloadFunctions_) {
        // When opening in non-profiling mode, lookup the debug info now.
        debugInfoFile_ = compilerInfo_.FindDebugInfoFile(binaryFilePath_);
        debugInfo_ = compilerInfo_.CreateDebugInfoProvider(debugInfoFile_);
      }

      if (debugInfo_ == null) {
        return summary_;
      }
    }

    InitializeDisassembler();

    if (preloadFunctions_) {
      // Used when opening a binary directly.
      var funcList = debugInfo_.GetSortedFunctions();

      foreach (var funcInfo in funcList) {
        if (funcInfo.RVA == 0) {
          continue; // Some entries don't represent real functions.
        }

        // The debug info function list can have duplicates, ignore them.
        var func = summary_.FindFunction(funcInfo.Name);

        if (func == null) {
          func = new IRTextFunction(funcInfo.Name);
          var section = new IRTextSection(func, func.Name, IRPassOutput.Empty);
          func.AddSection(section);
          summary_.AddFunction(func);
          summary_.AddSection(section);
          funcToDebugInfoMap_[func] = funcInfo;
        }
      }
    }

    progressHandler?.Invoke(null, new SectionReaderProgressInfo(false));
    return summary_;
  }

  private bool InitializeDisassembler() {
    if (!isManagedImage_) {
      // This preloads all code sections in the binary.
      disassembler_ = Disassembler.CreateForBinary(binaryFilePath_, debugInfo_,
                                                   compilerInfo_.NameProvider.FormatFunctionName);
      return true;
    }

    // For managed code, the code data is found on each function.
    if (debugInfo_.LoadDebugInfo(null)) {
      disassembler_ = Disassembler.CreateForMachine(debugInfo_, compilerInfo_.NameProvider.FormatFunctionName);
      return true;
    }

    return false;
  }

  public override string GetDocumentOutputText() {
    return "";
  }

  public override byte[] GetDocumentTextBytes() {
    return new byte[] { };
  }

  public override ParsedIRTextSection LoadSection(IRTextSection section) {
    string text = GetSectionText(section);

    if (string.IsNullOrEmpty(text)) {
      return null;
    }

    // Function size needed by parser to properly set instr. sizes.
    long functionSize = 0;

    if (funcToDebugInfoMap_.TryGetValue(section.ParentFunction, out var funcInfo)) {
      functionSize = funcInfo.Size;
    }

    var (sectionParser, errorHandler) = InitializeParser(functionSize);
    FunctionIR function;

    if (sectionParser == null) {
      function = new FunctionIR();
    }
    else {
      function = sectionParser.ParseSection(section, text);
    }

    return new ParsedIRTextSection(section, text.AsMemory(), function);
  }

  public override string GetSectionText(IRTextSection section) {
    if (disassembler_ == null) {
      return null; // Failed to initialize.
    }

    if (!funcToDebugInfoMap_.TryGetValue(section.ParentFunction, out var funcInfo)) {
      return "";
    }

    if (isManagedImage_) {
      // For managed code, the code data is found on each function as a byte array.
      var methodCode = ((DotNetDebugInfoProvider)debugInfo_).FindMethodCode(funcInfo);

      if (methodCode != null) {
        byte[] code = methodCode.Code;

        if (code != null) {
          disassembler_.UseSymbolNameResolver(address => methodCode.FindCallTarget(address));
          return disassembler_.DisassembleToText(code, funcInfo.StartRVA);
        }
      }

      return "";
    }

    return disassembler_.DisassembleToText(funcInfo);
  }

  public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
    return GetSectionText(section).AsMemory();
  }

  public override string GetSectionOutputText(IRPassOutput output) {
    return "";
  }

  public override ReadOnlyMemory<char> GetSectionPassOutputTextSpan(IRPassOutput output) {
    return ReadOnlyMemory<char>.Empty;
  }

  public override List<string> GetSectionPassOutputTextLines(IRPassOutput output) {
    return new List<string>();
  }

  public override string GetRawSectionText(IRTextSection section) {
    return GetSectionText(section);
  }

  public override string GetRawSectionPassOutput(IRPassOutput output) {
    return "";
  }

  public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
    return GetRawSectionText(section).AsMemory();
  }

  public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
    return ReadOnlyMemory<char>.Empty;
  }

  protected override void Dispose(bool disposing) {
    disassembler_?.Dispose();
    debugInfo_?.Dispose();
  }
}