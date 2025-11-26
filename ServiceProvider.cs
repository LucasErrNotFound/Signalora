using Avalonia;
using Jab;
using ShadUI;
using Signalora.ViewModels;

namespace Signalora;

[ServiceProvider]
[Transient<MainWindowViewModel>]
[Transient<DashboardViewModel>]
[Singleton<DialogManager>]
[Singleton<ToastManager>]
[Singleton(typeof(PageManager), Factory = nameof(PageManagerFactory))]
[Singleton(typeof(ThemeWatcher), Factory = nameof(ThemeWatcherFactory))]

public partial class ServiceProvider
{
    public PageManager PageManagerFactory()
    {
        return new PageManager(this);
    }
    
    public ThemeWatcher ThemeWatcherFactory()
    {
        return new ThemeWatcher(Application.Current!);
    }
}