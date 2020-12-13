using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IRExplorerUI {
    /// <summary>An effect that turns the input into shades of a single color.</summary>
    public class DesaturateEffect : ShaderEffect {
        /// <summary>Identifies the <see cref="Input"/> dependency property.</summary>
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(
            nameof(Input),
            typeof(DesaturateEffect),
            0);

        /// <summary>Identifies the <see cref="Strength"/> dependency property.</summary>
        public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(
            nameof(Strength),
            typeof(double),
            typeof(DesaturateEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(0)));

        private static readonly PixelShader Shader = new PixelShader {
            UriSource = new Uri("/IRExplorer;component/Effects/DesaturateEffect.ps", UriKind.RelativeOrAbsolute),
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="DesaturateEffect"/> class.
        /// </summary>
        public DesaturateEffect() {
            this.PixelShader = Shader;
            this.UpdateShaderValue(InputProperty);
            this.UpdateShaderValue(StrengthProperty);
        }

        /// <summary>
        /// Gets or sets the Brush called "Input" that is required by a shader.
        /// This property contains the input image and it is usually not set directly - it is set automatically when our effect is applied to a control.
        /// </summary>
        public Brush Input {
            get => (Brush)this.GetValue(InputProperty);
            set => this.SetValue(InputProperty, value);
        }

        /// <summary>
        /// Gets or sets the strangth that the image is desaturated with.
        /// The value can be between 0 and 1.
        /// 0 means the original image is returned.
        /// 1 means a monochrome image is produced.
        /// </summary>
        public double Strength {
            get => (double)this.GetValue(StrengthProperty);
            set => this.SetValue(StrengthProperty, value);
        }
    }
}
