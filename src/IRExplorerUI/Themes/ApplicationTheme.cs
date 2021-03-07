// unset

using System.Collections.Generic;
using AvalonDock.Themes;

namespace IRExplorerUI {
    public enum ApplicationThemeKind {
        Light,
        Gray,
        Blue,
        Dark
    }

    public class ApplicationTheme {
        public static readonly ApplicationTheme Light =
            new ApplicationTheme(ApplicationThemeKind.Light, "Light",
                "/IRExplorer;component/Themes/LightAppTheme.xaml",
                "-light",
                () => new Vs2013LightTheme());

        public static readonly ApplicationTheme Gray =
            new ApplicationTheme(ApplicationThemeKind.Gray, "Gray",
                "/IRExplorer;component/Themes/LightAppTheme.xaml",
                "-gray}",
                () => new Vs2013LightTheme());

        public static readonly ApplicationTheme Blue =
            new ApplicationTheme(ApplicationThemeKind.Blue, "Blue",
                "/IRExplorer;component/Themes/LightAppTheme.xaml",
                "-blue",
                () => new Vs2013BlueTheme());

        public static readonly ApplicationTheme Dark =
            new ApplicationTheme(ApplicationThemeKind.Dark, "Dark",
                "/IRExplorer;component/Themes/DarkAppTheme.xaml",
                "-dark",
                () => new Vs2013DarkTheme());

        public static readonly List<ApplicationTheme> Themes =
            new() { Light, Gray, Blue, Dark };

        public delegate Theme ThemeDelegate();
        private readonly ThemeDelegate themeDelegate_;

        public ApplicationTheme(ApplicationThemeKind kind, string name, string uri,
                                string syntaxFileFormat, ThemeDelegate themeDelegate) {
            Kind = kind;
            Name = name;
            ResourcesUri = uri;
            SyntaxFileFormat = syntaxFileFormat;
            themeDelegate_ = themeDelegate;
        }

        public static ApplicationTheme GetBuiltinTheme(ApplicationThemeKind kind) {
            return Themes.Find((theme) => theme.Kind == kind);
        }

        public ApplicationThemeKind Kind { get; set; }
        public string Name { get; set; }
        public string ResourcesUri { get; set; }
        public string SyntaxFileFormat { get; set; }

        public Theme GetDockPanelTheme() {
            return themeDelegate_();
        }

        public override string ToString() {
            return Name;
        }

        protected bool Equals(ApplicationTheme other) {
            return Kind == other.Kind;
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj)) {
                return false;
            }

            if (ReferenceEquals(this, obj)) {
                return true;
            }

            if (obj.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ApplicationTheme)obj);
        }

        public override int GetHashCode() {
            return Kind.GetHashCode();
        }
    }
}
