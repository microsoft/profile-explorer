// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ProfileExplorer.Core.Document.Renderers.Highlighters;

public enum DiffKind {
  None,
  Insertion,
  Deletion,
  Modification,
  MinorModification,
  Placeholder
}
