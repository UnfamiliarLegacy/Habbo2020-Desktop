using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using MitmServerNet.Config;
using MitmServerNet.Helpers;
using MitmServerNet.Net.Crypto;
using MitmServerNet.Net.Packets;

namespace MitmServerNet.Net;

public class Middle
{
    private const string TargetHost = "game-us.habbo.com";
    private const string TargetServer = "wss://game-us.habbo.com:30001/websocket";
    private const string ExpectedCertificate = "CN=game-*.habbo.com, OU=Technical Operations, O=Sulake Oy, L=Helsinki, S=Uusimaa, C=FI";

    private const string RealExponent = "10001";
    private const string RealModulus = "BD214E4F036D35B75FEE36000F24EBBEF15D56614756D7AFBD4D186EF5445F758B284647FEB773927418EF70B95387B80B961EA56D8441D410440E3D3295539A3E86E7707609A274C02614CC2C7DF7D7720068F072E098744AFE68485C6297893F3D2BA3D7AAAAF7FA8EBF5D7AF0BA2D42E0D565B89D332DE4CF898D666096CE61698DE0FAB03A8A5E12430CB427C97194CBD221843D162C9F3ACF74DA1D80EBC37FDE442B68A0814DFEA3989FDF8129C120A8418248D7EE85D0B79FA818422E496D6FA7B5BD5DB77E588F8400CDA1A8D82EFED6C86B434BAFA6D07DFCC459D35D773F8DFAF523DFED8FCA45908D0F9ED0D4BCEAC3743AF39F11310EAF3DFF45";

    private const string FakeExponent = "3";
    private const string FakeModulus = "86851DD364D5C5CECE3C883171CC6DDC5760779B992482BD1E20DD296888DF91B33B936A7B93F06D29E8870F703A216257DEC7C81DE0058FEA4CC5116F75E6EFC4E9113513E45357DC3FD43D4EFAB5963EF178B78BD61E81A14C603B24C8BCCE0A12230B320045498EDC29282FF0603BC7B7DAE8FC1B05B52B2F301A9DC783B7";
    private const string FakePrivateExponent = "59AE13E243392E89DED305764BDD9E92E4EAFA67BB6DAC7E1415E8C645B0950BCCD26246FD0D4AF37145AF5FA026C0EC3A94853013EAAE5FF1888360F4F9449EE023762EC195DFF3F30CA0B08B8C947E3859877B5D7DCED5C8715C58B53740B84E11FBC71349A27C31745FCEFEEEA57CFF291099205E230E0C7C27E8E1C0512B";

    private static ReadOnlySpan<byte> PacketStartTLS => "StartTLS"u8;
    private static ReadOnlySpan<byte> PacketOK => "OK"u8;

    private readonly ILogger<Middle> _logger;

    private readonly X509Certificate2 _serverCertificate;
    private readonly X509Certificate2 _targetCertificate;

    /// <summary>
    ///     Encryption with the Habbo unity client.
    /// </summary>
    private readonly HabboEncryption _clientEnc;
    
    /// <summary>
    ///     Encryption with the Habbo server.
    /// </summary>
    private readonly HabboEncryption _serverEnc;

    private byte[]? _nonce;
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

        _clientEnc = new HabboEncryption(FakeExponent, FakeModulus, FakePrivateExponent);
        _serverEnc = new HabboEncryption(RealExponent, RealModulus);
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

