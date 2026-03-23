using adrilight.Resources;
using adrilight.ui;
using adrilight.Util;
using adrilight.ViewModel;
using Microsoft.Win32;
using Ninject;
using Ninject.Extensions.Conventions;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace adrilight
{
    public sealed partial class App : Application
    {
        private static Mutex _adrilightMutex;
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SystemParametersInfo(uint uAction, uint uParam, ref bool lpvParam, uint fuWinIni);
        private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;

        private DispatcherTimer _screenSaverTimer;
        private Util.SleepWakeController _sleepWakeController;
        private Util.IModeManager _modeManager;
        private ViewModel.DiagnosticsViewModel _diagnosticsViewModel;

        protected override void OnStartup(StartupEventArgs startupEvent)
        {
            ReadVersionDetails();

            if (!IsSupported())
            {
                var os = Environment.OSVersion;
                MessageBox.Show(
                    $"Your Windows version is not supported by adrilight, sorry!\n\n"
                    + $"Platform={os.Platform}\nVersion={os.Version}\nService Pack={os.ServicePack}\n\n\n"
                    + "adrilight requires Windows 10 or later.",
                    "Your Windows version is too old!", MessageBoxButton.OK);
                Shutdown();
                return;
            }

            _adrilightMutex = new Mutex(true, "adrilight2");
            if (!_adrilightMutex.WaitOne(TimeSpan.Zero, true))
            {
                MessageBox.Show("There is already an instance of adrilight running. Please start only a single instance at any given time.",
                    "Adrilight is already running!");
                Shutdown();
                return;
            }

            _diagnosticsViewModel = new ViewModel.DiagnosticsViewModel();
            SetupLogging();
            SetupLoggingForProcessWideEvents();

            base.OnStartup(startupEvent);

            _log.Debug($"adrilight {VersionNumber}: Main() started.");
            kernel = SetupDependencyInjection(false, _diagnosticsViewModel);

            this.Resources["Locator"] = new ViewModelLocator(kernel);

            UserSettings = kernel.Get<IUserSettings>();
            _modeManager = kernel.Get<Util.IModeManager>();

            var isNewVersion = VersionNumber != UserSettings.AdrilightVersion;
            if (!IsPrivateBuild && isNewVersion)
                UserSettings.AdrilightVersion = VersionNumber;

            SetupNotifyIcon();

            if (!UserSettings.StartMinimized || isNewVersion)
            {
                OpenSettingsWindow();
            }

            _nightLightDetection = kernel.Get<NightLightDetection>();
            _nightLightDetection.Start();

            _tcpControlServer = new TcpControlServer(UserSettings, _modeManager, 5080);
            _tcpControlServer.Start();

            _sleepWakeController = new Util.SleepWakeController(UserSettings, _modeManager);
            SetupScreenSaverTimer();
        }

        private bool IsSupported()
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version.Major >= 10;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _screenSaverTimer?.Stop();
            _tcpControlServer?.Stop();
            _nightLightDetection?.Stop();
            base.OnExit(e);
            _adrilightMutex?.Dispose();
            LogManager.Shutdown();
        }

        internal static IKernel SetupDependencyInjection(bool isInDesignMode,
            ViewModel.DiagnosticsViewModel diagnosticsViewModel = null,
            string settingsFolder = null)
        {
            var kernel = new StandardKernel();
            if (isInDesignMode)
            {
                kernel.Bind<IUserSettings>().To<Fakes.UserSettingsFake>().InSingletonScope();
                kernel.Bind<IContext>().To<Fakes.ContextFake>().InSingletonScope();
                kernel.Bind<ISpotSet>().To<Fakes.SpotSetFake>().InSingletonScope();
                kernel.Bind<ISerialStream>().To<Fakes.SerialStreamFake>().InSingletonScope();
                kernel.Bind<IDesktopDuplicatorReader>().To<Fakes.DesktopDuplicatorReaderFake>().InSingletonScope();
                kernel.Bind<Util.IModeManager>().To<Fakes.ModeManagerFake>().InSingletonScope();
            }
            else
            {
                var settingsManager = new UserSettingsManager(settingsFolder);
                var settings = settingsManager.LoadIfExists() ?? settingsManager.MigrateOrDefault();
                kernel.Bind<IUserSettings>().ToConstant(settings);
                kernel.Bind<IContext>().To<WpfContext>().InSingletonScope();
                kernel.Bind<ISpotSet>().To<SpotSet>().InSingletonScope();
                kernel.Bind<ISerialStream>().To<SerialStream>().InSingletonScope();
                kernel.Bind<IDesktopDuplicatorReader>().To<DesktopDuplicatorReader>().InSingletonScope();
                kernel.Bind<Util.IModeManager>().To<Util.ModeManager>().InSingletonScope();
                kernel.Bind<Util.IAudioCaptureProvider>().To<Util.WasapiAudioCaptureProvider>().InSingletonScope();
            }

            kernel.Bind<ViewModel.DiagnosticsViewModel>()
                  .ToConstant(diagnosticsViewModel ?? new ViewModel.DiagnosticsViewModel())
                  .InSingletonScope();
            kernel.Bind<SettingsViewModel>().ToSelf().InSingletonScope();
            kernel.Bind(x => x.FromThisAssembly()
                .SelectAllClasses()
                .InheritedFrom<ISelectableViewPart>()
                .BindAllInterfaces());
            if (!isInDesignMode)
            {
                kernel.Bind(x => x.FromThisAssembly()
                    .SelectAllClasses()
                    .InheritedFrom<Util.ILightingMode>()
                    .BindAllInterfaces());
            }

            kernel.Bind<Util.INightLightRegistryReader>().To<Util.RegistryNightLightReader>().InSingletonScope();
            kernel.Bind<NightLightDetection>().ToSelf().InSingletonScope();

            var desktopDuplicationReader = kernel.Get<IDesktopDuplicatorReader>();
            var serialStream = kernel.Get<ISerialStream>();

            return kernel;
        }

        private void SetupLoggingForProcessWideEvents()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, args) => ApplicationWideException(sender, args.ExceptionObject as Exception, "CurrentDomain.UnhandledException");

            DispatcherUnhandledException += (sender, args) => ApplicationWideException(sender, args.Exception, "DispatcherUnhandledException");

            Exit += (s, e) => _log.Debug("Application exit!");

            SystemEvents.PowerModeChanged += (s, e) =>
            {
                _log.Debug("Changing Powermode to {0}", e.Mode);
                if (e.Mode == PowerModes.Suspend)
                {
                    _log.Debug("PC sleeping — pausing LEDs.");
                    _sleepWakeController?.OnSuspend();
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    _log.Debug("PC waking — restoring LED state.");
                    _sleepWakeController?.OnResume();
                    GC.Collect();
                }
            };

            SystemEvents.SessionSwitch += (s, e) =>
            {
                if (e.Reason == SessionSwitchReason.SessionLock)
                {
                    _log.Debug("Session locked — adding 'lock' inhibitor.");
                    _modeManager?.AddInhibitor("lock");
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    _log.Debug("Session unlocked — removing 'lock' inhibitor.");
                    _modeManager?.RemoveInhibitor("lock");
                }
            };
        }

        private bool _screenSaverWasActive = false;

        private void SetupScreenSaverTimer()
        {
            _screenSaverTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _screenSaverTimer.Tick += (s, e) =>
            {
                bool isRunning = false;
                SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, ref isRunning, 0);

                if (isRunning && !_screenSaverWasActive)
                {
                    _log.Debug("Screen saver started — adding 'screensaver' inhibitor.");
                    _screenSaverWasActive = true;
                    _modeManager?.AddInhibitor("screensaver");
                }
                else if (!isRunning && _screenSaverWasActive)
                {
                    _log.Debug("Screen saver stopped — removing 'screensaver' inhibitor.");
                    _screenSaverWasActive = false;
                    _modeManager?.RemoveInhibitor("screensaver");
                }
            };
            _screenSaverTimer.Start();
        }

        private void SetupLogging()
        {
            var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            var fileLayout = Layout.FromString("${longdate} ${level:uppercase=true} ${logger} ${message} ${exception:format=tostring}");
            var nightLightLayout = Layout.FromString("${assembly-version} ${longdate} ${level:uppercase=true} ${logger} ${message} ${exception:format=tostring}");

            var config = new LoggingConfiguration();

#if DEBUG
            // In Debug builds: log to the Visual Studio Output window as well
            var debuggerTarget = new DebuggerTarget("debugger")
            {
                Layout = "${processtime} ${message:exceptionSeparator=\n\t:withException=true}"
            };
            config.AddTarget(debuggerTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, debuggerTarget, "*");
#endif

            // General file target — all loggers, Info and above in Release, Debug and above in Debug
            var fileTarget = new FileTarget("file")
            {
                Layout = fileLayout,
                FileName = Path.Combine(logsDir, "adrilight.log.${shortdate}.txt"),
                ArchiveFileName = Path.Combine(logsDir, "archives", "adrilight.log.{#}.txt"),
                ArchiveEvery = FileArchivePeriod.None,
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                ArchiveAboveSize = 1048576,
                ArchiveDateFormat = "yyyyMMdd",
                MaxArchiveFiles = 10,
                ConcurrentWrites = true,
                KeepFileOpen = false,
                Encoding = System.Text.Encoding.UTF8
            };
            config.AddTarget(fileTarget);
#if DEBUG
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget, "*");
#else
            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget, "*");
