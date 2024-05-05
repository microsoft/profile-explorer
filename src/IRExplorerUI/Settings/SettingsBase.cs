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

  private delegate void VisitOptionAction(object settings, PropertyInfo property,
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
  
  public static void ResetAllOptions(object settings, Type type = null) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (settings, property, optionAttr, optionId) => {
        // Trace.WriteLine($"Resetting property {property.Name}, type {type.Name}: {optionAttr.Value}");
        if (optionAttr != null) {
          property.SetValue(settings, optionAttr.Value);
        }
      }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
      EmptyVisitNestedSettingsAction, false, false, type, visited);
  }
  
  public static void InitializeNewOptions(object settings, HashSet<OptionValueId> knownOptions) {
    var visited = new HashSet<object>();
    WalkSettingsOptions(settings, (settings, property, optionAttr, optionId) => {
        if (optionAttr != null && !knownOptions.Contains(optionId)) {
          // Trace.WriteLine($"Setting missing property {property.Name}, type {type.Name}: {optionAttr.Value}");
          property.SetValue(settings, optionAttr.Value);
        }
      }, EmptyVisitSettingsAction, EmptyVisitSettingsAction,
      EmptyVisitNestedSettingsAction, true, true, null, visited);
  }
  
  
  public static string PrintOptions(object settings, bool includeBaseClass = true) {
    var visited = new HashSet<object>();
    var sb = new StringBuilder();
    int currentLevel = 0;

    WalkSettingsOptions(settings, (settings, property, optionAttr, optionId) => {
        var value = property.GetValue(settings);
        sb.Append(' ', currentLevel * 4);
        sb.Append($"{property.Name}: {value}");

        if (optionAttr != null && optionAttr.Value != null) {
          if (value != null && value.Equals(optionAttr.Value)) {
            sb.Append($" (\u2713)");
          }
          else {
            sb.Append($" (default {optionAttr.Value})");
          }
        }

        sb.AppendLine();
      }, (settings, level) => {
        sb.Append(' ', currentLevel * 4);
        sb.AppendLine($"{settings.GetType().Name}:");
        currentLevel = level;
      },
      (settings, level) => {
        sb.Append(' ', currentLevel * 4);
        sb.AppendLine("------------------------");
        currentLevel = level;
      },
      (nestedSettings, property, isCollection) => {
        //? TODO: Pretty-print list/dict
      }, true, includeBaseClass, null, visited);

    return sb.ToString();
  }

  private static void WalkSettingsOptions(object settings, VisitOptionAction optionAction,
                                          VisitSettingsAction beginVisitSettingsAction,
                                          VisitSettingsAction endVisitSettingsAction,
                                          VisitNestedSettingsAction beginNestedSettingsAction,
                                   bool visitedNestedSettings, bool visitBaseClass, Type type,
                                   HashSet<object> visited, int level = 0) {
    if (settings == null) {
      return;
    }

    if (!visited.Add(settings)) {
      return; // Avoid cycles in the object graph.
    }

    if (type != null) {
      Debug.Assert(type.IsAssignableFrom(settings.GetType()));
    }
    else {
      type = settings.GetType();
    }

    var contractAttr = type.GetCustomAttribute<ProtoContractAttribute>();

    if (contractAttr == null) {
      return;
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
        optionAction(settings, property, optionAttr, optionId);
      }
      else if (!propertyIsSettings) {
        optionAction(settings, property, null, null);
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
