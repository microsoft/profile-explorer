// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorer.Core.Controls;
using ProfileExplorer.UI.Document;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract]
public class IRDocumentState {
  [ProtoMember(5)]
  public byte[] Bookmarks;
  [ProtoMember(7)]
  public int CaretOffset;
  [ProtoMember(1)]
  public ElementHighlighterState HoverHighlighter;
  [ProtoMember(4)]
  public DocumentMarginState Margin;
  [ProtoMember(3)]
  public ElementHighlighterState MarkedHighlighter;
  [ProtoMember(6)]
  public List<IRElementReference> SelectedElements;
  [ProtoMember(2)]
  public ElementHighlighterState SelectedHighlighter;
  [ProtoMember(8)]
  public ElementOverlayState ElementOverlays;
  public bool HasAnnotations => MarkedHighlighter.HasAnnotations || Margin.HasAnnotations;
}