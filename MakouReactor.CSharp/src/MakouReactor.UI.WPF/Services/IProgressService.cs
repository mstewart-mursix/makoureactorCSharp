namespace MakouReactor.UI.WPF.Services;

public interface IProgressService
{
    bool IsBusy { get; }
    string Message { get; }
    event EventHandler? Changed;
    void Show(string message);
    void Clear();
}
