using Avalonia;
using Jab;
using ShadUI;
using Signalora.Services;
using Signalora.Services.Interface;
using Signalora.ViewModels;

namespace Signalora;

[ServiceProvider]
[Transient<MainWindowViewModel>]
[Transient<DashboardViewModel>]
[Singleton<DevicesViewModel>]
[Singleton<DialogManager>]
[Singleton<ToastManager>]
[Singleton<INetworkScanner, NetworkScanner>]
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