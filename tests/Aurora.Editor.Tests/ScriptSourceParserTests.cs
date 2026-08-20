using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// O parser de texto é o que faz um script recém-salvo no editor interno já aparecer no
/// "+ Add Componente" — se ele errar campo/nome, o inspector escreve JSON que o runtime ignora
/// (ou pior, com default diferente do que a classe tem). Estes testes prendem o formato de saída
/// no mesmo contrato do describe-scripts do runtime: kind em float/int/bool/string e default como
/// texto invariante.
/// </summary>
public class ScriptSourceParserTests
{
    [Fact]
    public void Parse_LeNomeECamposDeUmScriptSimples()
    {
        var scripts = ScriptSourceParser.Parse("""
            using Aurora.Runtime.Ecs;

            namespace MeuJogo;

            [SceneScript]
            public sealed class CharacterController : Behavior
            {
                public float Speed = 200f;
                public int Vidas = 3;
                public bool PodePular = true;
                public string Alvo = "Player";

                public override void Update(float deltaTime)
                {
                    float local = 1f;
                }
            }
            """);

        var script = Assert.Single(scripts);
        Assert.Equal("CharacterController", script.Name);
        Assert.Equal(
            [("Speed", "float", "200"), ("Vidas", "int", "3"), ("PodePular", "bool", "true"), ("Alvo", "string", "Player")],
            script.Fields.Select(f => (f.Name, f.Kind, f.Default)));
    }

    [Fact]
    public void Parse_UsaOAliasDoAtributoComoNomeDoComponente()
    {
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript("MovimentoTop")]
            public sealed class CharacterController : Behavior { }
            """);

        Assert.Equal("MovimentoTop", Assert.Single(scripts).Name);
    }

    [Fact]
    public void FindPrimaryClassName_DevolveOTipoNaoOAlias()
    {
        // O nome do arquivo segue a classe, não o alias — senão salvaria MovimentoTop.cs com
        // "class CharacterController" dentro.
        string? name = ScriptSourceParser.FindPrimaryClassName("""
            [SceneScript("MovimentoTop")]
            public sealed class CharacterController : Behavior { }
            """);

        Assert.Equal("CharacterController", name);
    }

    [Fact]
    public void Parse_IgnoraAtributoComentado()
    {
        var scripts = ScriptSourceParser.Parse("""
            // [SceneScript]
            // public sealed class Fantasma : Behavior { }

            /* [SceneScript]
            public sealed class OutroFantasma : Behavior { } */

            [SceneScript]
            public sealed class DeVerdade : Behavior { }
            """);

        Assert.Equal("DeVerdade", Assert.Single(scripts).Name);
    }

    [Fact]
    public void Parse_NaoConfundeBarrasDentroDeStringComComentario()
    {
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public sealed class Config : Behavior
            {
                public string Url = "http://exemplo/rota";
                public float Volume = 0.5f;
            }
            """);

        var fields = Assert.Single(scripts).Fields;
        Assert.Equal("http://exemplo/rota", fields[0].Default);
        Assert.Equal("0.5", fields[1].Default);
    }

    [Fact]
    public void Parse_AceitaPropriedadeComGetSet()
    {
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public sealed class Arma : Behavior
            {
                public float Dano { get; set; } = 15f;
                public int Municao { get; } = 10;
            }
            """);

        var field = Assert.Single(Assert.Single(scripts).Fields);
        Assert.Equal(("Dano", "float", "15"), (field.Name, field.Kind, field.Default));
    }

    [Fact]
    public void Parse_PulaMembrosQueORuntimeNaoLe()
    {
        // static/const não são membros de instância (SceneSerializer.GetScriptableMembers),
        // privado não é público, e Vector2 não é um dos quatro tipos suportados.
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public sealed class Inimigo : Behavior
            {
                public const float Gravidade = 9.8f;
                public static int Contador = 0;
                private float _timer = 1f;
                public Vector2 Direcao = Vector2.Zero;
                public float Vida = 30f;
            }
            """);

        var field = Assert.Single(Assert.Single(scripts).Fields);
        Assert.Equal("Vida", field.Name);
    }

    [Fact]
    public void Parse_IgnoraClasseAbstrata()
    {
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public abstract class BaseInimigo : Behavior
            {
                public float Vida = 10f;
            }
            """);

        Assert.Empty(scripts);
    }

    [Fact]
    public void Parse_SemInicializadorUsaODefaultDoTipo()
    {
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public sealed class Vazio : Behavior
            {
                public float Escala;
                public string Nome;
            }
            """);

        var fields = Assert.Single(scripts).Fields;
        Assert.Equal("0", fields[0].Default);
        Assert.Equal("", fields[1].Default);
    }

    [Fact]
    public void Parse_ExpressaoNaoLiteralViraDefaultDoTipo()
    {
        // O editor não avalia expressão; melhor cair no default do tipo do que escrever lixo no
        // JSON da cena — o "↻" (reflection no assembly) corrige com o valor real.
        var scripts = ScriptSourceParser.Parse("""
            [SceneScript]
            public sealed class Calculado : Behavior
            {
                public float Raio = Largura * 2f;
            }
            """);

        Assert.Equal("0", Assert.Single(Assert.Single(scripts).Fields).Default);
    }

    [Theory]
    [InlineData("Movement")]
    [InlineData("Weapon")]
    [InlineData("Enemy")]
    [InlineData("Item")]
    [InlineData("Magic")]
    [InlineData("Empty")]
    public void Parse_LeTodosOsTemplatesDoBotaoNovo(string templateId)
    {
        // Todo template do "+ Novo…" precisa ser legível pelo parser, senão o script criado pelo
        // editor interno não aparece na lista de componentes até alguém apertar "↻".
        var template = ScriptTemplates.All.Single(t => t.Id == templateId);
        string source = ScriptTemplates.Build(template, "MeuJogo", template.DefaultClassName);

        var script = Assert.Single(ScriptSourceParser.Parse(source));
        Assert.Equal(template.DefaultClassName, script.Name);
        Assert.All(script.Fields, f => Assert.Contains(f.Kind, new[] { "float", "int", "bool", "string" }));
    }

    [Fact]
    public void Parse_ArquivoSemSceneScriptNaoDevolveNada()
    {
        Assert.Empty(ScriptSourceParser.Parse("public sealed class Util { public float X = 1f; }"));
        Assert.Null(ScriptSourceParser.FindPrimaryClassName("public sealed class Util { }"));
    }
}
