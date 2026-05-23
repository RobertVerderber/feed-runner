Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

Module Program
    Sub Main(args As String())
        Dim baseDirectory As String = AppDomain.CurrentDomain.BaseDirectory
        Dim configPath As String = Path.Combine(baseDirectory, "appsettings.json")

        Dim bootstrapLogger As New Logger("logs")
        bootstrapLogger.Info("Feed runner application starting.")

        Dim startupOptions As StartupOptions = StartupOptions.Parse(args)
        Dim config As AppConfig = ConfigLoader.Load(configPath, bootstrapLogger)
        Dim settings As RunnerSettings = config.RunnerSettings
        Dim logger As New Logger(settings.LogFolder)

        logger.Info("Configuration loaded from: " & configPath)
        logger.Info("Enabled feeds: " & config.Feeds.Where(Function(f) f.Enabled).Count().ToString())

        Dim statusStore As New StatusStore(Path.Combine(baseDirectory, settings.StatusFilePath), logger)
        Dim feedNames As IEnumerable(Of String) = config.Feeds.Select(Function(f) f.FeedName)

        If startupOptions.ResetStatus Then
            logger.Info("Reset parameter specified. Clearing saved feed status and logs.")
            Dim deletedLogs As Integer = logger.ClearLogFolder()
            logger.Info("Deleted " & deletedLogs.ToString() & " log file(s).")
            statusStore.ResetAll(feedNames)
        Else
            statusStore.LoadAndRecoverInterrupted(feedNames)
        End If

        Dim windowMode As FeedConsoleWindowMode = FeedConsoleWindowModeHelper.ResolveMode(settings)
        logger.Info("Feed console window mode: " & windowMode.ToString())

        If settings.TestRunMode Then
            logger.Info(
                "TEST RUN MODE enabled. Feeds will be simulated for " &
                Math.Max(1, settings.TestRunDurationSeconds).ToString() &
                " second(s) without launching executables.")
        End If

        If settings.AlwaysOnTop Then
            If ConsoleWindowHelper.SetAlwaysOnTop(True) Then
                logger.Info("Feed runner console window set to always on top.")
            Else
                logger.LogError("Failed to set feed runner console window to always on top.")
            End If
        End If

        Dim processRunner As New FeedProcessRunner(
            logger,
            settings.LogFolder,
            windowMode,
            settings.KeepFeedWindowOpenOnExit,
            settings.TestRunMode,
            settings.TestRunDurationSeconds)
        Dim service As New FeedRunnerService(config, statusStore, processRunner, logger)
        Dim dashboard As New ConsoleDashboard(service, settings)

        Using cancellationSource As New CancellationTokenSource()
            AddHandler Console.CancelKeyPress,
                Sub(sender, e)
                    e.Cancel = True
                    logger.Info("Shutdown requested by user.")
                    service.RequestStop()
                    cancellationSource.Cancel()
                End Sub

            Dim serviceTask As Task = service.RunAsync(cancellationSource.Token)
            Dim dashboardTask As Task = dashboard.RunAsync(cancellationSource.Token)

            Try
                Task.WaitAll(serviceTask, dashboardTask)
            Catch ex As AggregateException
                For Each inner As Exception In ex.Flatten().InnerExceptions
                    If TypeOf inner Is TaskCanceledException Then
                        Continue For
                    End If

                    logger.LogError("Unhandled application error.", inner)
                Next
            End Try
        End Using

        logger.Info("Feed runner application stopped.")
    End Sub
End Module
