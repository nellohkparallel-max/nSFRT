using System.ComponentModel;
using VMS.TPS.Common.Model.Types;

namespace SFRThelper.Models
{
    public class SphereModel : INotifyPropertyChanged
    {
        private VVector _center;
        private double _radius;
        private bool _isEdited;
        private bool _isSelected;

        public VVector Center
        {
            get => _center;
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
            get => _radius;
            set
            {
                _radius = value;
                OnPropertyChanged(nameof(Radius));
            }
        }

        public string Id { get; set; }

        public bool IsEdited
        {
            get => _isEdited;
            set
            {
                _isEdited = value;
                OnPropertyChanged(nameof(IsEdited));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public double X => Center.x;
        public double Y => Center.y;
        public double Z => Center.z;

        public SphereModel(VVector center, double radius, int index)
        {
            _center = center;
            _radius = radius;
            Id = $"Sphere_{index:D3}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}