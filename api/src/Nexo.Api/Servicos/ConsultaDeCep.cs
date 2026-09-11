using System.Text.Json.Serialization;
using Nexo.Api.Dominio;

namespace Nexo.Api.Servicos;

public record EnderecoDoCep(
    string Cep,
    string Logradouro,
    string Bairro,
    string Cidade,
    string Uf);

public enum ResultadoDaConsulta
{
    /// <summary>O CEP existe e o endereço veio.</summary>
    Encontrado = 1,

    /// <summary>O serviço respondeu, e disse que este CEP não existe.</summary>
    NaoEncontrado = 2,

    /// <summary>
    /// Não deu para perguntar: fora do ar, lento demais, sem internet. Diferente
    /// de não encontrado, e a tela precisa dizer coisas diferentes — um pede
    /// para conferir o número digitado, o outro para preencher à mão.
    /// </summary>
    Indisponivel = 3,
}

public record RespostaDoCep(ResultadoDaConsulta Resultado, EnderecoDoCep? Endereco = null);

public interface IConsultaDeCep
{
    Task<RespostaDoCep> BuscarAsync(string cep, CancellationToken cancelamento);
}

/// <summary>
/// Consulta de CEP no ViaCEP.
///
/// <para>
/// <b>É a primeira dependência externa do sistema</b>, e por isso ela entra
/// pela porta estreita: a consulta só preenche campos que a pessoa poderia
/// digitar sozinha, nada depende da resposta, e falha não impede salvar. Quando
/// o serviço cair — e vai —, o cadastro continua funcionando exatamente como
/// funcionava antes de isto existir.
/// </para>
/// <para>
/// A chamada sai <b>do servidor</b>, e não do navegador, por duas razões. O
/// desenho de domínio único (Q29) vale para tudo: o navegador fala com um
/// endereço só. E quem usa o sistema não precisa aparecer no registro de
/// acesso de terceiro só porque digitou um CEP.
/// </para>
/// <para>
/// O tempo de espera é curto de propósito. Este é um atalho de digitação: se
/// demorar mais do que digitar o endereço à mão, deixou de ser atalho.
/// </para>
/// </summary>
public sealed class ConsultaDeCepViaCep(HttpClient http, ILogger<ConsultaDeCepViaCep> registro)
    : IConsultaDeCep
{
    public async Task<RespostaDoCep> BuscarAsync(string cep, CancellationToken cancelamento)
    {
        var numero = Documento.ApenasDigitos(cep);
        if (numero.Length != 8) return new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado);

        try
        {
            var resposta = await http.GetAsync($"ws/{numero}/json/", cancelamento);

            if (!resposta.IsSuccessStatusCode)
            {
                registro.LogWarning(
                    "O ViaCEP respondeu {Codigo} para uma consulta de CEP.", (int)resposta.StatusCode);
                return new RespostaDoCep(ResultadoDaConsulta.Indisponivel);
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<CorpoDoViaCep>(cancelamento);

            /*
             * CEP inexistente não vira 404 no ViaCEP: vem 200 com `"erro": true`.
             * Tratar isso como sucesso encheria o formulário de campos vazios.
             */
            if (corpo is null || corpo.Erro || string.IsNullOrWhiteSpace(corpo.Localidade))
                return new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado);

            return new RespostaDoCep(ResultadoDaConsulta.Encontrado, new EnderecoDoCep(
                numero,
                corpo.Logradouro ?? string.Empty,
                corpo.Bairro ?? string.Empty,
                corpo.Localidade,
                corpo.Uf ?? string.Empty));
        }
        catch (Exception erro) when (erro is HttpRequestException or TaskCanceledException)
        {
            /*
             * Rede fora, DNS ruim, estouro do tempo de espera. Só estas: outra
             * exceção é defeito nosso e precisa aparecer, não virar
             * "indisponível".
             */
            registro.LogWarning(erro, "Não foi possível consultar o CEP.");
            return new RespostaDoCep(ResultadoDaConsulta.Indisponivel);
        }
    }

    private sealed record CorpoDoViaCep
    {
        public string? Logradouro { get; init; }
        public string? Bairro { get; init; }
        public string? Localidade { get; init; }
        public string? Uf { get; init; }

        /*
         * O ViaCEP já mandou `"erro": "true"` como texto e `"erro": true` como
         * booleano em momentos diferentes. Ler como texto aceita os dois, e o
         * dia em que mudar de novo não quebra o cadastro inteiro.
         */
        [JsonConverter(typeof(ErroComoTexto))]
        public bool Erro { get; init; }
    }

    private sealed class ErroComoTexto : System.Text.Json.Serialization.JsonConverter<bool>
    {
        public override bool Read(
            ref System.Text.Json.Utf8JsonReader leitor,
            Type tipo,
            System.Text.Json.JsonSerializerOptions opcoes) =>
            leitor.TokenType switch
            {
                System.Text.Json.JsonTokenType.True => true,
                System.Text.Json.JsonTokenType.False => false,
                System.Text.Json.JsonTokenType.String =>
                    bool.TryParse(leitor.GetString(), out var valor) && valor,
                _ => false,
            };

        public override void Write(
            System.Text.Json.Utf8JsonWriter escritor,
            bool valor,
            System.Text.Json.JsonSerializerOptions opcoes) => escritor.WriteBooleanValue(valor);
    }
}
