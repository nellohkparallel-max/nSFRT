// MainWindow.xaml.cs : replace the entire file
using SFRThelper.Models;
using SFRThelper.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VMS.TPS.Common.Model.API;

namespace SFRThelper.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        private Ellipse _draggingEllipse;
        private AxialSphereVisual _draggingVisual;
        private readonly Dictionary<SphereModel, Ellipse> _ellipseMap = new Dictionary<SphereModel, Ellipse>();

        public MainWindow(ScriptContext context)
        {
            InitializeComponent();
            DataContext = new MainViewModel(context);

            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                ViewModel.UpdateViewerSize(AxialCanvas.ActualWidth, AxialCanvas.ActualHeight);
                RedrawAxialCanvas();
            }

            AxialCanvas.MouseWheel += AxialCanvas_MouseWheel;
            AxialCanvas.MouseMove += AxialCanvas_MouseMove;
            AxialCanvas.MouseLeftButtonUp += AxialCanvas_MouseLeftButtonUp;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ViewModel == null)
                return;

            ViewModel.UpdateViewerSize(AxialCanvas.ActualWidth, AxialCanvas.ActualHeight);
            RedrawAxialCanvas();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentSliceIndex) ||
                e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                RedrawAxialCanvas();
            }
        }

        private void RedrawAxialCanvas()
        {
            if (ViewModel == null)
                return;

            _ellipseMap.Clear();
            AxialCanvas.Children.Clear();

            List<PointCollection> contours = ViewModel.GetCanvasContours();
            foreach (PointCollection contour in contours)
            {
                var polyline = new Polyline
                {
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 1.5,
                    Points = contour
                };
                AxialCanvas.Children.Add(polyline);
            }

            foreach (AxialSphereVisual sphere in ViewModel.AxialSpheres)
            {
                var ellipse = new Ellipse
                {
                    Width = sphere.RadiusPixels * 2,
                    Height = sphere.RadiusPixels * 2,
                    Stroke = sphere.Stroke,
                    Fill = sphere.Fill,
                    StrokeThickness = sphere.Sphere.IsSelected ? 3 : 2,
                    Tag = sphere,
                    Cursor = Cursors.SizeAll
                };

                Canvas.SetLeft(ellipse, sphere.Center.X - sphere.RadiusPixels);
                Canvas.SetTop(ellipse, sphere.Center.Y - sphere.RadiusPixels);

                ellipse.MouseLeftButtonDown += Sphere_MouseLeftButtonDown;
                AxialCanvas.Children.Add(ellipse);
                _ellipseMap[sphere.Sphere] = ellipse;
            }
        }

        private void Sphere_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var ellipse = sender as Ellipse;
            if (ellipse == null)
                return;

            _draggingEllipse = ellipse;
            _draggingVisual = ellipse.Tag as AxialSphereVisual;

            if (_draggingVisual == null || ViewModel == null)
                return;

            ViewModel.SelectSphere(_draggingVisual);
            _draggingEllipse.CaptureMouse();
            e.Handled = true;
        }

        private void AxialCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingEllipse == null || _draggingVisual == null || ViewModel == null)
                return;

            if (!_draggingEllipse.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
                return;

            var p = e.GetPosition(AxialCanvas);
            bool moved = ViewModel.TryMoveSelectedSphereOnAxialCanvas(p);
            if (moved)
                RedrawAxialCanvas();

            e.Handled = true;
        }

        private void AxialCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingEllipse != null)
                _draggingEllipse.ReleaseMouseCapture();

            _draggingEllipse = null;
            _draggingVisual = null;
        }

        private void AxialCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ViewModel == null)
                return;

            if (e.Delta > 0)
                ViewModel.CurrentSliceIndex++;
            else if (e.Delta < 0)
                ViewModel.CurrentSliceIndex--;

            e.Handled = true;
        }
    }
}