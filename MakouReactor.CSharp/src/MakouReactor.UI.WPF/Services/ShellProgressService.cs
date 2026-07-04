namespace MakouReactor.UI.WPF.Services;

public sealed class ShellProgressService : IProgressService
{
    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public event EventHandler? Changed;

    public void Show(string message)
    {
        IsBusy = true;
        Message = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        IsBusy = false;
        Message = string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
