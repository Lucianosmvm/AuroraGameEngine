using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class AnimatorViewModel : ComponentViewModel
{
    public ObservableCollection<AnimationClipViewModel> Clips { get; } = [];
    public ICommand AddClipCommand { get; }

    public AnimatorViewModel(JsonObject node) : base(node)
    {
        AddClipCommand = new RelayCommand(AddClip);
        RebuildClips();
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
}
