using System.Net;
using System.Net.Sockets;

namespace ChatP2P.Rede;

public static class Conexao
{
    public static async Task<Socket> EscutarAsync(Socket ouvinte, CancellationToken ct)
    {
        Socket socket = await ouvinte.AcceptAsync(ct);
        socket.NoDelay = true;
        return socket;
    }

    public static Socket CriarOuvinte(int porta)
    {
        Socket ouvinte = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ouvinte.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        ouvinte.Bind(new IPEndPoint(IPAddress.Any, porta));
        ouvinte.Listen(32);
        return ouvinte;
    }

    public static async Task<Socket> ConectarAsync(string host, int porta, TimeSpan timeout, CancellationToken ct)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            using CancellationTokenSource comTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            comTimeout.CancelAfter(timeout);
            await socket.ConnectAsync(host, porta, comTimeout.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
