// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public sealed class DummySectionLoader : IRTextSectionLoader {
    public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
        return null;
    }

    public override string GetDocumentOutputText() {
        return "";
    }

    public override byte[] GetDocumentTextBytes() {
        return null;
    }

    public override ParsedIRTextSection LoadSection(IRTextSection section) {
        return null;
    }

    public override string GetSectionText(IRTextSection section) {
        return "";
    }

    public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
        return ReadOnlyMemory<char>.Empty;
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
        return "";
    }

    public override string GetRawSectionPassOutput(IRPassOutput output) {
        return "";
    }

    public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
        return ReadOnlyMemory<char>.Empty;
    }

    public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
        return ReadOnlyMemory<char>.Empty;
    }

    protected override void Dispose(bool disposing) {

    }
}