using ChatP2P;
using ChatP2P.Configuracao;

OpcoesDoNo opcoes;
try
{
    opcoes = AnalisadorDeArgumentos.Analisar(args);
}
catch (ConfiguracaoInvalidaException ex)
{
    Console.Error.WriteLine($"Configuração inválida: {ex.Message}");
    ImprimirUso();
    return 1;
}

NoDeChat no;
try
{
    no = new NoDeChat(opcoes);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Não foi possível iniciar o nó: {ex.Message}");
    return 1;
}

no.Iniciar();

while (true)
{
    string? linha = Console.ReadLine();
    if (linha is null)
        continue;

    if (linha.Length > 0 && linha[0] == (char)0xFEFF)
        linha = linha[1..];

    linha = linha.Trim();
    if (linha.Length == 0)
        continue;

    if (linha.Equals("/quit", StringComparison.OrdinalIgnoreCase))
    {
        await no.SairAsync();
        break;
    }

    if (linha.Equals("/list", StringComparison.OrdinalIgnoreCase))
    {
        no.ListarParticipantes();
        continue;
    }

    if (linha.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
    {
        string resto = linha["/msg ".Length..].TrimStart();
        int separador = resto.IndexOf(' ');
        if (separador < 0)
        {
            Console.WriteLine("uso: /msg apelido texto");
            continue;
        }

        string destino = resto[..separador];
        string texto = resto[(separador + 1)..];
        no.EnviarPrivada(destino, texto);
        continue;
    }

    no.Transmitir(linha);
}

return 0;

static void ImprimirUso() => Console.WriteLine("""
    chatp2p: chat distribuído em malha completa, sem servidor central.

      chatp2p --porta <porta> --apelido <nome> [--pares host:porta,host:porta,...]
      chatp2p --config <arquivo.json>

    Exemplo:
      chatp2p --porta 9001 --apelido alice --pares 127.0.0.1:9002,127.0.0.1:9003
      chatp2p --config pares.exemplo.json

    Comandos durante a conversa:
      /list             lista os participantes atualmente conhecidos
      /msg apelido txt  envia uma mensagem privada
      /quit             sai anunciando a saída
    """);
