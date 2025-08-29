// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Media;

namespace ProfileExplorer.UI.Query;

public class FunctionTaskOptionsSerializer {
  public static byte[] Serialize(IFunctionTaskOptions inputObject) {
    try {
      // Save each property to a dictionary and serialize it in JSON format.
      // Protobuf is not used here because by default it cannot handle the generic object values.
      var fields = new Dictionary<string, object>();
      var inputType = inputObject.GetType();

      foreach (var property in inputType.GetProperties()) {
        if (!property.CanRead || !property.CanWrite) {
          continue;
        }

        fields[property.Name] = property.GetValue(inputObject);
      }

      return UIJsonUtils.SerializeToBytes(fields);
    }
    catch (Exception ex) {
      return null;
    }
  }

  public static IFunctionTaskOptions Deserialize(byte[] data, Type outputType) {
    try {
      if (!UIJsonUtils.DeserializeFromBytes<Dictionary<string, object>>(data, out var state)) {
        return null;
      }

      object outputObject = Activator.CreateInstance(outputType);

      foreach (var field in state) {
        var property = outputType.GetProperty(field.Key);
        var valueType = property.PropertyType;

        // Because the values are serialized/deserialized as a generic object,
        // instead of the proper type the values are plain JsonEleement (strings)
        // which have to be converted now to the property type.
        var jsonValue = (JsonElement)field.Value;
        object value = null;

        switch (valueType) {
          case var _ when valueType == typeof(bool): {
            value = jsonValue.GetBoolean();
            break;
          }
          case var _ when valueType == typeof(int): {
            value = jsonValue.GetInt32();
            break;
          }
          case var _ when valueType == typeof(string): {
            value = jsonValue.GetString();
            break;
          }
          case var _ when valueType == typeof(Color): {
            value = Utils.ColorFromString(jsonValue.GetString());
            break;
          }
        }

        property.SetValue(outputObject, value);
      }

      return (IFunctionTaskOptions)outputObject;
    }
    catch (Exception ex) {
      return null;
    }
  }
}