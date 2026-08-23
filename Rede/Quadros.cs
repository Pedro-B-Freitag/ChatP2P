using System.Buffers.Binary;
using System.Net.Sockets;

namespace ChatP2P.Rede;

public static class Quadros
{
    public const int TamanhoMaximoDoQuadro = 64 * 1024;

    public static async Task EscreverAsync(Socket socket, ReadOnlyMemory<byte> conteudo, CancellationToken ct = default)
    {
        if (conteudo.Length > TamanhoMaximoDoQuadro)
            throw new ArgumentException($"Conteúdo de {conteudo.Length} bytes excede o limite.");

        byte[] cabecalho = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(cabecalho, conteudo.Length);

        await EnviarTudoAsync(socket, cabecalho, ct);
        await EnviarTudoAsync(socket, conteudo, ct);
    }

    public static async Task<byte[]?> LerAsync(Socket socket, CancellationToken ct = default)
    {
        byte[] cabecalho = new byte[4];
        if (!await LerCompletoAsync(socket, cabecalho, ct))
            return null;

        int tamanho = BinaryPrimitives.ReadInt32BigEndian(cabecalho);
        if (tamanho < 0 || tamanho > TamanhoMaximoDoQuadro)
            throw new InvalidDataException($"Tamanho de quadro inválido: {tamanho}");

        if (tamanho == 0)
            return [];

        byte[] conteudo = new byte[tamanho];
        if (!await LerCompletoAsync(socket, conteudo, ct))
            throw new EndOfStreamException("Conexão encerrada no meio de um quadro.");

        return conteudo;
    }

    private static async Task EnviarTudoAsync(Socket socket, ReadOnlyMemory<byte> dados, CancellationToken ct)
    {
        int enviados = 0;
        while (enviados < dados.Length)
        {
            int n = await socket.SendAsync(dados[enviados..], SocketFlags.None, ct);
            if (n == 0)
                throw new SocketException((int)SocketError.ConnectionReset);
            enviados += n;
        }
    }

    private static async Task<bool> LerCompletoAsync(Socket socket, Memory<byte> destino, CancellationToken ct)
    {
        int lidos = 0;
        while (lidos < destino.Length)
        {
            int n = await socket.ReceiveAsync(destino[lidos..], SocketFlags.None, ct);
            if (n == 0)
            {
                if (lidos == 0)
                    return false;

                throw new EndOfStreamException($"Faltam {destino.Length - lidos} bytes do quadro.");
            }
            lidos += n;
        }
        return true;
    }
}
