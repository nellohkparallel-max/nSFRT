using System;
using System.Windows;
using System.Reflection;
using VMS.TPS.Common.Model.API;
using SFRThelper.Views;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.15")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }
        
	//[MethodImpl(MethodImplOptions.NoInlining)]
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
                
                var mainWindow = new MainWindow(context);
                mainWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", 
                    "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}