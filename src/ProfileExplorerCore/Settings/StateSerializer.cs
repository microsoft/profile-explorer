// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using ProtoBuf;
using ProtoBuf.Meta;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Core state serializer for non-UI objects using protobuf.
/// For UI-specific serialization with surrogates, use ProfileExplorer.UI.StateSerializer.
/// </summary>
static class StateSerializer {
  public static readonly int subtypeIdStep_ = 100;
  public static int nextSubtypeId_;

  static StateSerializer() {
    RuntimeTypeModel.Default.InternStrings = true;
  }

  public static void RegisterDerivedClass<T1, T2>(int id = 0) {
    RegisterDerivedClass(typeof(T2), typeof(T1), id);
  }

  public static void RegisterDerivedClass(Type derivedType, Type baseType, int id = 0) {
    var model = RuntimeTypeModel.Default;

    if (id == 0) {
      nextSubtypeId_ += subtypeIdStep_;
      id = nextSubtypeId_;
    }

    model.Add(baseType, false).AddSubType(id, derivedType);
  }

  public static byte[] Serialize<T>(T state) where T : class {
    using var stream = new MemoryStream();
    Serializer.Serialize(stream, state);
    return stream.ToArray();
  }

  public static bool Serialize<T>(string filePath, T state) where T : class {
    using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite);
    Serializer.Serialize(stream, state);
    return true;
  }

  public static T Deserialize<T>(byte[] data) where T : class {
    if (data == null) {
      return null;
    }

    var stream = new MemoryStream(data);
    return Serializer.Deserialize<T>(stream);
  }

  public static T Deserialize<T>(string filePath) where T : class {
    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
    return Serializer.Deserialize<T>(stream);
  }
}