    private async Task SwapAsync<TFrom, TTo>(MiddleSwap<TFrom> from, MiddleSwap<TTo> to, CancellationToken cancellationToken) where TFrom : Enum where TTo : Enum
    {
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(4096);
        
        var buffer = bufferOwner.Memory;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var readBytes = await from.Stream.ReadAsync(buffer, cancellationToken);
                if (readBytes == 0)
                {
                    break;
                }

                // Parse the received bytes.
                foreach (var frame in from.Parser.Parse(buffer.Slice(0, readBytes)).ToArray())
                {
                    // Decrypt frame.
                    from.ShuffleIn(frame.Header);
                    
                    // Parse packet.
                    var packet = new HabboPacket(frame);
                    
                    // Dump the received bytes.
                    _logger.LogInformation("[{From} -> {To}] Received packet Id {Id}, length {Length}", from.Name, to.Name, packet.Id, packet.Length);

                    Console.ForegroundColor = from.Color;
                    Console.WriteLine(HexFormatting.Dump(frame.Data));
                    Console.ResetColor();
                    
                    // Modify the packet.
                    var outModified = from.Modifier((TFrom) Enum.ToObject(typeof(TFrom), packet.Id), packet);
                    var outFrame = outModified?.Frame ?? frame;
                    
                    // Encrypt frame.
                    if (outModified == null || !outModified.SkipShuffle)
                    {
                        to.ShuffleOut(outFrame.Header);
                    }
                    
                    // Write to the target stream.
                    await to.Stream.WriteAsync(outFrame.Data, cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to swap data");
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
        var clientSwap = new MiddleSwap<C2S>("Client", _serverStream, InterceptC2S, ClientShuffleIn, ClientShuffleOut, ConsoleColor.Cyan);
        var targetSwap = new MiddleSwap<S2C>("Target", _targetStream, InterceptS2C, TargetShuffleIn, TargetShuffleOut, ConsoleColor.Magenta);
        
        var clientTask = SwapAsync(clientSwap, targetSwap, cancellationToken);
        var targetTask = SwapAsync(targetSwap, clientSwap, cancellationToken);

        // Wait for any task to finish.
        await Task.WhenAny(clientTask, targetTask);
    }

    private HabboPacket? InterceptC2S(C2S header, HabboPacket packet)
    {
        switch (header)
        {
            case C2S.ClientHelloMessageComposer:
            {
                var nonce = packet.ReadString();
                var protocol = packet.ReadString();
                
                _logger.LogDebug("Received ClientHelloMessageComposer {Nonce} {Protocol}", nonce, protocol);

                var nonceHex = string.Empty;
                
                for (var i = 0; i < 8; i++)
                {
                    nonceHex += nonce.Substring(i * 3, 2);
                }

                _nonce = Convert.FromHexString(nonceHex);
                return null;
            }
            case C2S.CompleteDiffieHandshakeMessageComposer:
            {
                var clientPublicKey = packet.ReadString();
                
                // Set up ChaCha
                Span<byte> clientSharedKey = stackalloc byte[32];

                _clientEnc.Diffie.GetSharedKey(clientPublicKey).CopyTo(clientSharedKey);
                
                _clientEnc.Incoming = new HabboChaCha20(clientSharedKey, _nonce, 0);
                _clientEnc.Outgoing = new HabboChaCha20(clientSharedKey, _nonce, 0);
                
                // Replace local public key
                var packetWriter = new HabboPacketWriter((short)header);
                
                packetWriter.WriteString(_serverEnc.Diffie.GetPublicKey());
                
                return new HabboPacket(new HabboFrame(packetWriter.Buffer), true);
            }
        }
        
        return null;
    }
    
    private HabboPacket? InterceptS2C(S2C header, HabboPacket packet)
    {
        switch (header)
        {
            case S2C.InitDiffieHandshakeEvent:
            {
                var remoteP = packet.ReadString();
                var remoteG = packet.ReadString();
                
                _serverEnc.Diffie.DoHandshake(remoteP, remoteG);
                
                // Replace remote signed prime
                var packetWriter = new HabboPacketWriter((short)header);
                
                packetWriter.WriteString(_clientEnc.Diffie.GetSignedPrime());
                packetWriter.WriteString(_clientEnc.Diffie.GetSignedGenerator());
                
                return new HabboPacket(new HabboFrame(packetWriter.Buffer), true);
            }
            case S2C.CompleteDiffieHandshakeEvent:
            {
                var serverPublicKey = packet.ReadString();
                var clientEncryption = packet.ReadBoolean();

                // Set up ChaCha
                Span<byte> serverSharedKey = stackalloc byte[32];

                _serverEnc.Diffie.GetSharedKey(serverPublicKey).CopyTo(serverSharedKey);
                
                _serverEnc.Incoming = new HabboChaCha20(serverSharedKey, _nonce, 0);
                _serverEnc.Outgoing = new HabboChaCha20(serverSharedKey, _nonce, 0);
                
                // Not used (?)
                // if (!clientEncryption)
                // {
                //     _clientEnc.Outgoing = null; // We will not send encrypted packets to the habbo client.
                //     _serverEnc.Incoming = null; // The habbo server won't send us encrypted packets.
                // }

                // Replace remote public key
                var packetWriter = new HabboPacketWriter((short)header);
                
                packetWriter.WriteString(_clientEnc.Diffie.GetPublicKey());
                packetWriter.WriteBoolean(clientEncryption);
                
                return new HabboPacket(new HabboFrame(packetWriter.Buffer), true);
            }
        }
        
        return null;
    }

    private void ClientShuffleIn(Span<byte> header)
    {
        _clientEnc.ProcessIn(header);
    }

    private void ClientShuffleOut(Span<byte> header)
    {
        _clientEnc.ProcessOut(header);
    }

    private void TargetShuffleIn(Span<byte> header)
    {
        _serverEnc.ProcessIn(header);
    }

    private void TargetShuffleOut(Span<byte> header)
    {
        _serverEnc.ProcessOut(header);
    }
}