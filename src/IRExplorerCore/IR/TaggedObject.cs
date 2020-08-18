// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public class TaggedObject {
        public List<ITag> Tags { get; set; }

        public void AddTag(ITag tag) {
            Tags ??= new List<ITag>();
            tag.Owner = this;
            Tags.Add(tag);
        }

        public T GetTag<T>() where T : class {
            if (Tags != null) {
                foreach (var tag in Tags) {
                    if (tag is T value) {
                        return value;
                    }
                }
            }

            return null;
        }

        public T GetOrAddTag<T>() where T : class, new() {
            var result = GetTag<T>();

            if (result != null) {
                return result;
            }

            var tag = new T();
            AddTag(tag as ITag);
            return tag;
        }

        public bool RemoveTag<T>() where T : class {
            if (Tags != null) {
                return Tags.RemoveAll(tag => tag is T) > 0;
            }

            return false;
        }
    }
}
