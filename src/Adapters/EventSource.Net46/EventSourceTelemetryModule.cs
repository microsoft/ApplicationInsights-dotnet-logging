// -----------------------------------------------------------------------
// <copyright file="EventSourceTelemetryModule.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. 
// All rights reserved.  2017
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Implementation;

using Microsoft.ApplicationInsights.EventSourceListener.Implementation;

namespace Microsoft.ApplicationInsights.EventSourceListener
{
    /// <summary>
    /// A module to trace data submitted via .NET framework <seealso cref="System.Diagnostics.Tracing.EventSource" /> class.
    /// </summary>
    public class EventSourceTelemetryModule : EventListener, ITelemetryModule
    {
        private TelemetryClient client;
        private bool constructed;  // Relying on the fact that constructed will be false initially (.NET default)
        private List<EventSource> eventSourcesPresentAtConstruction;

        /// <summary>
        /// Gets or sets the list of EventSource listening requests (information about which EventSources should be traced).
        /// </summary>
        public IList<EventSourceListeningRequest> Sources { get; private set; }

        /// <summary>
        /// EventSourceTelemetryModule parameterless constructor
        /// </summary>
        public EventSourceTelemetryModule()
        {
            this.Sources = new List<EventSourceListeningRequest>();
        }

        /// <summary>
        /// Initializes the telemetry module and starts tracing EventSources specified via <see cref="Sources"/> property.
        /// </summary>
        /// <param name="configuration">Module configuration</param>
        public void Initialize(TelemetryConfiguration configuration)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("evl:");

            if (this.Sources.Count == 0)
            {
                EventSourceListenerEventSource.Log.NoEventSourcesConfigured();
                return;
            }

            lock (this) // See OnEventSourceCreated() for the reason why we are locking on 'this' here.
            {
                try
                {
                    Debug.Assert(!this.constructed);
                    if (this.eventSourcesPresentAtConstruction != null)
                    {
                        foreach (EventSource eventSource in this.eventSourcesPresentAtConstruction)
                        {
                            EnableAsNecessary(eventSource);
                        }
                        this.eventSourcesPresentAtConstruction.Clear(); // Do not hold onto EventSource references that are already initialized.
                    }
                }
                finally
                {
                    this.constructed = true;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventData"></param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            eventData.Track(this.client);
        }

        /// <summary>
        /// Enables a single EventSource for tracing 
        /// </summary>
        /// <param name="eventSource"></param>
        private void EnableAsNecessary(EventSource eventSource)
        {
            EventSourceListeningRequest listeningRequest = this.Sources?.FirstOrDefault(s => s.Name == eventSource.Name);
            if (listeningRequest == null)
            {
                return;
            }

            // LIMITATION: There is a known issue where if we listen to the FrameworkEventSource, the dataflow pipeline may hang when it
            // tries to process the Threadpool event. The reason is the dataflow pipeline itself is using Task library for scheduling async
            // tasks, which then itself also fires Threadpool events on FrameworkEventSource at unexpected locations, and trigger deadlocks.
            // Hence, we like special case this and mask out Threadpool events.
            EventKeywords keywords = listeningRequest.Keywords;
            if (listeningRequest.Name == "System.Diagnostics.Eventing.FrameworkEventSource")
            {
                // Turn off the Threadpool | ThreadTransfer keyword. Definition is at http://referencesource.microsoft.com/#mscorlib/system/diagnostics/eventing/frameworkeventsource.cs
                // However, if keywords was to begin with, then we need to set it to All first, which is 0xFFFFF....
                if (keywords == 0)
                {
                    keywords = EventKeywords.All;
                }
                keywords &= (EventKeywords)~0x12;
            }

            this.EnableEvents(eventSource, listeningRequest.Level, keywords);
        }

        /// <summary>
        /// Processes notifications about new EventSource creation
        /// </summary>
        /// <param name="eventSource">EventSource instance</param>
        /// <remarks>When an instance of an EventListener is created, it will immediately receive notifications about all EventSources already existing in the AppDomain. 
        /// Then, as new EventSources are created, the EventListener will receive notifications about them.</remarks>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            // There is a bug in the EventListener library that causes this override to be called before the object is fully constructed.
            // So if we are not constructed yet, we will just remember the event source reference. Once the construction is accomplished,
            // we can decide if we want to handle a given event source or not.

            // Locking on 'this' is generally a bad practice because someone from outside could put a lock on us, and this is outside of our control.
            // But in the case of this class it is an unlikely scenario, and because of the bug described above, 
            // we cannot rely on construction to prepare a private lock object for us.
            lock (this)
            {
                if (!this.constructed)
                {
                    if (this.eventSourcesPresentAtConstruction == null)
                    {
                        this.eventSourcesPresentAtConstruction = new List<EventSource>();
                    }

                    this.eventSourcesPresentAtConstruction.Add(eventSource);
                }
                else
                {
                    EnableAsNecessary(eventSource);
                }
            }
        }
    }
}
