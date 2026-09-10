using Nexo.Api.Dados;

namespace Nexo.Api.Testes;

/// <summary>
/// O que a aplicação diz quando a string de conexão está errada.
///
/// Isto existe por causa de uma implantação real. O driver recusou o valor com
/// "Format of the initialization string does not conform to specification
/// starting at index 0" — uma frase que não ajuda ninguém a descobrir que o
/// nome do serviço na referência estava errado.
///
/// A regra dos testes daqui: a mensagem tem que ensinar o conserto, e
/// <b>nunca</b> repetir o valor, que carrega a senha do banco.
/// </summary>
public class ConexaoRuimTeste
{
    [Fact]
    public void Referencia_nao_resolvida_diz_que_o_nome_do_servico_esta_errado()
    {
        var erro = Assert.Throws<InvalidOperationException>(
            () => ConexaoDoBanco.Normalizar("${{Postgres.DATABASE_URL}}"));

        Assert.Contains("referência não resolvida", erro.Message);
        Assert.Contains("nome do serviço", erro.Message);
    }

    [Fact]
    public void Valor_sem_formato_reconhecido_descreve_sem_repetir_o_segredo()
    {
        const string segredo = "senha-secreta-do-banco-que-nao-pode-vazar";

        var erro = Assert.Throws<InvalidOperationException>(
            () => ConexaoDoBanco.Normalizar(segredo));

        /*
         * A mensagem descreve o formato — tamanho, o que falta — sem citar o
         * conteúdo. Log é o último lugar onde uma senha deve aparecer, e uma
         * mensagem de erro é log.
         */
        Assert.DoesNotContain(segredo, erro.Message);
        Assert.Contains(segredo.Length.ToString(), erro.Message);
        Assert.Contains("postgresql://", erro.Message);
    }

    [Fact]
    public void Aspas_coladas_por_engano_sao_removidas()
    {
        const string comAspas = "\"postgresql://u:s@servidor:5432/banco\"";

        var normalizada = ConexaoDoBanco.Normalizar(comAspas);

        Assert.StartsWith("Host=servidor", normalizada);
    }

    [Fact]
    public void A_forma_de_pares_continua_passando_intacta()
    {
        const string pares = "Host=localhost;Port=5432;Database=nexo;Username=nexo;Password=nexo";

        Assert.Equal(pares, ConexaoDoBanco.Normalizar(pares));
    }
}
