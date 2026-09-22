using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;
using SFRThelper.Services;
using SFRThelper.Views;

[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0 nSFRT clinical suite")]

[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    /// <summary>
    /// Eclipse binary plugin. ESAPI objects stay on this STA thread (nested DispatcherFrame).
    /// The WPF window runs on a second STA thread and marshals through EsapiWorker.
    /// IEsapiScript is not part of ESAPI 16.1; VMS.TPS.Script.Execute is the plugin contract.
    /// </summary>
    public class Script
    {
        public Script()
        {
        }

        public void Execute(ScriptContext context)
        {
            try
            {
                if (context.Patient == null)
                {
                    MessageBox.Show("Please load a patient before running this script.",
                        "No Patient Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (context.StructureSet == null)
                {
                    MessageBox.Show("Please load a structure set before running this script.",
                        "No Structure Set Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var esapiDispatcher = Dispatcher.CurrentDispatcher;
                var worker = new EsapiWorker(esapiDispatcher);
                var service = new ESAPIService(context, worker);
                var frame = new DispatcherFrame();

                var uiThread = new Thread(() =>
                {
                    try
                    {
                        var mainWindow = new MainWindow(service);
                        mainWindow.Closed += (s, e) => { frame.Continue = false; };
                        mainWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        esapiDispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("UI error: " + ex.Message, "nSFRT",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }));
                    }
                    finally
                    {
                        frame.Continue = false;
                    }
                });
                uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.Name = "nSFRT-UI";
                uiThread.IsBackground = false;
                uiThread.Start();

                Dispatcher.PushFrame(frame);
                uiThread.Join(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred: " + ex.Message,
                    "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
