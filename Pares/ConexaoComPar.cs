using System.Net.Sockets;
using System.Threading.Channels;

namespace ChatP2P.Pares;

public enum Direcao
{
    Saida,
    Entrada
}

public sealed class ConexaoComPar : IAsyncDisposable
{
    private int _encerrada;

    public required Socket Socket { get; init; }
    public required string ApelidoRemoto { get; init; }
    public required Direcao Direcao { get; init; }
    public required int PortaDeEscutaRemota { get; init; }
    public required string EnderecoRemoto { get; init; }

    public Channel<byte[]> CanalDeSaida { get; } = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public CancellationTokenSource Cts { get; } = new();
    public DateTimeOffset UltimaAtividade { get; private set; } = DateTimeOffset.UtcNow;

    public void RegistrarAtividade() => UltimaAtividade = DateTimeOffset.UtcNow;

    public bool MarcarComoEncerrada() => Interlocked.Exchange(ref _encerrada, 1) == 0;

    public async ValueTask DisposeAsync()
    {
        CanalDeSaida.Writer.TryComplete();

        try
        {
            await Cts.CancelAsync();
        }
        catch (ObjectDisposedException) { }

        try
        {
            Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        Socket.Dispose();
        Cts.Dispose();
    }
}
