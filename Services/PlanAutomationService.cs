using SFRThelper.Models;

namespace SFRThelper.Services
{
    public interface IPlanAutomationService
    {
        string SetupVmatArcs();
        string SetupVmatArcs(SFRTParameters parameters);
        string SeedPhotonObjectives(SFRTParameters parameters);
    }

    /// <summary>
    /// Optional Eclipse plan helpers: coplanar VMAT template and Photon Optimizer seeding.
    /// Delegates VMS work to <see cref="IESAPIService"/> so ViewModels stay POCO-only.
    /// </summary>
    public class PlanAutomationService : IPlanAutomationService
    {
        private readonly IESAPIService _esapi;

        public PlanAutomationService(IESAPIService esapi)
        {
            _esapi = esapi;
        }

        public string SetupVmatArcs()
        {
            return SetupVmatArcs(null);
        }

        public string SetupVmatArcs(SFRTParameters parameters)
        {
            if (_esapi == null)
                return "ESAPI service is not available.";
            if (_esapi.Worker == null || _esapi.Worker.CheckAccess())
                return _esapi.SetupVmatArcs(parameters);
            return _esapi.Worker.Invoke(() => _esapi.SetupVmatArcs(parameters));
        }

        public string SeedPhotonObjectives(SFRTParameters parameters)
        {
            if (_esapi == null)
                return "ESAPI service is not available.";
            if (_esapi.Worker == null || _esapi.Worker.CheckAccess())
                return _esapi.SeedPhotonObjectives(parameters);
            return _esapi.Worker.Invoke(() => _esapi.SeedPhotonObjectives(parameters));
        }
    }
}