#endif

            // NightLight-specific file target — all levels so low-confidence predictions are captured
            var nightLightTarget = new FileTarget("nightlight")
            {
                Layout = nightLightLayout,
                FileName = Path.Combine(logsDir, "adrilight.log.nightlight.${shortdate}.txt"),
                ArchiveFileName = Path.Combine(logsDir, "archives.nightlight", "adrilight.log.{#}.txt"),
                ArchiveEvery = FileArchivePeriod.None,
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                ArchiveAboveSize = 1048576,
                ArchiveDateFormat = "yyyyMMdd",
                MaxArchiveFiles = 10,
                ConcurrentWrites = true,
                KeepFileOpen = false,
                Encoding = System.Text.Encoding.UTF8
            };
            config.AddTarget(nightLightTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, nightLightTarget, "adrilight.Util.NightLightDetection");

            // In-memory target for the Diagnostics UI — Info+ so the filter can drill down
            var observableTarget = new Util.ObservableCollectionNLogTarget(_diagnosticsViewModel);
            config.AddTarget(observableTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, observableTarget, "*");

            LogManager.Configuration = config;
            _log.Info($"adrilight {VersionNumber} logging initialised. Log folder: {logsDir}");
        }

        private SettingsWindow _mainForm;
        private IKernel kernel;
        private NightLightDetection _nightLightDetection;
        private TcpControlServer _tcpControlServer;

        private void OpenSettingsWindow()
        {
            if (_mainForm == null)
            {
                _mainForm = new SettingsWindow();
                _mainForm.Closed += MainForm_FormClosed;
                _mainForm.Show();
            }
            else
            {
                _mainForm.Focus();
            }
        }

        private void MainForm_FormClosed(object sender, EventArgs e)
        {
            if (_mainForm == null) return;
            _mainForm.Closed -= MainForm_FormClosed;
            _mainForm = null;
        }

        private void SetupNotifyIcon()
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adrilight_icon.ico");
            var icon = new System.Drawing.Icon(iconPath);

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add(CreateSendingMenuItem());
            contextMenu.Items.Add("Settings...", null, (s, e) => OpenSettingsWindow());
            contextMenu.Items.Add("Exit", null, (s, e) => Shutdown(0));

            var notifyIcon = new System.Windows.Forms.NotifyIcon()
            {
                Text = $"adrilight {VersionNumber}",
                Icon = icon,
                Visible = true,
                ContextMenuStrip = contextMenu
            };
            notifyIcon.DoubleClick += (s, e) => OpenSettingsWindow();

            Exit += (s, e) => notifyIcon.Dispose();
        }

        private System.Windows.Forms.ToolStripMenuItem CreateSendingMenuItem()
        {
            var menuItem = new System.Windows.Forms.ToolStripMenuItem();
            menuItem.Click += (_, __) => UserSettings.TransferActive = !UserSettings.TransferActive;

            void UpdateMenuItem()
            {
                menuItem.Text = UserSettings.TransferActive ? "Sending Active" : "Sending Disabled";
                menuItem.Checked = UserSettings.TransferActive;
            }

            UpdateMenuItem();
            UserSettings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UserSettings.TransferActive))
                    UpdateMenuItem();
            };

            return menuItem;
        }

        public static bool IsPrivateBuild { get; private set; }
        public static string VersionNumber { get; private set; }

        private static void ReadVersionDetails()
        {
            if (VersionNumber == null)
            {
                VersionNumber = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
                IsPrivateBuild = VersionNumber == "0.0.0";
                if (IsPrivateBuild)
                {
#if DEBUG
                    VersionNumber = "*private debug build*";
#else
                    VersionNumber = "*private release build*";
#endif
                }
            }
        }

        private IUserSettings UserSettings { get; set; }

        private void ApplicationWideException(object sender, Exception ex, string eventSource)
        {
            _log.Fatal(ex, $"ApplicationWideException from sender={sender}, adrilight version={VersionNumber}, eventSource={eventSource}");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Sender: {sender}");
            sb.AppendLine($"Source: {eventSource}");
            if (sender != null)
                sb.AppendLine($"Sender Type: {sender.GetType().FullName}");
            sb.AppendLine("-------");
            do
            {
                sb.AppendLine($"exception type: {ex.GetType().FullName}");
                sb.AppendLine($"exception message: {ex.Message}");
                sb.AppendLine($"exception stacktrace: {ex.StackTrace}");
                sb.AppendLine("-------");
                ex = ex.InnerException;
            } while (ex != null);

            MessageBox.Show(sb.ToString(), "unhandled exception :-(");
            try { Shutdown(-1); }
            catch { Environment.Exit(-1); }
        }
    }
}
