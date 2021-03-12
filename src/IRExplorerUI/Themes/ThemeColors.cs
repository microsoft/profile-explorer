// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract]
    public class ThemeColorSet : IEnumerable<KeyValuePair<string, Color>> {
        public Dictionary<string, Color> ColorValues { get; set; }
        public Guid Id { get; set; }
        public int Count => ColorValues.Count;

        public ThemeColorSet(Guid id) {
            ColorValues = new Dictionary<string, Color>();
            Id = id;
        }

        public ThemeColorSet Clone() {
            return new ThemeColorSet(Id) {
                ColorValues = ColorValues.CloneDictionary()
            };
        }

        public ThemeColorSet CombineWith(ThemeColorSet other) {
            ColorValues.CombineWith(other.ColorValues);
            return this;
        }

        // Support for dictionary collection initializer,
        // used when defining the default colors.
        public IEnumerator<KeyValuePair<string, Color>> GetEnumerator() {
            return ColorValues.GetEnumerator();
        }
        
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public void Add(string key, Color value) {
            ColorValues[key] = value;
        }

        public bool ContainsKey(string key) {
            return ColorValues.ContainsKey(key);
        }

        public bool TryGetValue(string key, out Color value) {
            return ColorValues.TryGetValue(key, out value);
        }

        public Color this[string key] {
            get => ColorValues[key];
            set => Add(key, value);
        }
        
        public override bool Equals(object obj) {
            return obj is ThemeColorSet other && ColorValues.IsEqual(other.ColorValues);
        }

        public void Clear() {
            ColorValues.Clear();
        }
    }
    
    [ProtoContract(SkipConstructor = true)]
    public class ThemeColors {
        [ProtoMember(1)]
        private Dictionary<Guid, ThemeColorSet> colorSets_;
        [ProtoMember(2)]
        private Dictionary<string, Color> defaultColorValues_;

        public ThemeColors DefaultTheme { get; set; }
        
        public ThemeColors() {
            InitializeReferenceMembers();
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            colorSets_ ??= new Dictionary<Guid, ThemeColorSet>();
            defaultColorValues_ ??= new Dictionary<string, Color>();
        }

        public void AddColorSet(ThemeColorSet themeColorSet) {
            colorSets_[themeColorSet.Id] = themeColorSet;
        }

        public void RemoveColorSet(Guid id) {
            if (colorSets_.ContainsKey(id)) {
                colorSets_.Remove(id);
            }
        }
        
        public void SetColor(Guid id, string valueName, Color color) {
            var colorSet = GetOrCreateColorSet(id);
            colorSet[valueName] = color;
        }
        
        public bool HasCustomColor(Guid id, string valueName) {
            return colorSets_.TryGetValue(id, out var colorSet) &&
                   colorSet.ContainsKey(valueName);
        }
        
        public void SetDefaultColor(string valueName, Color color) {
            defaultColorValues_[valueName] = color;
        }

        public Color GetColor(Guid id, string valueName) {
            if (colorSets_.TryGetValue(id, out var colorSet) &&
                colorSet.TryGetValue(valueName, out var color)) {
                return color;
            }

            if (DefaultTheme != null) {
                return DefaultTheme.GetColor(id, valueName);
            }
            else if (defaultColorValues_.TryGetValue(valueName, out var defaultColor)) {
                return defaultColor;
            } 

            return Colors.Transparent;
        }
        
        private ThemeColorSet GetOrCreateColorSet(Guid id) {
            if(!colorSets_.TryGetValue(id, out var colorSet)) {
                colorSet = new ThemeColorSet(id);
                colorSets_[id] = colorSet;
            }

            return colorSet;
        }
    }
}
