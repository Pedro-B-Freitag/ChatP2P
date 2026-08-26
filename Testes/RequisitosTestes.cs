using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatP2P;
using ChatP2P.Configuracao;
using ChatP2P.Pares;
using ChatP2P.Protocolo;
using ChatP2P.Rede;
using Xunit;

namespace ChatP2P.Testes;

public class FuncoesPrincipaisTests
{
    [Fact]
    // OBJETIVO PRINCIPAL
    public async Task IniciaEEncerraNo()
    {
        NoDeChat no = new(new OpcoesDoNo(0, "alice", []));

        no.Iniciar();
        await no.SairAsync();
    }

    [Fact]
    // REQ1
    public void LeArgumentos()
    {
        OpcoesDoNo opcoes = AnalisadorDeArgumentos.Analisar([
            "--porta", "9001", "--apelido", "alice", "--pares", "127.0.0.1:9002, bob:9003"]);

        Assert.Equal(9001, opcoes.Porta);
        Assert.Equal("alice", opcoes.Apelido);
        Assert.Equal([new ParConhecido("127.0.0.1", 9002), new ParConhecido("bob", 9003)], opcoes.Pares);
    }

    [Fact]
    // REQ1
    public void RejeitaArgumentosSemPorta()
    {
        ConfiguracaoInvalidaException erro = Assert.Throws<ConfiguracaoInvalidaException>(
            () => AnalisadorDeArgumentos.Analisar(["--apelido", "alice"]));

        Assert.Contains("--porta é obrigatório", erro.Message);
    }

    [Fact]
    // REQ2, REQ8
    public void RegistraEConsultaPar()
    {
        RegistroDePares registro = new();
        ConexaoComPar primeira = CriarPar("bob");
        ConexaoComPar segunda = CriarPar("bob");

        Assert.Null(registro.SubstituirERetornarAntiga(primeira));
        Assert.Same(primeira, registro.SubstituirERetornarAntiga(segunda));
        Assert.Same(segunda, registro.BuscarPorApelido("BOB"));
        Assert.True(registro.RemoverSe("bob", segunda));
        Assert.Empty(registro.Todas());

        primeira.Socket.Dispose();
        segunda.Socket.Dispose();
    }

    [Fact]
    // REQ3
    public async Task EnviaMensagemAosPares()
    {
        using Socket ouvinte = Conexao.CriarOuvinte(0);
        int porta = ((IPEndPoint)ouvinte.LocalEndPoint!).Port;
        Task<Socket> aceitacao1 = Conexao.EscutarAsync(ouvinte, CancellationToken.None);
        using Socket cliente1 = await Conexao.ConectarAsync("127.0.0.1", porta, TimeSpan.FromSeconds(2), CancellationToken.None);
        using Socket par1 = await aceitacao1;
        Task<Socket> aceitacao2 = Conexao.EscutarAsync(ouvinte, CancellationToken.None);
        using Socket cliente2 = await Conexao.ConectarAsync("127.0.0.1", porta, TimeSpan.FromSeconds(2), CancellationToken.None);
        using Socket par2 = await aceitacao2;

        byte[] mensagem = Envelope.Mensagem("alice", "oi, pessoal").ParaBytes();
        await Quadros.EscreverAsync(cliente1, mensagem);
        await Quadros.EscreverAsync(cliente2, mensagem);

        Assert.Equal(mensagem, await Quadros.LerAsync(par1));
        Assert.Equal(mensagem, await Quadros.LerAsync(par2));
    }

    [Fact]
    // REQ4
    public void IdentificaAutor()
    {
        Envelope recebido = Envelope.DeBytes(Envelope.Mensagem("alice", "oi").ParaBytes());

        Assert.Equal("alice", recebido.Remetente);
        Assert.Equal("oi", recebido.Texto);
    }

