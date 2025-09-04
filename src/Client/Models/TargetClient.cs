using CommunityToolkit.Mvvm.ComponentModel;

namespace Client;

public partial class TargetClient : ObservableObject
{
    [ObservableProperty]
    private string id = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    public TargetClient() { }
    public TargetClient(string id, bool selected = false)
    {
        this.id = id;
        this.isSelected = selected;
    }
}

