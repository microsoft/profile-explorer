// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.ASM;
using System;
using System.Text;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using System.IO;
using System.Threading.Tasks;

namespace IRExplorerUI.Compilers.ASM {
    public class DotNetCompilerInfoProvider : ASMCompilerInfoProvider {
        public DotNetCompilerInfoProvider(IRMode mode, ISession session) : base(mode, session) {
        }

        public override string CompilerIRName => "DotNet";

        public override string CompilerDisplayName => ".NET " + IR.Mode.ToString();

        public override string OpenFileFilter => ".NET ASM and Binary Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys|All Files|*.*";
        public override string OpenDebugFileFilter => "Debug Files|*.json|All Files|*.*";
        
        public override async Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
            document.DebugInfoFilePath = await FindDebugInfoFile(modulePath);
        }
    }
}
