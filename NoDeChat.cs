using System.Net;
using System.Net.Sockets;
using ChatP2P.Configuracao;
using ChatP2P.Pares;
using ChatP2P.Protocolo;
using ChatP2P.Rede;

namespace ChatP2P;

public sealed class NoDeChat
{
    private static readonly TimeSpan TimeoutConexao = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeoutHandshake = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimeoutEnvio = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimeoutSaida = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IntervaloPing = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeoutOciosidade = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan IntervaloRediscagem = TimeSpan.FromSeconds(5);

    private readonly OpcoesDoNo _opcoes;
    private readonly RegistroDePares _registro = new();
    private readonly Socket _ouvinte;
    private readonly CancellationTokenSource _ctsRaiz = new();

    public NoDeChat(OpcoesDoNo opcoes)
    {
        _opcoes = opcoes;
        _ouvinte = Conexao.CriarOuvinte(opcoes.Porta);
    }

    public void Iniciar()
    {
        Console.WriteLine($"== {_opcoes.Apelido} on-line, escutando na porta {_opcoes.Porta} ==");
        Console.WriteLine("   digite uma mensagem para transmitir | /list | /msg apelido texto | /quit");

        _ = LacoDeAceitacaoAsync(_ctsRaiz.Token);
        _ = DiscarParaParesConhecidosAsync(_ctsRaiz.Token);
    }

    public void Transmitir(string texto)
    {
        Console.WriteLine($"{_opcoes.Apelido} (você): {texto}");
        byte[] envelope = Envelope.Mensagem(_opcoes.Apelido, texto).ParaBytes();
        foreach (ConexaoComPar par in _registro.Todas())
            par.CanalDeSaida.Writer.TryWrite(envelope);
    }

    public void EnviarPrivada(string apelidoDestino, string texto)
    {
        ConexaoComPar? conexao = _registro.BuscarPorApelido(apelidoDestino);
        if (conexao is null)
        {
            Console.WriteLine($"[apelido desconhecido: {apelidoDestino}]");
            return;
        }

        conexao.CanalDeSaida.Writer.TryWrite(Envelope.Privada(_opcoes.Apelido, apelidoDestino, texto).ParaBytes());
        Console.WriteLine($"[privado para {apelidoDestino}]: {texto}");
    }

    public void ListarParticipantes()
    {
        IReadOnlyCollection<ConexaoComPar> pares = _registro.Todas();
        Console.WriteLine($"-- {pares.Count} participante(s) conhecido(s) além de você ({_opcoes.Apelido}) --");
        foreach (ConexaoComPar par in pares.OrderBy(p => p.ApelidoRemoto, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"   {par.ApelidoRemoto}  ({par.EnderecoRemoto})");
    }

    public async Task SairAsync()
    {
        byte[] envelope = Envelope.SaidaNova(_opcoes.Apelido).ParaBytes();
        IReadOnlyCollection<ConexaoComPar> pares = _registro.Todas();

        await Task.WhenAll(pares.Select(async par =>
        {
            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_ctsRaiz.Token);
                cts.CancelAfter(TimeoutSaida);
                await Quadros.EscreverAsync(par.Socket, envelope, cts.Token);
            }
            catch { }
        }));

        await Task.WhenAll(pares.Select(EncerrarSilenciosamenteAsync));

