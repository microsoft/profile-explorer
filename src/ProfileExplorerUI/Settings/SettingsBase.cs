// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

// A typical settings class should:
// - should inherit from SettingsBase and be marked with ProtoContract.
// - have a constructor that calls Reset() and is skipped by ProtoBuf.
// - have a ProtoMember for each setting with an OptionValue default value.
// - have a ProtoAfterDeserialization method that initializes reference members.
// - have a Reset method that calls ResetAllOptions.
// - have an Equals method that calls AreSettingsOptionsEqual.
// - have a ToString method that calls PrintOptions.
// - have a Clone method that serializes and deserializes the object.
//
// Example:
// [ProtoContract(SkipConstructor = true)]
// public class ExampleSettings : SettingsBase {
//   [ProtoMember(1), OptionValue("Default Value")]
//   public string SomeSetting { get; set; }
//
//   public ExampleSettings() {
//     Reset();
//   }
//
//   public override void Reset() {
//     InitializeReferenceMembers();
//     ResetAllOptions(this);
//   }
//
//   [ProtoAfterDeserialization]
//   private void InitializeReferenceMembers() {
//     // Initialize reference members here.
//   }
//
//   public override bool Equals(object obj) {
//     return AreSettingsOptionsEqual(this, obj);
//   }
//
//   public override string ToString() {
//     return PrintOptions(this);
//   }
//
//   public ExampleSettings Clone() {
//     byte[] serialized = StateSerializer.Serialize(this);
//     return StateSerializer.Deserialize<ExampleSettings>(serialized);
//   }
// }

// A unique identifier for an option value, combines the class name
// with the ProtoBuf ProtoMember Id tag, which should remain stable
// across versions of the app.
public record OptionValueId(string ClassName, int MemberId);

// Attribute to specify the default value of an option.
[AttributeUsage(AttributeTargets.All)]
public class OptionValueAttribute : Attribute {
  public object Value { get; set; }
  public bool CreateNewInstance { get; set; }

  public OptionValueAttribute() {
    // Create new object of type, calling default constructor.
    CreateNewInstance = true;
  }

  public OptionValueAttribute(object value) {
    // Set value to the primitive value passed in.
    Value = value;
  }
}

public class SettingsBase {
  private delegate bool VisitOptionAction(object settings, PropertyInfo property,
                                          OptionValueAttribute optionAttr, OptionValueId optionId);

  private delegate void VisitSettingsAction(object settings, int level);
  private delegate void VisitNestedSettingsAction(object nestedSettings, PropertyInfo property, bool isCollection);

  private static void EmptyVisitOptionAction(object settings, PropertyInfo property,
                                             OptionValueAttribute optionAttr, OptionValueId optionId) {
  }

  private static void EmptyVisitSettingsAction(object settings, int level) { }

  private static void EmptyVisitNestedSettingsAction(object nestedSettings, PropertyInfo property, bool isCollection) {
  }

  public virtual void Reset() { }

  public static HashSet<OptionValueId> CollectOptionMembers(object settings) {
    var set = new HashSet<OptionValueId>();
    CollectOptionMembers(settings, set);
    return set;
  }

  public static void CollectOptionMembers(object settings, HashSet<OptionValueId> set) {
    var type = settings.GetType();
    var visited = new HashSet<Type>();
    CollectOptionMembers(type, set, visited);
  }

  private static ProtoIncludeAttribute FindMatchingProtoInclude(Type baseType, Type derivedType) {
    foreach (var attr in baseType.GetCustomAttributes<ProtoIncludeAttribute>()) {
      if (attr.KnownType == derivedType) {
        return attr;
      }
    }

    return null;
  }

