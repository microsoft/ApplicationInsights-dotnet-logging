using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Implementation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.EventSourceListener
{
    public class EventListenerModule : ITelemetryModule
    {
        private TelemetryClient client;

        // TODO: it will be great of changing the list of providers wil lregister/unregister listeners
        public IList<string> Providers;

        public void Initialize(TelemetryConfiguration configuration)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("evl:");

            foreach (var provider in this.Providers)
            {
                this.EnableListening(provider);
            }
        }

        private void EnableListening(string provider)
        {

        }

        private void OnEvent()
        {
            this.client.TrackEvent("test");
        }
    }
}
