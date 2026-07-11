using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class AnimationClipViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;

    public ICommand RemoveCommand { get; }

    public AnimationClipViewModel(JsonObject node, Action onEdited, Action<AnimationClipViewModel> onRemove)
    {
        _node = node;
        _onEdited = onEdited;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    public string ClipName
    {
        get => _node["Name"]?.GetValue<string>() ?? "";
        set { _node["Name"] = value; Raise(); _onEdited(); }
    }

    public float FrameDuration
    {
        get => _node["Duration"]?.GetValue<float>() ?? 0.1f;
        set { _node["Duration"] = value; Raise(); Raise(nameof(FrameDurationText)); _onEdited(); }
    }

    public string FrameDurationText
    {
        get => FrameDuration.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                FrameDuration = f;
        }
    }

    public bool Loop
    {
        get => _node["Loop"]?.GetValue<bool>() ?? true;
        set { _node["Loop"] = value; Raise(); _onEdited(); }
    }

    public string FramesText
    {
        get
        {
            if (_node["Frames"] is not JsonArray arr)
                return "";
            return string.Join(", ", arr.Select(f => f?.GetValue<int>() ?? 0));
        }
        set
        {
            var arr = new JsonArray();
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out int idx))
                    arr.Add(idx);
            _node["Frames"] = arr;
            Raise();
            _onEdited();
        }
    }
}