  public static void ResetAllOptions(object settings, Type type = null,
                                     bool resetNestedSettings = true,
                                     bool resetToNew = true) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (obj, property, optionAttr, optionId) => {
                          // Trace.WriteLine($"Resetting property {property.Name}, type {type.Name}: {optionAttr.Value}");
                          if (optionAttr != null) {
                            SetOptionValue(property, obj, optionAttr);
                            return true;
                          }

                          if (resetNestedSettings &&
                              property.GetValue(obj) is SettingsBase nestedSettings) {
                            nestedSettings.Reset();
                          }
                          else if (resetToNew) {
                            if (property.GetValue(obj) is IList list) {
                              list.Clear();
                            }
                            else if (property.GetValue(obj) is IDictionary dict) {
                              dict.Clear();
                            }
                            else if (property.GetSetMethod() != null) {
                              object newObject = Activator.CreateInstance(property.PropertyType);
                              property.SetValue(obj, newObject);
                            }
                          }

                          return true;
                        }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
                        EmptyVisitNestedSettingsAction, false, false, type, visited);
  }

  private static void SetOptionValue(PropertyInfo property, object obj,
                                     OptionValueAttribute optionAttr) {
    if (property.GetSetMethod() == null) return;

    if (optionAttr.CreateNewInstance) {
      property.SetValue(obj, Activator.CreateInstance(property.PropertyType));
    }
    else if (optionAttr.Value != null) {
      if (optionAttr.Value.GetType() == property.PropertyType) {
        property.SetValue(obj, optionAttr.Value);
      }
      else if (optionAttr.Value.GetType().IsPrimitive) {
        object convertedValue = Convert.ChangeType(optionAttr.Value, property.PropertyType);
        property.SetValue(obj, convertedValue);
      }
      else if (optionAttr.Value is string strValue) {
        // Convert from string to a known type.
        if (property.PropertyType == typeof(Color)) {
          property.SetValue(obj, Utils.ColorFromString(strValue));
        }
        else {
          throw new InvalidOperationException("Type not handled");
        }
      }
      else if (optionAttr.Value is string[] strArray) {
        // Convert from multiple strings to a an array.
        if (property.PropertyType == typeof(Color[])) {
          var colors = new Color[strArray.Length];

          for (int i = 0; i < strArray.Length; i++) {
            colors[i] = Utils.ColorFromString(strArray[i]);
          }

          property.SetValue(obj, colors);
        }
        else {
          throw new InvalidOperationException("Type not handled");
        }
      }
      else {
        throw new InvalidOperationException("Type not handled");
      }
    }
  }

  public static void InitializeAllNewOptions(object settings, HashSet<OptionValueId> knownOptions) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (obj, property, optionAttr, optionId) => {
                          // Initialize only missing properties.
                          if (optionAttr != null && knownOptions != null && !knownOptions.Contains(optionId)) {
                            // Trace.WriteLine($"Setting missing property {property.Name}, type {type.Name}: {optionAttr.Value}");
                            SetOptionValue(property, obj, optionAttr);
                          }

                          return true;
                        }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
                        EmptyVisitNestedSettingsAction, true, true, null, visited);
  }

  public static void InitializeReferenceOptions(object settings) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (obj, property, optionAttr, optionId) => {
                          if (!property.GetType().IsValueType &&
                              property.GetValue(obj) == null &&
                              (optionAttr == null || optionAttr.CreateNewInstance) &&
                              property.GetSetMethod() != null) {
                            // Initialize all reference properties with an instance.
                            object newObject = Activator.CreateInstance(property.PropertyType);
                            property.SetValue(obj, newObject);
                          }

                          return true;
                        }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
                        EmptyVisitNestedSettingsAction, false, true, null, visited);
  }

  public static string PrintOptions(object settings, Type type = null,
                                    bool includeBaseClass = true) {
    var visited = new HashSet<object>();
    var sb = new StringBuilder();
    int currentLevel = 0;

    if (type != null) {
      Debug.Assert(type.IsAssignableFrom(settings.GetType()));
    }
    else {
      type = settings.GetType();
    }

    WalkSettingsOptions(settings, (obj, property, optionAttr, optionId) => {
                          object value = property.GetValue(obj);

                          if (value is SettingsBase) {
                            return true; // Printed as sub-section.
                          }

                          sb.Append(' ', currentLevel * 4);
                          sb.Append($"{property.Name}: {value}");

                          if (optionAttr != null && optionAttr.Value != null) {
                            if (value != null && AreValuesEqual(value, optionAttr.Value)) {
                              sb.Append($"  (default \u2713)");
                            }
                            else {
                              sb.Append($"  (default {optionAttr.Value})");
                            }
                          }

                          sb.AppendLine();
                          return true;
                        }, (settings, level) => {
                          sb.Append(' ', currentLevel * 4);
                          sb.AppendLine($"{settings.GetType().Name}:");
                          currentLevel = level;
                        },
                        (settings, level) => {
                          sb.Append(' ', currentLevel * 4);
                          sb.AppendLine("--------------------------------------");
                          currentLevel = level;
                        },
                        (nestedSettings, property, isCollection) => {
                          //? TODO: Pretty-print list/dict
                        }, true, includeBaseClass, type, visited);

    return sb.ToString();
  }

  private static bool AreValuesEqual(object a, object b, bool
                                       compareNestedSettings = false) {
    if (ReferenceEquals(a, b)) {
      return true;
    }
    else if (a == null || b == null) {
      return false;
    }

    switch ((a, b)) {
      case (double da, double db): {
        return Math.Abs(da - db) < double.Epsilon;
        break;
      }
      case (float fa, float fb): {
        return Math.Abs(fa - fb) < float.Epsilon;
        break;
      }
      case (double da, int ib): {
        return Math.Abs(da - ib) < double.Epsilon;
        break;
      }
      case (float fa, int ib): {
        return Math.Abs(fa - ib) < float.Epsilon;
        break;
      }
      case (string sa, string sb): {
        return sa.Equals(sb, StringComparison.Ordinal);
      }
      case (SettingsBase settingsA, SettingsBase settingsB): {
        if (compareNestedSettings) {
          // If Equals was overridden use it, otherwise
          // recursively compare each property in nested settings.
          var equals = settingsA.GetType().GetMethod("Equals");

          if (equals == null || equals.DeclaringType != settingsA.GetType()) {
            return AreOptionsEqual(settingsA, settingsB, null, false, compareNestedSettings);
          }
        }

        return settingsA.Equals(settingsB);
      }
      case (IList listA, IList listB): {
        // Compare each element of the collection.
        return listA.AreEqual(listB);
      }
      case (IDictionary dictA, IDictionary dictB): {
        // Compare each element of the collection.
        return dictA.AreEqual(dictB);
      }
      case (IEnumerable enumA, IEnumerable enumB): {
        // Compare each element of the collection.
        var iterA = enumA.GetEnumerator();
        var iterB = enumB.GetEnumerator();
        bool hasA = iterA.MoveNext();
        bool hasB = iterB.MoveNext();

        while (hasA && hasB) {
          if (!Equals(iterA.Current, iterB.Current)) {
            return false;
          }

          hasA = iterA.MoveNext();
          hasB = iterB.MoveNext();

          if (!hasA && !hasB) {
            return true;
          }
        }

        return false;
      }
      default: {
        return a.Equals(b);
      }
    }
  }

  public static bool AreOptionsEqual(object settingsA, object settingsB,
                                     Type type = null, bool compareBaseClass = false,
                                     bool compareNestedSettings = true) {
    if (settingsA == null || settingsB == null ||
        settingsA.GetType() != settingsB.GetType()) {
      return false;
    }
    else if (ReferenceEquals(settingsA, settingsB)) {
      return true;
    }

    if (type != null) {
      Debug.Assert(type.IsAssignableFrom(settingsA.GetType()));
    }
    else {
      type = settingsA.GetType();
    }

    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    if (!compareBaseClass) {
      flags |= BindingFlags.DeclaredOnly;
    }

    foreach (var property in type.GetProperties(flags)) {
      object valueA = property.GetValue(settingsA);
      object valueB = property.GetValue(settingsB);

      if (!AreValuesEqual(valueA, valueB, compareNestedSettings)) {
        return false;
      }
    }

    return true;
  }

  private static bool WalkSettingsOptions(object settings, VisitOptionAction optionAction,
                                          VisitSettingsAction beginVisitSettingsAction,
                                          VisitSettingsAction endVisitSettingsAction,
                                          VisitNestedSettingsAction beginNestedSettingsAction,
                                          bool visitedNestedSettings, bool visitBaseClass,
                                          Type type, HashSet<object> visited, int level = 0) {
    if (settings == null || !visited.Add(settings)) {
      return true; // Avoid cycles in the object graph.
    }

    if (type != null) {
      Debug.Assert(type.IsAssignableFrom(settings.GetType()));
    }
    else {
      type = settings.GetType();
    }

    var contractAttr = type.GetCustomAttribute<ProtoContractAttribute>();

    if (contractAttr == null) {
      return true;
    }

    beginVisitSettingsAction?.Invoke(settings, level);
    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    if (!visitBaseClass) {
      // When resetting don't consider properties from the base class.
      flags |= BindingFlags.DeclaredOnly;
    }

    foreach (var property in type.GetProperties(flags)) {
      var protoAttr = property.GetCustomAttribute<ProtoMemberAttribute>();
      if (protoAttr == null) continue;

      // Check if there is a default option value attribute attached.
      var optionAttr = property.GetCustomAttribute<OptionValueAttribute>();
      bool propertyIsSettings = property.PropertyType.BaseType == typeof(SettingsBase);

      if (optionAttr != null) {
        var optionId = MakeOptionId(property, type, contractAttr, protoAttr);

        if (!optionAction(settings, property, optionAttr, optionId)) {
          return false;
        }
      }
      else {
        if (!optionAction(settings, property, null, null)) {
          return false;
        }
      }

      if (!visitedNestedSettings) {
        continue; // When resetting don't handle base class and nested settings.
      }

      // Recursively go over nested settings.
      if (propertyIsSettings) {
        object value = property.GetValue(settings);

        if (value is SettingsBase nestedSettings) {
          beginNestedSettingsAction?.Invoke(nestedSettings, property, false);
          WalkSettingsOptions(nestedSettings, optionAction,
                              beginVisitSettingsAction, endVisitSettingsAction,
                              beginNestedSettingsAction,
                              visitedNestedSettings, visitBaseClass,
                              null, visited, level + 1);
        }
      }
      else if (property.PropertyType.IsGenericType &&
               property.PropertyType.GetGenericTypeDefinition() == typeof(List<>)) {
        if (IsSettingsBaseGenericType(property) &&
            property.GetValue(settings) is IEnumerable enumValue) {
          beginNestedSettingsAction?.Invoke(enumValue, property, true);

          foreach (object nestedValue in enumValue) {
            if (nestedValue is SettingsBase nestedSettings) {
              WalkSettingsOptions(nestedSettings, optionAction,
                                  beginVisitSettingsAction, endVisitSettingsAction,
                                  beginNestedSettingsAction,
                                  visitedNestedSettings, visitBaseClass,
                                  null, visited, level + 1);
            }
          }
        }
      }
      else if (property.PropertyType.IsGenericType &&
               property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
        if (IsSettingsBaseGenericType(property) &&
            property.GetValue(settings) is IDictionary dictValue) {
          beginNestedSettingsAction?.Invoke(dictValue, property, true);

          foreach (DictionaryEntry kvp in dictValue) {
            if (kvp.Key is SettingsBase keySettings) {
              WalkSettingsOptions(keySettings, optionAction,
                                  beginVisitSettingsAction, endVisitSettingsAction,
                                  beginNestedSettingsAction,
                                  visitedNestedSettings, visitBaseClass,
                                  null, visited, level + 1);
            }

            if (kvp.Value is SettingsBase valueSettings) {
              WalkSettingsOptions(valueSettings, optionAction,
                                  beginVisitSettingsAction, endVisitSettingsAction,
                                  beginNestedSettingsAction,
                                  visitedNestedSettings, visitBaseClass,
                                  null, visited, level + 1);
            }
          }
        }
      }
    }

    endVisitSettingsAction?.Invoke(settings, Math.Max(0, level - 1));
    return true;
  }

  private static bool IsSettingsBaseGenericType(PropertyInfo property) {
    foreach (var genericType in property.PropertyType.GenericTypeArguments) {
      if (genericType.BaseType == typeof(SettingsBase)) {
        return true;
      }
    }

    return false;
  }

  private static void CollectOptionMembers(Type type, HashSet<OptionValueId> set,
                                           HashSet<Type> visited) {
    if (!visited.Add(type)) {
      return; // Avoid cycles in the type graph.
    }

    var contractAttr = type.GetCustomAttribute<ProtoContractAttribute>();
    if (contractAttr == null) return;

    foreach (var property in type.GetProperties()) {
      var protoAttr = property.GetCustomAttribute<ProtoMemberAttribute>();
      if (protoAttr == null) continue;

      var optionId = MakeOptionId(property, type, contractAttr, protoAttr);
      set.Add(optionId);

      if (property.PropertyType.BaseType == typeof(SettingsBase)) {
        CollectOptionMembers(property.PropertyType, set, visited);
      }
      else if (property.PropertyType.IsGenericType &&
               (property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) ||
                property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))) {
        // Go over generic types of in List<T> and Dictionary<K,V>.
        foreach (var genericType in property.PropertyType.GenericTypeArguments) {
          if (genericType.BaseType == typeof(SettingsBase)) {
            CollectOptionMembers(genericType, set, visited);
          }
        }
      }
    }
  }

  private static OptionValueId MakeOptionId(PropertyInfo property, Type type, ProtoContractAttribute contractAttr,
                                            ProtoMemberAttribute protoAttr) {
    string className = !string.IsNullOrEmpty(contractAttr.Name) ?
      contractAttr.Name : type.Name;
    int id = protoAttr.Tag;

    if (property.DeclaringType != type) {
      // Members from base classes have an Id that is at an offset
      // defined by a ProtoInclude attribute. Find the ProtoInclude that corresponds
      // to this derived type.
      var protoIncludeAttr = FindMatchingProtoInclude(property.DeclaringType, type);

      if (protoIncludeAttr != null) {
        id += protoIncludeAttr.Tag;
      }
    }

    var optionId = new OptionValueId(className, id);
    return optionId;
  }
}