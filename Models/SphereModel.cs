using System.ComponentModel;

namespace SFRThelper.Models
{
    public class SphereModel : INotifyPropertyChanged
    {
        private Point3D _center;
        private double _radius;
        private bool _isEdited;
        private bool _isSelected;

        public Point3D Center
        {
            get { return _center; }
            set
            {
                _center = value;
                OnPropertyChanged(nameof(Center));
                OnPropertyChanged(nameof(X));
                OnPropertyChanged(nameof(Y));
                OnPropertyChanged(nameof(Z));
            }
        }

        public double Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                OnPropertyChanged(nameof(Radius));
            }
        }

        public string Id { get; set; }

        public bool IsEdited
        {
            get { return _isEdited; }
            set
            {
                _isEdited = value;
                OnPropertyChanged(nameof(IsEdited));
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public double X { get { return Center.X; } }
        public double Y { get { return Center.Y; } }
        public double Z { get { return Center.Z; } }

        public SphereModel(Point3D center, double radius, int index)
        {
            _center = center;
            _radius = radius;
            Id = StructureNaming.FormatPeakId(index);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
