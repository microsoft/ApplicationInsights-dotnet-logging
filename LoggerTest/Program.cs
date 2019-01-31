using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LoggerTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            // Create DI container.
            IServiceCollection services = new ServiceCollection();
            
            var serverChannel = new InMemoryChannel();
            services.Configure<TelemetryConfiguration>(
                        (config) =>
                        {
                            config.InstrumentationKey = "ikey";
                            config.TelemetryChannel = serverChannel;
                            config.TelemetryInitializers.Add(new MyTelemetryInitalizers());
                            // config.DefaultTelemetrySink.TelemetryProcessorChainBuilder.UseSampling(5);
                            // serverChannel.Initialize(config);
                        }
                );

            

            // Add the logging pipelines to use. We are adding ApplicationInsights only.
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddApplicationInsights("cijokey");

            });

            // Build ServiceProvider.
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            ILogger<Program> logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Begin a new scope. This is optional. Epecially in case of AspNetCore request info is already
            // present in scope.
            using (logger.BeginScope(new Dictionary<string, object> { { "Method", nameof(Main) } }))
            {
                logger.LogInformation("Logger is working");
            }

            for (int i = 0; i < 100; i++)
            {
                logger.LogInformation("Logger item " + i);
                Thread.Sleep(100);
            }


            // Sleep to delay exit of this console app so that events are pushed to ApplicationInsights.
            Thread.Sleep(10000);
            Console.ReadLine();
        }
    }
}
