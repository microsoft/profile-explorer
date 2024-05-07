using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Primitives;
using ProtoBuf;

namespace IRExplorerUI;

public class SettingsBase {
  public record OptionValueId(string ClassName, int MemberId);

  private delegate bool VisitOptionAction(object settings, PropertyInfo property,
                                   OptionValueAttribute optionAttr, OptionValueId optionId);
  private delegate void VisitSettingsAction(object settings, int level);
  private delegate void VisitNestedSettingsAction(object nestedSettings, PropertyInfo property, bool isCollection);
  
  private static void EmptyVisitOptionAction(object settings, PropertyInfo property,
                                      OptionValueAttribute optionAttr, OptionValueId optionId) {}
  private static void EmptyVisitSettingsAction(object settings, int level) {}
  private static void EmptyVisitNestedSettingsAction(object nestedSettings, PropertyInfo property, bool isCollection) {}
  
  public virtual void Reset() { }

  public virtual bool HasChanges(SettingsBase other) {
    return !other.Equals(this);
  }

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
          if (property.GetSetMethod() != null) {
            property.SetValue(obj, optionAttr.Value);
          }

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
            var newObject = Activator.CreateInstance(property.PropertyType);
            property.SetValue(obj, newObject);
          }
        }

        return true;
      }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
      EmptyVisitNestedSettingsAction, false, false, type, visited);
  }

  public static void InitializeAllNewOptions(object settings, HashSet<OptionValueId> knownOptions) {
    InitializeNewOptions(settings, knownOptions, true);
  }
  
  public static void InitializeReferenceOptions(object settings) {
    InitializeNewOptions(settings, null, false);
  }

  private static void InitializeNewOptions(object settings, HashSet<OptionValueId> knownOptions = null,
                                          bool visitNestedSettings = false) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (obj, property, optionAttr, optionId) => {
        if (knownOptions != null &&
            knownOptions.Contains(optionId)) {
          return true; // Option already initialized.
        }
        
        if (optionAttr != null) {
          // Trace.WriteLine($"Setting missing property {property.Name}, type {type.Name}: {optionAttr.Value}");
          if (property.GetSetMethod() != null) {
            property.SetValue(obj, optionAttr.Value);
          }
        }
        else if (property.GetValue(obj) == null &&
                 property.GetSetMethod() != null) {
          // If no default value defined, set to new instance.
          var newObject = Activator.CreateInstance(property.PropertyType);
          property.SetValue(obj, newObject);
        }
        return true;
      }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
      EmptyVisitNestedSettingsAction, visitNestedSettings, true, null, visited);
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
        var value = property.GetValue(obj);

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
            return AreSettingsOptionsEqual(settingsA, settingsB, null, false, compareNestedSettings);
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
  
  public static bool AreSettingsOptionsEqual(object settingsA, object settingsB,
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
      var valueA = property.GetValue(settingsA);
      var valueB = property.GetValue(settingsB);

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
        var value = property.GetValue(settings);

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
          
          foreach (var nestedValue in enumValue) {
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
                null, visited, level + 1); }
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
    var className = !string.IsNullOrEmpty(contractAttr.Name) ?
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
