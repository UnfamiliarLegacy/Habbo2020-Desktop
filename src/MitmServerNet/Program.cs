using MitmServerNet.Config;
using MitmServerNet.Net;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Configuration.AddJsonFile("appsettings.Private.json", optional: true);

builder.Services.AddTransient<Middle>();
builder.Services.Configure<HabboCertificateOptions>(builder.Configuration.GetSection("HabboCertificate"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Request.Path != "/websocket")
    {
        await next(context);
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    
    // Accept websocket.
    var logger = context.RequestServices.GetRequiredService<ILogger<Middle>>();
    var middle = context.RequestServices.GetRequiredService<Middle>();
    var websocket = await context.WebSockets.AcceptWebSocketAsync();

    logger.LogInformation("Received websocket connection");
    
    await middle.ConnectToClient(websocket, context.RequestAborted);
    await middle.ConnectToHabbo(context.RequestAborted);
    await middle.Exchange(context.RequestAborted);
});

app.MapGet("/", () => "Hello World!");

app.Run();