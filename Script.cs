using System;
using System.Windows;
using System.Reflection;
using VMS.TPS.Common.Model.API;
using SFRThelper.Services;
using SFRThelper.Views;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0 nSFRT")]

[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
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

                var mainWindow = new MainWindow(new ESAPIService(context));
                mainWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred: " + ex.Message,
                    "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
