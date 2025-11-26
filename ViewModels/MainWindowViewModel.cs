using System;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotAvalonia;
using ShadUI;

namespace Signalora.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, INavigable
{
    [ObservableProperty] private DialogManager _dialogManager;
    [ObservableProperty] private ToastManager _toastManager;
    [ObservableProperty] private object? _selectedPage;
    [ObservableProperty] private string _currentRoute = "dashboard";
    
    private readonly PageManager _pageManager;
    private readonly ThemeWatcher _themeWatcher;
    private readonly DashboardViewModel _dashboardViewModel;
    
    private ThemeMode _currentTheme;

    public MainWindowViewModel(DialogManager dialogManager, 
        ToastManager toastManager, 
        PageManager pageManager,
        ThemeWatcher themeWatcher,
        DashboardViewModel dashboardViewModel)
    {
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _pageManager = pageManager;
        _themeWatcher =  themeWatcher;
        
        _dashboardViewModel = dashboardViewModel;
        
        _pageManager.OnNavigate = SwitchPage;
    }

    public MainWindowViewModel()
    {
        _dialogManager = new DialogManager();
        _toastManager = new ToastManager();
        _pageManager = new PageManager(new ServiceProvider());

        _dashboardViewModel = new DashboardViewModel();
    }
    
    [AvaloniaHotReload]
    public void Initialize()
    {
        SwitchPage(_dashboardViewModel);
    }
    
    private void SwitchPage(INavigable page, string route = "")
    {
        try
        {
            var pageType = page.GetType();
            if (string.IsNullOrEmpty(route)) route = pageType.GetCustomAttribute<PageAttribute>()?.Route ?? "dashboard";
            CurrentRoute = route;

            if (SelectedPage == page) return;
            SelectedPage = page;
            CurrentRoute = route;
            page.Initialize();
            Debug.WriteLine("Success!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error switching page: {ex.Message}");
        }
    }
    
    public ThemeMode CurrentTheme
    {
        get => _currentTheme;
        private set => SetProperty(ref _currentTheme, value);
    }
    
    [RelayCommand]
    private void SwitchTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        _themeWatcher.SwitchTheme(CurrentTheme);
    }
    
    [RelayCommand] private void OpenDashboard() => SwitchPage(_dashboardViewModel);
}