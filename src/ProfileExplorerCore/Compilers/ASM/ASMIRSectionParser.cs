// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Parser;

namespace ProfileExplorer.Core.Compilers.ASM;

public sealed class ASMIRSectionParser : IRSectionParser {
  private ICompilerIRInfo irInfo_;
  private IRParsingErrorHandler errorHandler_;
  private long functionSize_;

  public ASMIRSectionParser(long functionSize, ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler) {
    functionSize_ = functionSize;
    irInfo_ = irInfo;
    errorHandler_ = errorHandler;
  }

  public FunctionIR ParseSection(IRTextSection section, string sectionText) {
    return new ASMParser(irInfo_, errorHandler_,
                         RegisterTables.SelectRegisterTable(irInfo_.Mode),
                         sectionText, section, functionSize_).Parse();
  }

  public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) {
    return new ASMParser(irInfo_, errorHandler_,
                         RegisterTables.SelectRegisterTable(irInfo_.Mode),
                         sectionText, section, functionSize_).Parse();
  }

  public void SkipCurrentToken() {
    throw new NotImplementedException();
  }

  public void SkipToFunctionEnd() {
    throw new NotImplementedException();
  }

  public void SkipToLineEnd() {
    throw new NotImplementedException();
  }

  public void SkipToLineStart() {
    throw new NotImplementedException();
  }

  public void SkipToNextBlock() {
    throw new NotImplementedException();
  }
}