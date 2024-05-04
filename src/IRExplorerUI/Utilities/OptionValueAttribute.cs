using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

public record OptionValueId(string ClassName, int MemberId);

[AttributeUsage(AttributeTargets.All)]
public class OptionValueAttribute : Attribute {
  //? Make Optional protobuf-aware, maybe use the surrogate to have it in UI project
  //? TODO: Static func that inits all Optionals with the value if not set
  //?   - reset only missing (from settings.AfterDeserialize)
  //?   - reset everything (from settings.Reset())

  public object Value { get; set; }

  public OptionValueAttribute(object value) {
    Value = value;
  }
  
  public OptionValueAttribute(Type type, string convertedValue) {
    if (type == typeof(Color)) {
      Value = Utils.ColorFromString(convertedValue);
    }
  }

  public static void CollectOptionMembers(object settings, HashSet<OptionValueId> set) {
    var type = settings.GetType();
    CollectOptionMembers(type, set);
  }

  private static ProtoIncludeAttribute FindMatchingProtoInclude(Type baseType, Type derivedType) {
    foreach (var attr in baseType.GetCustomAttributes(typeof(ProtoIncludeAttribute))) {
      if (attr is ProtoIncludeAttribute protoIncludeAttr &&
          protoIncludeAttr.KnownType == derivedType) {
        return protoIncludeAttr;
      }
    }

    return null;
  }
  
  public static void ResetAllOptions(object settings) {
    ResetOptions(settings, null, false);
  }
  
  public static void InitializeNewOptions(object settings, HashSet<OptionValueId> knownOptions) {
    ResetOptions(settings, knownOptions, true);
  }
  
  private static void ResetOptions(object settings, HashSet<OptionValueId> knownOptions,
                                   bool initializeOnlyMissingOptions) {
    if (settings == null) {
      return;
    }
    
    var type = settings.GetType();
    var contractAttr = type.GetCustomAttribute(typeof(ProtoContractAttribute)) as ProtoContractAttribute;

    if (contractAttr == null) {
      return;
    }
    
    foreach (var property in type.GetProperties()) {
      var protoAttr = property.GetCustomAttribute(typeof(ProtoMemberAttribute)) as ProtoMemberAttribute;
      if (protoAttr == null) continue;

      if (property.GetCustomAttribute(typeof(OptionValueAttribute)) is OptionValueAttribute optionAttr) {
        bool reset = true;
        
        if (initializeOnlyMissingOptions) {
          var optionId = MakeOptionId(property, type, contractAttr, protoAttr);

          if (knownOptions.Contains(optionId)) {
            reset = false;
          }
        }

        if (reset) {
          property.SetValue(settings, optionAttr.Value);
        }
      }
      
      // Recursively go over nested settings.
      if (!initializeOnlyMissingOptions) continue;

      if (property.PropertyType.BaseType == typeof(SettingsBase)) {
        var value = property.GetValue(settings);

        if (value is SettingsBase nestedSettings) {
          ResetOptions(nestedSettings, knownOptions, initializeOnlyMissingOptions);
        }
      }
      else if (property.PropertyType.IsGenericType &&
               property.PropertyType.GetGenericTypeDefinition() == typeof(List<>)) {
        if (IsSettingsBaseGenericType(property) &&
            property.GetValue(settings) is IEnumerable enumValue) {
          foreach (var nestedValue in enumValue) {
            if (nestedValue is SettingsBase nestedSettings) {
              ResetOptions(nestedSettings, knownOptions, true);
            }
          }
        }
      }
      else if (property.PropertyType.IsGenericType &&
               property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
        if (IsSettingsBaseGenericType(property) &&
            property.GetValue(settings) is IDictionary dictValue) {
          foreach (DictionaryEntry kvp in dictValue) {
            if (kvp.Key is SettingsBase keySettings) {
              ResetOptions(keySettings, knownOptions, true);
            }
            if (kvp.Value is SettingsBase valueSettings) {
              ResetOptions(valueSettings, knownOptions, true);
            }
          }
        }
      }
    }
  }

  private static bool IsSettingsBaseGenericType(PropertyInfo property) {
    foreach (var genericType in property.PropertyType.GenericTypeArguments) {
      if (genericType.BaseType == typeof(SettingsBase)) {
        return true;
      }
    }

    return false;
  }

  private static void CollectOptionMembers(Type type, HashSet<OptionValueId> set) {
    var contractAttr = type.GetCustomAttribute(typeof(ProtoContractAttribute)) as ProtoContractAttribute;

    if (contractAttr == null) {
      return;
    }
    
    //? TODO: Cycle detection, same for patching
    
    foreach (var property in type.GetProperties()) {
      var protoAttr = property.GetCustomAttribute(typeof(ProtoMemberAttribute)) as ProtoMemberAttribute;
      if (protoAttr == null) continue;
      
      var optionId = MakeOptionId(property, type, contractAttr, protoAttr);
      set.Add(optionId);

      if (property.PropertyType.BaseType == typeof(SettingsBase)) {
        CollectOptionMembers(property.PropertyType, set);
      }
      else if (property.PropertyType.IsGenericType && 
               (property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) ||
                property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))) {
        // Go over generic types of in List<T> and Dictionary<K,V>.
        foreach (var genericType in property.PropertyType.GenericTypeArguments) {
          if (genericType.BaseType == typeof(SettingsBase)) {
            CollectOptionMembers(genericType, set);
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
