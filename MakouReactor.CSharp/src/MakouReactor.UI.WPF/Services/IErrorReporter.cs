using System.Windows;

namespace MakouReactor.UI.WPF.Services;

public interface IErrorReporter
{
    void Report(Window owner, string title, Exception exception);
}
