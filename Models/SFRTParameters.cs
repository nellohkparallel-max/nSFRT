// SFRTParameters.cs
using System.ComponentModel;

namespace SFRThelper.Models
{
    public class SFRTParameters : INotifyPropertyChanged
    {
        private double _sphereRadius = 5.0;
        private double _centerSpacing = 15.0;
        private double _boundaryMarginMm = 5.0;
        private int _maxIterations = 1331;
        private string _selectedPTVId;
        private bool _singleStructureOnly;

        public double SphereRadius
        {
            get => _sphereRadius;
            set
            {
                _sphereRadius = value;
                OnPropertyChanged(nameof(SphereRadius));
            }
        }

        public double CenterSpacing
        {
            get => _centerSpacing;
            set
            {
                _centerSpacing = value;
                OnPropertyChanged(nameof(CenterSpacing));
            }
        }

        public double BoundaryMarginMm
        {
            get => _boundaryMarginMm;
            set
            {
                _boundaryMarginMm = value;
                OnPropertyChanged(nameof(BoundaryMarginMm));
            }
        }

        public int MaxIterations
        {
            get => _maxIterations;
            set
            {
                _maxIterations = value;
                OnPropertyChanged(nameof(MaxIterations));
            }
        }

        public string SelectedPTVId
        {
            get => _selectedPTVId;
            set
            {
                _selectedPTVId = value;
                OnPropertyChanged(nameof(SelectedPTVId));
            }
        }

        public bool SingleStructureOnly
        {
            get => _singleStructureOnly;
            set
            {
                _singleStructureOnly = value;
                OnPropertyChanged(nameof(SingleStructureOnly));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}