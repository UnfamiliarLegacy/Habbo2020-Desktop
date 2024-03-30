using System.Diagnostics;

namespace MitmServerNet.Net.Crypto;

public class HabboEncryption
{
    public HabboEncryption(string e, string n)
    {
        Crypto = new HabboRSACrypto(e, n);
        Diffie = new HabboDiffieHellman(Crypto, HabboConnectionType.Client);
    }

    public HabboEncryption(string e, string n, string d)
    {
        Crypto = new HabboRSACrypto(e, n, d);
        Diffie = new HabboDiffieHellman(Crypto, HabboConnectionType.Server);
    }

    public HabboRSACrypto Crypto { get; }
    public HabboDiffieHellman Diffie { get; }
    public HabboChaCha20? Incoming { get; set; }
    public HabboChaCha20? Outgoing { get; set; }

    public void ProcessIn(Span<byte> header)
    {
        if (Incoming == null)
        {
            return;
        }

        Process(Incoming, header);
    }

    public void ProcessOut(Span<byte> header)
    {
        if (Outgoing == null)
        {
            return;
        }

        Process(Outgoing, header);
    }
    
    private static void Process(HabboChaCha20 cipher, Span<byte> header)
    {
        Debug.Assert(header.Length == 2, "Header length must be 2 bytes.");

        Span<byte> headerReverse = stackalloc byte[2];
        
        headerReverse[0] = header[1];
        headerReverse[1] = header[0];
        
        cipher.Process(headerReverse);
        
        header[0] = headerReverse[1];
        header[1] = headerReverse[0];
    }
}