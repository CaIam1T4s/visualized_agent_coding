using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace happy_agent_coding.ViewModels;

/// <summary>Editable metadata for one role costume; runtime selection is not stored here.</summary>
public partial class StageCostumeItem : ObservableObject
{
    public StageCostumeItem(string id, string name, string imagePath, bool normalizeImage)
    {
        Id = id;
        Name = name;
        ImagePath = imagePath;
        NormalizeImage = normalizeImage;
    }

    public string Id { get; }
    public string ImagePath { get; }
    public bool NormalizeImage { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string name;
    public string FileName => Path.GetFileName(ImagePath);
    public string Summary => Name;
}