        try { _ouvinte.Dispose(); } catch { }
        await _ctsRaiz.CancelAsync();
    }

    private async Task LacoDeAceitacaoAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await Conexao.EscutarAsync(_ouvinte, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Console.Error.WriteLine($"[erro ao aceitar conexão: {ex.Message}]");
                continue;
            }

            _ = TratarNovaConexaoAsync(socket, Direcao.Entrada, enderecoConhecido: null);
        }
    }

    private async Task DiscarParaParesConhecidosAsync(CancellationToken ct)
    {
        HashSet<string> jaAvisados = new();

        while (!ct.IsCancellationRequested)
        {
            foreach (ParConhecido par in _opcoes.Pares)
            {
                string endereco = $"{par.Host}:{par.Porta}";
                if (_registro.Todas().Any(c => c.EnderecoRemoto == endereco))
                    continue;

                try
                {
                    Socket socket = await Conexao.ConectarAsync(par.Host, par.Porta, TimeoutConexao, ct);
                    jaAvisados.Remove(endereco);
                    _ = TratarNovaConexaoAsync(socket, Direcao.Saida, endereco);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (jaAvisados.Add(endereco))
                        Console.Error.WriteLine($"[não foi possível conectar a {endereco}: {ex.Message}; tentando novamente em segundo plano]");
                }
            }

            try
            {
                await Task.Delay(IntervaloRediscagem, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TratarNovaConexaoAsync(Socket socket, Direcao direcao, string? enderecoConhecido)
    {
        string apelidoRemoto;
        int portaRemota;
        try
        {
            (apelidoRemoto, portaRemota) = await RealizarHandshakeAsync(socket);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[handshake falhou com {socket.RemoteEndPoint}: {ex.Message}]");
            socket.Dispose();
            return;
        }

        if (string.Equals(apelidoRemoto, _opcoes.Apelido, StringComparison.OrdinalIgnoreCase))
        {
            socket.Dispose();
            return;
        }

        if (!RegistroDePares.DeveManterConexao(_opcoes.Apelido, apelidoRemoto, direcao))
        {
            socket.Dispose();
            return;
        }

        string enderecoRemoto = enderecoConhecido
            ?? $"{((IPEndPoint)socket.RemoteEndPoint!).Address}:{portaRemota}";

        ConexaoComPar conexao = new ConexaoComPar
        {
            Socket = socket,
            ApelidoRemoto = apelidoRemoto,
            Direcao = direcao,
            PortaDeEscutaRemota = portaRemota,
            EnderecoRemoto = enderecoRemoto
        };

        ConexaoComPar? antiga = _registro.SubstituirERetornarAntiga(conexao);
        if (antiga is not null)
            await EncerrarSilenciosamenteAsync(antiga);

        Console.WriteLine($"[+] {apelidoRemoto} entrou. Participantes: {_registro.Todas().Count}");

        Task envioTask = LacoDeEnvioAsync(conexao);
        Task recebimentoTask = LacoDeRecebimentoAsync(conexao);
        Task vigiaTask = VigiarOciosidadeAsync(conexao);

        await Task.WhenAny(envioTask, recebimentoTask, vigiaTask);
        await TratarSaidaDoParAsync(conexao, "conexão perdida");
    }

    private async Task<(string ApelidoRemoto, int PortaDeEscuta)> RealizarHandshakeAsync(Socket socket)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_ctsRaiz.Token);
        cts.CancelAfter(TimeoutHandshake);

        await Quadros.EscreverAsync(socket, Envelope.Ola(_opcoes.Apelido, _opcoes.Porta).ParaBytes(), cts.Token);

        byte[] quadro = await Quadros.LerAsync(socket, cts.Token)
            ?? throw new EndOfStreamException("par encerrou antes do handshake");

        Envelope envelope = Envelope.DeBytes(quadro);
        if (envelope.Tipo != TipoDeMensagem.Ola || envelope.PortaDeEscuta is null)
            throw new InvalidDataException("handshake inesperado");

        return (envelope.Remetente, envelope.PortaDeEscuta.Value);
    }

    private async Task LacoDeEnvioAsync(ConexaoComPar conexao)
    {
        try
        {
            await foreach (byte[] payload in conexao.CanalDeSaida.Reader.ReadAllAsync(conexao.Cts.Token))
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(conexao.Cts.Token);
                cts.CancelAfter(TimeoutEnvio);
                await Quadros.EscreverAsync(conexao.Socket, payload, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[falha ao enviar para {conexao.ApelidoRemoto}: {ex.Message}]");
        }
    }

    private async Task LacoDeRecebimentoAsync(ConexaoComPar conexao)
    {
        try
        {
            while (!conexao.Cts.IsCancellationRequested)
            {
                byte[]? quadro = await Quadros.LerAsync(conexao.Socket, conexao.Cts.Token);
                if (quadro is null)
                    return;

                conexao.RegistrarAtividade();
                Envelope envelope = Envelope.DeBytes(quadro);
                await ProcessarEnvelopeAsync(conexao, envelope);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[falha ao receber de {conexao.ApelidoRemoto}: {ex.Message}]");
        }
    }

    private async Task VigiarOciosidadeAsync(ConexaoComPar conexao)
    {
        try
        {
            while (!conexao.Cts.IsCancellationRequested)
            {
                await Task.Delay(IntervaloPing, conexao.Cts.Token);
                conexao.CanalDeSaida.Writer.TryWrite(Envelope.PingNovo(_opcoes.Apelido).ParaBytes());

                if (DateTimeOffset.UtcNow - conexao.UltimaAtividade > TimeoutOciosidade)
                {
                    Console.Error.WriteLine($"[{conexao.ApelidoRemoto} não responde, encerrando conexão]");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task ProcessarEnvelopeAsync(ConexaoComPar conexao, Envelope envelope)
    {
        switch (envelope.Tipo)
        {
            case TipoDeMensagem.Mensagem:
                Console.WriteLine($"{envelope.Remetente}: {envelope.Texto}");
                break;

            case TipoDeMensagem.Privada:
                Console.WriteLine($"[privado de {envelope.Remetente}]: {envelope.Texto}");
                break;

            case TipoDeMensagem.Ping:
                conexao.CanalDeSaida.Writer.TryWrite(Envelope.PongNovo(_opcoes.Apelido).ParaBytes());
                break;

            case TipoDeMensagem.Pong:
                break;

            case TipoDeMensagem.Saida:
                return TratarSaidaDoParAsync(conexao, "saiu do chat");

            case TipoDeMensagem.Ola:
                break;
        }

        return Task.CompletedTask;
    }

    private async Task TratarSaidaDoParAsync(ConexaoComPar conexao, string motivo)
    {
        if (!conexao.MarcarComoEncerrada())
            return;

        _registro.RemoverSe(conexao.ApelidoRemoto, conexao);
        Console.WriteLine($"[-] {conexao.ApelidoRemoto} saiu ({motivo}). Participantes: {_registro.Todas().Count}");
        await conexao.DisposeAsync();
    }

    private async Task EncerrarSilenciosamenteAsync(ConexaoComPar conexao)
    {
        conexao.MarcarComoEncerrada();
        _registro.RemoverSe(conexao.ApelidoRemoto, conexao);
        await conexao.DisposeAsync();
    }
}
