﻿//-----------------------------------------------------------------------
// <copyright file="EventSourceTelemetryModule.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.EventSourceListener
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Tracing;
    using System.Linq;
    using Microsoft.ApplicationInsights.EventSourceListener.Implementation;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.Implementation;
    using Microsoft.ApplicationInsights.TraceEvent.Shared.Implementation;
    using Microsoft.ApplicationInsights.TraceEvent.Shared.Utilities;

    public delegate void OnEventWrittenHandler(EventWrittenEventArgs eventArgs, TelemetryClient client);

    /// <summary>
    /// A module to trace data submitted via .NET framework <seealso cref="System.Diagnostics.Tracing.EventSource" /> class.
    /// </summary>
    public class EventSourceTelemetryModule : EventListener, ITelemetryModule
    {
        private TelemetryClient client;
        private bool initialized; // Relying on the fact that default value in .NET Framework is false
        private ConcurrentQueue<EventSource> appDomainEventSources;
        private ConcurrentQueue<EventSource> enabledEventSources;
        private readonly OnEventWrittenHandler onEventWrittenHandler;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventSourceTelemetryModule"/> class.
        /// </summary>
        public EventSourceTelemetryModule() : this(EventDataExtensions.Track)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EventSourceTelemetryModule"/> class.
        /// </summary>
        /// <param name="onEventWrittenHandler">Action to be executed each time an event is written to format and send via the configured <see cref="TelemetryClient"/></param>
        public EventSourceTelemetryModule(OnEventWrittenHandler onEventWrittenHandler)
        {
            if (onEventWrittenHandler == null) throw new ArgumentNullException(nameof(onEventWrittenHandler));

            this.Sources = new List<EventSourceListeningRequest>();
            this.enabledEventSources = new ConcurrentQueue<EventSource>();
            this.onEventWrittenHandler = onEventWrittenHandler;
        }

        /// <summary>
        /// Gets the list of EventSource listening requests (information about which EventSources should be traced).
        /// </summary>
        public IList<EventSourceListeningRequest> Sources { get; private set; }

        /// <summary>
        /// Initializes the telemetry module and starts tracing EventSources specified via <see cref="Sources"/> property.
        /// </summary>
        /// <param name="configuration">Module configuration.</param>
        public void Initialize(TelemetryConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("evl:");

            if (this.Sources.Count == 0)
            {
                EventSourceListenerEventSource.Log.NoSourcesConfigured(nameof(EventSourceListener.EventSourceTelemetryModule));
                return;
            }

            try
            {
                if (this.initialized)
                {
                    // Source listening requests might have changed between initializations. Let's start from a clean slate
                    EventSource enabledEventSource = null;
                    while (this.enabledEventSources.TryDequeue(out enabledEventSource))
                    {
                        this.DisableEvents(enabledEventSource);
                    }
                }

                // Set the initialized flag now to ensure that we do not miss any sources that came online as we are executing the initialization
                // (OnEventSourceCreated() might have been called on a separate thread). Worst case we will attempt to enable the same source twice
                // (with same settings), but that is OK, as the semantics of EnableEvents() is really "update what is being tracked", so it is fine
                // to call it multiple times for the same source.
                this.initialized = true;

                if (this.appDomainEventSources != null)
                {
                    // Enumeration over concurrent queue is thread-safe.
                    foreach (EventSource eventSourceToEnable in this.appDomainEventSources)
                    {
                        this.EnableAsNecessary(eventSourceToEnable);
                    }
                }
            }
            finally
            {
                // No matter what problems we encounter with enabling EventSources, we should note that we have been initialized.
                this.initialized = true;
            }
        }

        /// <summary>
        /// Processes a new EventSource event.
        /// </summary>
        /// <param name="eventData">Event to process.</param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Suppress events from TplEventSource--they are mostly interesting for debugging task processing and interaction,
            // and not that useful for production tracing. However, TPL EventSource must be enabled to get hierarchical activity IDs.
            if (this.initialized && !TplActivities.TplEventSourceGuid.Equals(eventData.EventSource.Guid))
            {
                this.onEventWrittenHandler(eventData, this.client);
            }
        }

        /// <summary>
        /// Processes notifications about new EventSource creation.
        /// </summary>
        /// <param name="eventSource">EventSource instance.</param>
        /// <remarks>When an instance of an EventListener is created, it will immediately receive notifications about all EventSources already existing in the AppDomain.
        /// Then, as new EventSources are created, the EventListener will receive notifications about them.</remarks>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            // There is a bug in the EventListener library that causes this override to be called before the object is fully constructed.
            // We need to remember all EventSources we get notified about to do the initialization correctly (event if it happens multiple times).
            // If we are initialized, we can follow up with enabling the source straight away.

            // Locking on 'this' is generally a bad practice because someone from outside could put a lock on us, and this is outside of our control.
            // But in the case of this class it is an unlikely scenario, and because of the bug described above,
            // we cannot rely on construction to prepare a private lock object for us.
            lock (this)
            {
                if (this.appDomainEventSources == null)
                {
                    this.appDomainEventSources = new ConcurrentQueue<EventSource>();
                }

                this.appDomainEventSources.Enqueue(eventSource);
            }

            // Do not call EnableAsNecessary() directly while processing OnEventSourceCreated() and holding the lock.
            // Enabling an EventSource tries to take a lock on EventListener list.
            // (part of EventSource implementation). If another EventSource is created on a different thread, 
            // the same lock will be taken before the call to OnEventSourceCreated() comes in and deadlock may result.
            // Reference: https://github.com/Microsoft/ApplicationInsights-dotnet-logging/issues/109
            if (this.initialized)
            {
                this.EnableAsNecessary(eventSource);
            }
        }

        /// <summary>
        /// Enables a single EventSource for tracing.
        /// </summary>
        /// <param name="eventSource">EventSource to enable.</param>
        private void EnableAsNecessary(EventSource eventSource)
        {
            // Special case: enable TPL activity flow for better tracing of nested activities.
            if (eventSource.Guid == TplActivities.TplEventSourceGuid)
            {
                this.EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)TplActivities.TaskFlowActivityIdsKeyword);
                this.enabledEventSources.Enqueue(eventSource);
            }
            else if (eventSource.Name == EventSourceListenerEventSource.ProviderName)
            {
                // Tracking ourselves does not make much sense
                return;
            }
            else
            {
                EventSourceListeningRequest listeningRequest = this.Sources?.FirstOrDefault(s => s.Name == eventSource.Name);
                if (listeningRequest != null)
                {
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
                    this.enabledEventSources.Enqueue(eventSource);
                }
            }
        }
    }
}
