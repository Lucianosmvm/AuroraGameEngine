using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class AnimatorViewModel : ComponentViewModel
{
    public ObservableCollection<AnimationClipViewModel> Clips { get; } = [];
    public ICommand AddClipCommand { get; }

    public ObservableCollection<AnimatorTransitionViewModel> Transitions { get; } = [];
    public ICommand AddTransitionCommand { get; }

    public AnimatorViewModel(JsonObject node) : base(node)
    {
        AddClipCommand = new RelayCommand(AddClip);
        AddTransitionCommand = new RelayCommand(AddTransition);
        RebuildClips();
        RebuildTransitions();
    }

    private void RebuildClips()
    {
        Clips.Clear();
        if (Node["Clips"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                Clips.Add(new AnimationClipViewModel(item, OnClipEdited, RemoveClip));
        }
    }

    private void AddClip()
    {
        var clipNode = new JsonObject
        {
            ["Name"] = "Idle",
            ["Duration"] = 0.1f,
            ["Loop"] = true,
            ["Frames"] = new JsonArray(),
        };
        if (Node["Clips"] is not JsonArray arr)
            Node["Clips"] = arr = [];
        arr.Add(clipNode);
        Clips.Add(new AnimationClipViewModel(clipNode, OnClipEdited, RemoveClip));
        OnClipEdited();
    }

    private void RemoveClip(AnimationClipViewModel clip)
    {
        int index = Clips.IndexOf(clip);
        if (index >= 0 && Node["Clips"] is JsonArray arr && index < arr.Count)
            arr.RemoveAt(index);
        Clips.Remove(clip);
        OnClipEdited();
    }

    private void OnClipEdited() => RaiseEdited("Clips");

    private void RebuildTransitions()
    {
        Transitions.Clear();
        if (Node["Transitions"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                Transitions.Add(new AnimatorTransitionViewModel(item, OnTransitionEdited, RemoveTransition));
        }
    }

    /// <summary>Novo estado padrão: de qualquer clipe pro primeiro da lista quando o
    /// parâmetro "Speed" (convenção comum) atingir 1 — o autor ajusta os campos depois.</summary>
    private void AddTransition()
    {
        var transitionNode = new JsonObject
        {
            ["From"] = "Any",
            ["To"] = Clips.Count > 0 ? Clips[0].ClipName : "",
            ["Parameter"] = "Speed",
            ["CompareOp"] = ">=",
            ["CompareValue"] = 1f,
        };
        if (Node["Transitions"] is not JsonArray arr)
            Node["Transitions"] = arr = [];
        arr.Add(transitionNode);
        Transitions.Add(new AnimatorTransitionViewModel(transitionNode, OnTransitionEdited, RemoveTransition));
        OnTransitionEdited();
    }

    private void RemoveTransition(AnimatorTransitionViewModel transition)
    {
        int index = Transitions.IndexOf(transition);
        if (index >= 0 && Node["Transitions"] is JsonArray arr && index < arr.Count)
            arr.RemoveAt(index);
        Transitions.Remove(transition);
        OnTransitionEdited();
    }

    private void OnTransitionEdited() => RaiseEdited("Transitions");
}
