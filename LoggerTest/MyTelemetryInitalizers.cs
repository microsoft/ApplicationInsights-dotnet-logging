using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace LoggerTest
{
    internal class MyTelemetryInitalizers : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            (telemetry as ISupportProperties).Properties.Add("CijoKey", "CijoValue");
        }
    }
}