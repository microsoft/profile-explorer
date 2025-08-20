// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ProfileExplorerCore2.Controls;
using ProfileExplorerCore2.IR;
using ProtoBuf;
using ProtoBuf.Meta;

namespace ProfileExplorerCore2.Session;

static class StateSerializer {
  public static readonly int subtypeIdStep_ = 100;
  public static int nextSubtypeId_;

  static StateSerializer() {
    RuntimeTypeModel.Default.InternStrings = true;
  }

  public static void RegisterSurrogate<T1, T2>() {
    RegisterSurrogate(typeof(T1), typeof(T2));
  }

  public static void RegisterSurrogate(Type realType, Type surrogateType) {
    var model = RuntimeTypeModel.Default;
    model.Add(surrogateType);
    model.Add(realType, false).SetSurrogate(surrogateType);
  }

  public static void RegisterDerivedClass<T1, T2>(int id = 0) {
    RegisterDerivedClass(typeof(T1), typeof(T2), id);
  }

  public static void RegisterDerivedClass(Type derivedType, Type baseType, int id = 0) {
    var model = RuntimeTypeModel.Default;

    if (id == 0) {
      nextSubtypeId_ += subtypeIdStep_;
      id = nextSubtypeId_;
    }

    model.Add(baseType, false).AddSubType(id, derivedType);
  }

  public static byte[] Serialize<T>(T state, FunctionIR function = null) where T : class {
    using var stream = new MemoryStream();
    Serializer.Serialize(stream, state);
    return stream.ToArray();
  }

  public static bool Serialize<T>(string filePath, T state, FunctionIR function = null) where T : class {
    using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite);
    Serializer.Serialize(stream, state);
    return true;
  }

  public static T Deserialize<T>(byte[] data, FunctionIR function) where T : class {
    var value = Deserialize<T>(data);

    if (value != null) {
      PatchIRElementObjects(value, function);
      return value;
    }

    return null;
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

  public static T Deserialize<T>(object data, FunctionIR function) where T : class {
    return Deserialize<T>((byte[])data, function);
  }

  public static T Deserialize<T>(object data) where T : class {
    return Deserialize<T>((byte[])data);
  }

  public static void PatchIRElementObjects(object value, FunctionIR function) {
    if (value == null) {
      return;
    }

    if (value is IRElementReference elementRef) {
      elementRef.Value = function.GetElementWithId(elementRef.Id);
      return;
    }

    if (!value.GetType().GetTypeInfo().IsClass) {
      return; // Don't walk primitive types.
    }

    if (value is IList list) {
      foreach (object item in list) {
        PatchIRElementObjects(item, function);
      }
    }
    else if (value is IDictionary dict) {
      foreach (object item in dict.Keys) {
        PatchIRElementObjects(item, function);
      }

      foreach (object item in dict.Values) {
        PatchIRElementObjects(item, function);
      }
    }
    else {
      var fields = value.GetType().GetFields(BindingFlags.Public |
                                             BindingFlags.NonPublic |
                                             BindingFlags.Instance);

      foreach (var field in fields) {
        PatchIRElementObjects(field.GetValue(value), function);
      }
    }
  }
}