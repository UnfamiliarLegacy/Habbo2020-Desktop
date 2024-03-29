using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using MitmServerNet.Config;
using MitmServerNet.Helpers;

namespace MitmServerNet.Net;

public class Middle
{
    private const string TargetHost = "game-us.habbo.com";
    private const string TargetServer = "wss://game-us.habbo.com:30001/websocket";
    private const string ExpectedCertificate = "CN=game-*.habbo.com, OU=Technical Operations, O=Sulake Oy, L=Helsinki, S=Uusimaa, C=FI";
        
    private static ReadOnlySpan<byte> PacketStartTLS => "StartTLS"u8;
    private static ReadOnlySpan<byte> PacketOK => "OK"u8;

    private readonly ILogger<Middle> _logger;

    private readonly X509Certificate2 _serverCertificate;
    private readonly X509Certificate2 _targetCertificate;
    
    private SslStream? _serverStream;
    private SslStream? _targetStream;

    public Middle(ILogger<Middle> logger, IOptions<HabboCertificateOptions> habboCertificateOptions)
    {
        var options = habboCertificateOptions.Value;
        
        if (string.IsNullOrEmpty(options.Password))
        {
            throw new InvalidOperationException("Habbo certificate password is not set");
        }
        
        _logger = logger;
        
        var certificatesPath = Path.Combine(AppContext.BaseDirectory, "Certificates");
        var certificatePath = Path.Combine(certificatesPath, "Self", "cert.pem");
        var keyPath = Path.Combine(certificatesPath, "Self", "key.pem");

        _serverCertificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        _serverCertificate = new X509Certificate2(_serverCertificate.Export(X509ContentType.Pfx));
        
        var targetCertificatePath = Path.Combine(certificatesPath, "Dumped", "habbo_prod-1455.pfx");
        var targetCertificateData = File.ReadAllBytes(targetCertificatePath);

        _targetCertificate = new X509Certificate2(targetCertificateData, options.Password);
    }

    public async Task ConnectToClient(WebSocket websocket, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received connection from Habbo client");
        
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = bufferOwner.Memory;
        
        // Receive "StartTLS".
        var recv = await websocket.ReceiveAsync(buffer, cancellationToken);
        if (recv.Count != PacketStartTLS.Length || !PacketStartTLS.SequenceEqual(buffer.Slice(0, recv.Count).Span))
        {
            _logger.LogWarning("First packet was not 'StartTLS'");
            return;
        }
        
        // Send "OK".
        PacketOK.CopyTo(buffer.Span);
        
        await websocket.SendAsync(buffer.Slice(0, PacketOK.Length), WebSocketMessageType.Binary, true, cancellationToken);

        // Prepare SSL auth.
        var sslAuth = new SslServerAuthenticationOptions()
        {
            ServerCertificate = _serverCertificate,
            ClientCertificateRequired = true
        };
        
        // Wrap into SslStream.
        var websocketStream = new WebsocketStream(websocket);
        var websocketWrapped = new SslStream(websocketStream, false, ValidateHabboClientCertificate);
        
        await websocketWrapped.AuthenticateAsServerAsync(sslAuth, cancellationToken);
        
        _serverStream = websocketWrapped;
    }

    public async Task ConnectToHabbo(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to Habbo server");
        
        // Disable "traceparent" header.
        Activity.Current = null;
        
        // Connect to Habbo server.
        var target = new ClientWebSocket();
        
        target.Options.SetRequestHeader("User-Agent", "websocket-sharp/1.0");
        
        await target.ConnectAsync(new Uri(TargetServer), cancellationToken);
        
        // Send StartTLS.
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = bufferOwner.Memory;
        
        PacketStartTLS.CopyTo(buffer.Span);
        
        await target.SendAsync(buffer.Slice(0, PacketStartTLS.Length), WebSocketMessageType.Binary, true, cancellationToken);
        
        // Receive "OK".
        var recv = await target.ReceiveAsync(buffer, cancellationToken);
        if (recv.Count != PacketOK.Length || !PacketOK.SequenceEqual(buffer.Slice(0, recv.Count).Span))
        {
            _logger.LogWarning("Second packet was not 'OK'");
            return;
        }

        // Prepare SSL auth.
        var sslAuth = new SslClientAuthenticationOptions
        {
            TargetHost = TargetHost,
            LocalCertificateSelectionCallback = (_, _, _, _, _) => _targetCertificate,
            ClientCertificates = new X509Certificate2Collection(new[] { _targetCertificate }),
        };
        
        // Wrap into SslStream.
        var targetWebsocketStream = new WebsocketStream(target);
        var targetWebsocketWrapped = new SslStream(targetWebsocketStream, false, ValidateHabboServerCertificate);
        
        await targetWebsocketWrapped.AuthenticateAsClientAsync(sslAuth, cancellationToken);
        
        _targetStream = targetWebsocketWrapped;
    }

    private bool ValidateHabboClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        _logger.LogInformation("Habbo client certificate validation: [{Certificate}] {Errors}", certificate?.Subject, sslpolicyerrors);
        
        if (certificate == null)
        {
            _logger.LogWarning("Client certificate missing");
            return false;
        }
        
        // Compare CN with expected value.
        if (certificate.Subject != _targetCertificate.Subject)
        {
            _logger.LogWarning("Client certificate subject does not match dumped certificate subject [{Expected}]", _targetCertificate.Subject);
            return false;
        }
        
        _logger.LogInformation("Client certificate is valid");
        return true;
    }

    private bool ValidateHabboServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        _logger.LogInformation("Habbo server certificate validation: [{Certificate}] {Errors}", certificate?.Subject, sslpolicyerrors);
        
        if (certificate == null)
        {
            _logger.LogWarning("Server certificate missing");
            return false;
        }
        
        // Compare CN with expected value.
        if (certificate.Subject != ExpectedCertificate)
        {
            _logger.LogWarning("Server certificate subject does not match expected value [{Expected}]", ExpectedCertificate);
            return false;
        }
        
        _logger.LogInformation("Server certificate is valid");
        return true;
    }

    private async Task SwapAsync(MiddleSwap from, MiddleSwap to, CancellationToken cancellationToken)
    {
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(4096);
        
        var buffer = bufferOwner.Memory;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var readBytes = await from.Stream.ReadAsync(buffer, cancellationToken);
            if (readBytes == 0)
            {
                break;
            }
            
            // Dump the received bytes.
            _logger.LogInformation("[{From} -> {To}] Received {Bytes} bytes", from.Name, to.Name, readBytes);
            
            Console.ForegroundColor = from.Color;
            Console.WriteLine(HexFormatting.Dump(buffer.Slice(0, readBytes).Span));
            Console.ResetColor();
            
            // Write to the target stream.
            await to.Stream.WriteAsync(buffer.Slice(0, readBytes), cancellationToken);
        }
    }
    
    public async Task Exchange(CancellationToken cancellationToken)
    {
        if (_serverStream == null)
        {
            throw new InvalidOperationException("Server stream is not connected");
        }
        
        if (_targetStream == null)
        {
            throw new InvalidOperationException("Target stream is not connected");
        }
        
        // Create tasks for receiving from both streams.
        var clientSwap = new MiddleSwap("Client", _serverStream, ConsoleColor.Cyan);
        var targetSwap = new MiddleSwap("Target", _targetStream, ConsoleColor.Magenta);
        
        var clientTask = SwapAsync(clientSwap, targetSwap, cancellationToken);
        var targetTask = SwapAsync(targetSwap, clientSwap, cancellationToken);

        // Wait for any task to finish.
        await Task.WhenAny(clientTask, targetTask);
    }
}