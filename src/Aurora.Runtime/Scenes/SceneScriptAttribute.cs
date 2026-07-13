namespace Aurora.Runtime.Scenes;

/// <summary>
/// Marca uma classe <see cref="Ecs.Behavior"/> pra registro automático no serializador de
/// cena — sem precisar chamar <see cref="SceneSerializer.Register{T}"/> na mão nem escrever
/// leitura/escrita de JSON campo a campo. Campos e propriedades públicas de tipo
/// <c>float</c>, <c>int</c>, <c>bool</c> ou <c>string</c> viram campos de cena pelo próprio
/// nome, automaticamente. <see cref="Game.AutoRegisterScripts"/> varre o assembly do jogo
/// procurando essa marca antes de <c>OnLoad</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneScriptAttribute : Attribute
{
    /// <summary>Nome usado no campo "Type" da cena. Se omitido, usa o nome da classe.</summary>
    public string? Name { get; }

    public SceneScriptAttribute(string? name = null) => Name = name;
}
