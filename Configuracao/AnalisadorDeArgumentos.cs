using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatP2P.Configuracao;

public sealed class ConfiguracaoInvalidaException(string mensagem) : Exception(mensagem);

public static class AnalisadorDeArgumentos
{
    public static OpcoesDoNo Analisar(string[] args)
    {
        int indiceConfig = Array.IndexOf(args, "--config");
        if (indiceConfig >= 0)
        {
            if (indiceConfig + 1 >= args.Length)
                throw new ConfiguracaoInvalidaException("--config requer um caminho de arquivo.");

            return AnalisarArquivo(args[indiceConfig + 1]);
        }

        int porta = LerOpcaoInteiro(args, "--porta")
            ?? throw new ConfiguracaoInvalidaException("--porta é obrigatório (ou use --config).");

        string apelido = LerOpcaoTexto(args, "--apelido")
            ?? throw new ConfiguracaoInvalidaException("--apelido é obrigatório (ou use --config).");

        string? pares = LerOpcaoTexto(args, "--pares");
        List<ParConhecido> listaDePares = pares is null
            ? []
            : pares.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(AnalisarPar).ToList();

        return new OpcoesDoNo(porta, apelido, listaDePares);
    }

    private static OpcoesDoNo AnalisarArquivo(string caminho)
    {
        if (!File.Exists(caminho))
            throw new ConfiguracaoInvalidaException($"Arquivo de configuração não encontrado: {caminho}");

        string json = File.ReadAllText(caminho);
        ArquivoDeConfiguracaoDto dto = JsonSerializer.Deserialize<ArquivoDeConfiguracaoDto>(json)
            ?? throw new ConfiguracaoInvalidaException($"Configuração inválida em {caminho}");

        if (dto.Apelido is null)
            throw new ConfiguracaoInvalidaException("O campo 'apelido' é obrigatório na configuração.");

        List<ParConhecido> listaDePares = (dto.Pares ?? []).Select(AnalisarPar).ToList();
        return new OpcoesDoNo(dto.Porta, dto.Apelido, listaDePares);
    }

    private static ParConhecido AnalisarPar(string entrada)
    {
        string[] partes = entrada.Split(':', 2);
        if (partes.Length != 2 || !int.TryParse(partes[1], out int porta))
            throw new ConfiguracaoInvalidaException($"Par conhecido inválido: '{entrada}' (esperado host:porta).");

        return new ParConhecido(partes[0], porta);
    }

    private static int? LerOpcaoInteiro(string[] args, string nome)
    {
        string? texto = LerOpcaoTexto(args, nome);
        if (texto is null)
            return null;

        return int.TryParse(texto, out int valor)
            ? valor
            : throw new ConfiguracaoInvalidaException($"{nome} deve ser um número inteiro.");
    }

    private static string? LerOpcaoTexto(string[] args, string nome)
    {
        int indice = Array.IndexOf(args, nome);
        if (indice < 0)
            return null;

        if (indice + 1 >= args.Length)
            throw new ConfiguracaoInvalidaException($"{nome} requer um valor.");

        return args[indice + 1];
    }

    private sealed class ArquivoDeConfiguracaoDto
    {
        [JsonPropertyName("porta")]
        public int Porta { get; set; }

        [JsonPropertyName("apelido")]
        public string? Apelido { get; set; }

        [JsonPropertyName("pares")]
        public List<string>? Pares { get; set; }
    }
}