    [Fact]
    // REQ5
    public async Task SeparaMensagens()
    {
        using Socket ouvinte = Conexao.CriarOuvinte(0);
        int porta = ((IPEndPoint)ouvinte.LocalEndPoint!).Port;
        Task<Socket> aceitacao = Conexao.EscutarAsync(ouvinte, CancellationToken.None);
        using Socket cliente = await Conexao.ConectarAsync("127.0.0.1", porta, TimeSpan.FromSeconds(2), CancellationToken.None);
        using Socket servidor = await aceitacao;

        byte[][] mensagens =
        [
            Encoding.UTF8.GetBytes("primeira mensagem"),
            Encoding.UTF8.GetBytes(new string('x', 10_000)),
            Encoding.UTF8.GetBytes("última mensagem")
        ];

        foreach (byte[] mensagem in mensagens)
            await Quadros.EscreverAsync(cliente, mensagem);

        foreach (byte[] mensagem in mensagens)
            Assert.Equal(mensagem, await Quadros.LerAsync(servidor));
    }

    [Fact]
    // REQ5
    public async Task RecusaMensagemGrande()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Quadros.EscreverAsync(socket, new byte[Quadros.TamanhoMaximoDoQuadro + 1]));
    }

    [Fact]
    // REQ6
    public async Task CancelaEscutaNoPrazo()
    {
        using Socket ouvinte = Conexao.CriarOuvinte(0);
        using CancellationTokenSource prazo = new();
        Task<Socket> tarefa = Conexao.EscutarAsync(ouvinte, prazo.Token);
        prazo.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
    }

    [Fact]
    // REQ7
    public async Task MantemOutroOuvinte()
    {
        using Socket caiu = Conexao.CriarOuvinte(0);
        using Socket continua = Conexao.CriarOuvinte(0);
        int porta = ((IPEndPoint)continua.LocalEndPoint!).Port;
        caiu.Dispose();

        Task<Socket> aceitacao = Conexao.EscutarAsync(continua, CancellationToken.None);
        using Socket cliente = await Conexao.ConectarAsync("127.0.0.1", porta, TimeSpan.FromSeconds(2), CancellationToken.None);
        using Socket servidor = await aceitacao;

        Assert.True(servidor.Connected);
    }

    [Fact]
    // REQ8
    public async Task RemovePar()
    {
        RegistroDePares registro = new();
        ConexaoComPar par = CriarPar("bob");
        registro.SubstituirERetornarAntiga(par);

        Assert.True(registro.RemoverSe("bob", par));
        Assert.Null(registro.BuscarPorApelido("bob"));

        await par.DisposeAsync();
    }

    [Fact]
    // REQ9
    public async Task LimitaFilaNoFlood()
    {
        ConexaoComPar par = CriarPar("bob");
        for (int numero = 0; numero < 250; numero++)
            Assert.True(par.CanalDeSaida.Writer.TryWrite(Encoding.UTF8.GetBytes(numero.ToString())));

        List<string> mensagens = [];
        while (par.CanalDeSaida.Reader.TryRead(out byte[]? mensagem))
            mensagens.Add(Encoding.UTF8.GetString(mensagem));

        Assert.Equal(200, mensagens.Count);
        Assert.Equal("50", mensagens[0]);
        Assert.Equal("249", mensagens[^1]);

        await par.DisposeAsync();
    }

    [Fact]
    // REQ10
    public async Task ListaParticipantes()
    {
        using StringWriter saida = new();
        TextWriter saidaAnterior = Console.Out;
        Console.SetOut(saida);
        try
        {
            NoDeChat no = new(new OpcoesDoNo(0, "alice", []));
            no.ListarParticipantes();
            await no.SairAsync();
        }
        finally
        {
            Console.SetOut(saidaAnterior);
        }

        Assert.Contains("0 participante(s)", saida.ToString());
        Assert.Contains("alice", saida.ToString());
    }

    [Fact]
    // REQ11
    public void CriaMensagemPrivada()
    {
        Envelope original = Envelope.Privada("alice", "bob", "mensagem só para você");
        Envelope recebido = Envelope.DeBytes(original.ParaBytes());

        Assert.Equal(TipoDeMensagem.Privada, recebido.Tipo);
        Assert.Equal("alice", recebido.Remetente);
        Assert.Equal("bob", recebido.Destinatario);
        Assert.Equal("mensagem só para você", recebido.Texto);
    }

    private static ConexaoComPar CriarPar(string apelido) => new()
    {
        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp),
        ApelidoRemoto = apelido,
        Direcao = Direcao.Saida,
        PortaDeEscutaRemota = 9002,
        EnderecoRemoto = "127.0.0.1:9002"
    };
}
