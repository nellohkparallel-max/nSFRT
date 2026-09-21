using SFRThelper.Models;
using System.Windows;
using System.Windows.Media;

namespace SFRThelper.ViewModels
{
    public class AxialSphereVisual
    {
        public SphereModel Sphere { get; set; }
        public Point Center { get; set; }
        public double RadiusPixels { get; set; }
        public Brush Stroke { get; set; }
        public Brush Fill { get; set; }
    }

    public class PackingOption
    {
        public PackingGeometryMode Value { get; set; }
        public string Display { get; set; }
    }

    public class GenerationModeOption
    {
        public SphereGenerationMode Value { get; set; }
        public string Display { get; set; }
    }
}
