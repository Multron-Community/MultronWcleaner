using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MultronWinCleaner.Processes
{
    public static class SectionGlow
    {
        private static GlowAdorner? current;

        public static void Flash(UIElement section)
        {
            current?.Remove();
            current = null;

            var layer = AdornerLayer.GetAdornerLayer(section);
            if (layer == null)
                return;

            var adorner = new GlowAdorner(section, layer);
            layer.Add(adorner);
            current = adorner;

            var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3) };
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            pulse.Completed += (_, _) =>
            {
                adorner.Remove();
                if (ReferenceEquals(current, adorner))
                    current = null;
            };
            adorner.BeginAnimation(UIElement.OpacityProperty, pulse);
        }

        private sealed class GlowAdorner : Adorner
        {
            private static readonly Color Accent = Color.FromRgb(0x1E, 0x88, 0xE5);
            private readonly AdornerLayer layer;

            public GlowAdorner(UIElement section, AdornerLayer layer) : base(section)
            {
                this.layer = layer;
                IsHitTestVisible = false;
                Opacity = 0;
                Effect = new DropShadowEffect { Color = Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
            }

            public void Remove()
            {
                BeginAnimation(UIElement.OpacityProperty, null);
                layer.Remove(this);
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                var size = AdornedElement.RenderSize;
                var bounds = new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2));
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x16, Accent.R, Accent.G, Accent.B)), new Pen(new SolidColorBrush(Accent), 2), bounds, 8, 8);
            }
        }
    }
}
