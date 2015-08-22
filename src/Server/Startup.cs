using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Logging;

namespace Server
{
    public class Startup
    {
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddTransient<WebSocketHandler>();
            services.AddSingleton<MemoryPool>();
#if DNX451
            services.AddSingleton<IPerformanceCounter, PerformanceCounterWrapper>();
#endif
        }

        public void Configure(IApplicationBuilder app)
        {
            app.Map("/socket", socketApp =>
            {
                socketApp.Run(async context =>
                {
                    var badRequest = true;

                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = (WebSocket) null;
                        try
                        {
                            webSocket = await context.WebSockets.AcceptWebSocketAsync();
                            badRequest = false;
                        }
                        catch
                        {
                            badRequest = true;
                        }
                        
                        var logger = context.ApplicationServices.GetService<ILogger>();
                        var memoryPool = context.ApplicationServices.GetService<MemoryPool>();
                        var webSocketHandler = new WebSocketHandler(memoryPool, logger);
                        webSocketHandler.OnMessageTextAction = msg =>
                        {
                            webSocketHandler.SendAsync($"Recieved: {msg}");
                        };
                        
                        // Start the websocket handler so that we can process things over the channel
                        await webSocketHandler.ProcessWebSocketRequestAsync(webSocket, CancellationToken.None);
                    }

                    if (badRequest)
                    {
                        // Bad Request
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Bad request :(");
                    }
                });
            });

            app.Run(async (context) =>
            {
                await context.Response.WriteAsync("Hello World!");
            });
        }
    }
}
