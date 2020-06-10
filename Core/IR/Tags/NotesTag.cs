using System;
using System.Collections.Generic;
using System.Text;

namespace Core.IR.Tags
{
    public class NotesTag : ITag {
        public string Name { get => "Notes"; }
        public IRElement Parent { get; set; }
        public string Title { get; set; }
        public List<string> Notes { get; set; }

        public NotesTag()
        {
            Title = "";
            Notes = new List<string>();
        }

        public NotesTag(string title) : this() {
            Title = title;
        }

        public override bool Equals(object obj) {
            return obj is NotesTag tag &&
                   Title == tag.Title &&
                   EqualityComparer<List<string>>.Default.Equals(Notes, tag.Notes);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Title, Notes);
        }

        public override string ToString() {
            return $"Notes title: {Title}";
        }
    }
}
