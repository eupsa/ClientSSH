using ClientSSH.Services.Assistant;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Shell;
using ClientSSH.Services.Storage;
using ClientSSH.Services.Updates;

﻿using ClientSSH.Services;
using ClientSSH.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace ClientSSH
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; } = ConfigureServices();

        public static Window MainWindow { get; private set; } = null!;

        public static bool LaunchedByStartup =>
            Environment.GetCommandLineArgs().Contains(StartupService.StartupArgument) ||
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind ==
                Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;

        public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

        public App()
        {
            // Must run before any UI loads so resources resolve in the chosen language.
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                GetService<ISettingsStore>().Current.Language;
            // Before anything touches the data folder: a second launch only wakes the first one.
            if (!SingleInstance.TryAcquire()) Environment.Exit(0);
            RunMarker.Begin();
            InitializeComponent();
            UnhandledException += (_, e) => LogCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        }

        // Local only (nothing is sent): crashes are appended to crash.log next to the database, to attach to a bug report.
        private static void LogCrash(Exception? exception)
        {
            try
            {
                File.AppendAllText(Path.Combine(LocalData.Dir, "crash.log"), $"{DateTime.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
            SingleInstance.OnSecondLaunch(() => MainWindow.DispatcherQueue.TryEnqueue(GetService<TrayService>().Show));
            GetService<RemoteViewModel>().Start(); // this PC can be called while the app runs, also from the tray

            if (LaunchedByStartup && GetService<ISettingsStore>().Current.StartMinimized)
                GetService<TrayService>().HideToTray();
        }

        private static Database CreateDatabase()
        {
            var database = new Database(Path.Combine(LocalData.Dir, "clientssh.db"));
            LegacyHostsImport.Run(database, Path.Combine(LocalData.Dir, "hosts.json"), AppJsonContext.Default.ListHost);
            return database;
        }

        private static IServiceProvider ConfigureServices() => new ServiceCollection()
            .AddSingleton(_ => CreateDatabase())
            .AddSingleton<IHostStore, HostStore>()
            .AddSingleton<IHistoryStore, HistoryStore>()
            .AddSingleton<ISettingsStore, SettingsStore>()
            .AddSingleton<INavigationService, NavigationService>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            // Concrete types stay resolvable for the window code; view models take the interfaces (same instance).
            .AddSingleton<ThemeService>()
            .AddSingleton<IThemeService>(s => s.GetRequiredService<ThemeService>())
            .AddSingleton<WindowStateService>()
            .AddSingleton<IStartupService, StartupService>()
            .AddSingleton<SessionManager>()
            .AddSingleton<ISessionManager>(s => s.GetRequiredService<SessionManager>())
            .AddSingleton<Services.Customization.ProfileService>()
            .AddSingleton<Services.Customization.AppearanceService>()
            .AddSingleton<Services.Shortcuts.ShortcutService>()
            .AddSingleton<ProfilesViewModel>()
            .AddSingleton<AppearanceViewModel>()
            .AddSingleton<TrayService>()
            .AddSingleton<ITrayService>(s => s.GetRequiredService<TrayService>())
            .AddSingleton<IUpdateService, UpdateService>()
            .AddSingleton<AiKeyStore>()
            .AddSingleton<IAiKeyStore>(s => s.GetRequiredService<AiKeyStore>())
            .AddSingleton<IAssistantService, AssistantService>()
            .AddSingleton<AssistantViewModel>()
            .AddSingleton<RemoteViewModel>()
            .AddSingleton<TourViewModel>()
            .AddTransient<SettingsViewModel>()
            .AddTransient<ShortcutsViewModel>()
            .AddSingleton<HostsViewModel>()
            .AddTransient<WelcomeViewModel>()
            .AddTransient<SessionsViewModel>()
            .BuildServiceProvider();
    }
}
