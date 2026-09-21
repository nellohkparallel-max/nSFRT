using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SFRThelper.Services;
using SFRThelper.Views;
using EsapiApp = VMS.TPS.Common.Model.API.Application;

namespace SFRThelper.Standalone
{
    /// <summary>
    /// Standalone Eclipse entry: Application.CreateApplication on a dedicated STA dispatcher,
    /// WPF UI on a second STA thread. All VMS calls marshal through EsapiWorker.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            EsapiApp esapiApp = null;
            try
            {
                esapiApp = EsapiApp.CreateApplication();
                var worker = new EsapiWorker(Dispatcher.CurrentDispatcher);
                var service = new ESAPIService(esapiApp, worker);

                string patientId = args != null && args.Length > 0 ? args[0] : null;
                string structureSetId = args != null && args.Length > 1 ? args[1] : null;

                var uiThread = new Thread(() =>
                {
                    try
                    {
                        var wpfApp = new System.Windows.Application();
                        wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                        if (string.IsNullOrWhiteSpace(patientId))
                        {
                            var picker = BuildPicker();
                            bool? ok = picker.ShowDialog();
                            if (ok != true)
                                return;
                            patientId = picker.Tag as string;
                            structureSetId = picker.Uid;
                        }

                        string openError = null;
                        worker.Invoke(() =>
                        {
                            try
                            {
                                var patient = esapiApp.OpenPatientById(patientId);
                                if (patient == null)
                                {
                                    openError = "Patient '" + patientId + "' was not found.";
                                    return;
                                }

                                VMS.TPS.Common.Model.API.StructureSet ss = null;
                                if (!string.IsNullOrWhiteSpace(structureSetId))
                                {
                                    ss = patient.StructureSets.FirstOrDefault(s =>
                                        string.Equals(s.Id, structureSetId, StringComparison.OrdinalIgnoreCase));
                                }
                                if (ss == null)
                                    ss = patient.StructureSets.FirstOrDefault();
                                if (ss == null)
                                {
                                    openError = "Patient '" + patientId + "' has no structure set.";
                                    return;
                                }

                                service.AttachStandalone(patient, ss);
                            }
                            catch (Exception ex)
                            {
                                openError = ex.Message;
                            }
                        });

                        if (!string.IsNullOrEmpty(openError))
                        {
                            MessageBox.Show(openError, "nSFRT Standalone", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        var main = new MainWindow(service);
                        wpfApp.Run(main);
                    }
                    finally
                    {
                        worker.BeginShutdown();
                    }
                });
                uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.Name = "nSFRT-UI";
                uiThread.IsBackground = false;
                uiThread.Start();
                Dispatcher.Run();
                uiThread.Join();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Standalone startup failed: " + ex.Message,
                    "nSFRT Standalone", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (esapiApp != null)
                    esapiApp.Dispose();
            }
        }

        private static Window BuildPicker()
        {
            var patientBox = new TextBox { Margin = new Thickness(8), MinHeight = 24 };
            var ssBox = new TextBox { Margin = new Thickness(8), MinHeight = 24 };
            var window = new Window
            {
                Title = "nSFRT Standalone — Open Patient",
                Width = 440,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Patient ID", Margin = new Thickness(8, 8, 8, 0) });
            panel.Children.Add(patientBox);
            panel.Children.Add(new TextBlock { Text = "Structure Set ID (optional)", Margin = new Thickness(8, 8, 8, 0) });
            panel.Children.Add(ssBox);
            var open = new Button { Content = "Open", Margin = new Thickness(8), Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
            open.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(patientBox.Text))
                {
                    MessageBox.Show("Enter a patient ID.", "nSFRT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                window.Tag = patientBox.Text.Trim();
                window.Uid = (ssBox.Text ?? string.Empty).Trim();
                window.DialogResult = true;
            };
            panel.Children.Add(open);
            window.Content = panel;
            return window;
        }
    }
}
