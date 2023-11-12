// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Runtime.CompilerServices;

namespace IRExplorerCore;

public struct TextLocation {
  public int Offset {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }
  public int Line {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }
  public int Column {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public TextLocation(int offset, int line, int column) {
    Offset = offset;
    Line = line;
    Column = column;
  }

  public override string ToString() {
    return $"offset: {Offset}, line {Line}";
  }

  public override bool Equals(object obj) {
    return obj is TextLocation location &&
           Offset == location.Offset &&
           Line == location.Line &&
           Column == location.Column;
  }

  public override int GetHashCode() {
    int hashCode = -1429159632;
    hashCode = hashCode * -1521134295 + Offset.GetHashCode();
    hashCode = hashCode * -1521134295 + Line.GetHashCode();
    hashCode = hashCode * -1521134295 + Column.GetHashCode();
    return hashCode;
  }
}
