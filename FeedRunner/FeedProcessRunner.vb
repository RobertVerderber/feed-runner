Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Class FeedProcessRunner
    Private ReadOnly _logger As Logger
    Private ReadOnly _logFolder As String
    Private ReadOnly _windowMode As FeedConsoleWindowMode
    Private ReadOnly _keepWindowOpenOnExit As Boolean

    Public Sub New(
            logger As Logger,
            logFolder As String,
            windowMode As FeedConsoleWindowMode,
            keepWindowOpenOnExit As Boolean)

        _logger = logger
        _windowMode = windowMode
        _keepWindowOpenOnExit = keepWindowOpenOnExit

        If String.IsNullOrWhiteSpace(logFolder) Then
            _logFolder = "logs"
        Else
            _logFolder = logFolder
        End If
    End Sub

    Public Async Function RunFeedAsync(
            feed As FeedConfig,
            cancellationToken As CancellationToken,
            onProcessStarted As Action(Of Integer)) As Task(Of FeedExecutionResult)

        Dim startTime As DateTime = DateTime.Now
        Dim result As New FeedExecutionResult()
        result.FeedName = feed.FeedName
        result.StartTime = startTime

        If Not File.Exists(feed.ExecutablePath) Then
            result.EndTime = DateTime.Now
            result.Status = "Failed"
            result.ExitCode = -1
            result.ErrorMessage = "Executable not found: " & feed.ExecutablePath
            result.LogFilePath = String.Empty
            _logger.LogError("Feed '" & feed.FeedName & "' executable not found: " & feed.ExecutablePath)
            MaybeShowFailureWindow(feed, result, String.Empty)
            Return result
        End If

        Dim logFilePath As String = BuildLogFilePath(feed.FeedName, startTime)
        result.LogFilePath = logFilePath

        Dim process As Process = Nothing
        Dim outputBuilder As New StringBuilder()
        Dim errorBuilder As New StringBuilder()
        Dim launchedInSeparateWindow As Boolean = False

        Try
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath))

            process = New Process()
            process.EnableRaisingEvents = True

            If _windowMode = FeedConsoleWindowMode.Always Then
                ConfigureVisibleWindowLaunch(process, feed, outputBuilder)
                launchedInSeparateWindow = True
            Else
                ConfigureHiddenLaunch(process, feed, outputBuilder, errorBuilder)
            End If

            process.Start()

            Dim reportedProcessId As Integer = process.Id
            If launchedInSeparateWindow Then
                Await Task.Delay(750, cancellationToken).ConfigureAwait(False)
                Dim feedProcessId As Integer = TryFindFeedProcessId(feed.ExecutablePath)
                If feedProcessId > 0 Then
                    reportedProcessId = feedProcessId
                End If
            End If

            If onProcessStarted IsNot Nothing Then
                onProcessStarted(reportedProcessId)
            End If

            If Not launchedInSeparateWindow Then
                process.BeginOutputReadLine()
                process.BeginErrorReadLine()
            End If

            Dim timeoutMs As Integer = Math.Max(1, feed.TimeoutMinutes) * 60 * 1000
            Dim completed As Boolean = Await WaitForExitAsync(process, timeoutMs, cancellationToken).ConfigureAwait(False)

            If Not completed Then
                Try
                    If Not process.HasExited Then
                        process.Kill()
                        process.WaitForExit(5000)
                    End If

                    If launchedInSeparateWindow Then
                        KillMatchingFeedProcesses(feed.ExecutablePath)
                    End If
                Catch killEx As Exception
                    _logger.LogError("Failed to kill timed out process for feed '" & feed.FeedName & "'.", killEx)
                End Try

                result.EndTime = DateTime.Now
                result.Status = "Timeout"
                result.ExitCode = -2
                result.ErrorMessage = "Process exceeded timeout of " & feed.TimeoutMinutes.ToString() & " minutes."
                WriteFeedLog(logFilePath, feed, result, outputBuilder.ToString(), errorBuilder.ToString(), reportedProcessId, launchedInSeparateWindow)
                _logger.LogError("Feed '" & feed.FeedName & "' timed out after " & feed.TimeoutMinutes.ToString() & " minutes.")
                MaybeShowFailureWindow(feed, result, logFilePath)
                Return result
            End If

            result.EndTime = DateTime.Now
            result.ExitCode = process.ExitCode

            If process.ExitCode = 0 Then
                result.Status = "Success"
                result.ErrorMessage = String.Empty
            Else
                result.Status = "Failed"
                result.ErrorMessage = "Process exited with code " & process.ExitCode.ToString() & "."
            End If

            WriteFeedLog(logFilePath, feed, result, outputBuilder.ToString(), errorBuilder.ToString(), reportedProcessId, launchedInSeparateWindow)
            MaybeShowFailureWindow(feed, result, logFilePath)
            Return result
        Catch ex As Exception
            result.EndTime = DateTime.Now
            result.Status = "Exception"
            result.ExitCode = -3
            result.ErrorMessage = ex.Message

            Dim processId As Integer = 0
            If process IsNot Nothing Then
                processId = process.Id
            End If

            WriteFeedLog(logFilePath, feed, result, outputBuilder.ToString(), errorBuilder.ToString(), processId, launchedInSeparateWindow)
            _logger.LogError("Exception running feed '" & feed.FeedName & "'.", ex)
            MaybeShowFailureWindow(feed, result, logFilePath)
            Return result
        Finally
            If process IsNot Nothing Then
                process.Dispose()
            End If
        End Try
    End Function

    Private Sub ConfigureVisibleWindowLaunch(process As Process, feed As FeedConfig, outputBuilder As StringBuilder)
        Dim quote As String = Chr(34).ToString()
        Dim workingDirectory As String = ResolveWorkingDirectory(feed)
        Dim executablePath As String = feed.ExecutablePath
        Dim feedArguments As String = If(feed.Arguments, String.Empty).Trim()
        Dim windowTitle As String = BuildExecutableWindowTitle(feed)

        Dim feedCommand As New StringBuilder()
        feedCommand.Append("title ")
        feedCommand.Append(windowTitle)
        feedCommand.Append(" && cd /d ")
        feedCommand.Append(quote)
        feedCommand.Append(workingDirectory)
        feedCommand.Append(quote)
        feedCommand.Append(" && ")
        feedCommand.Append(quote)
        feedCommand.Append(executablePath)
        feedCommand.Append(quote)

        If feedArguments.Length > 0 Then
            feedCommand.Append(" ")
            feedCommand.Append(feedArguments)
        End If

        If _keepWindowOpenOnExit Then
            feedCommand.Append(" || (echo. & echo Feed exited with an error. & pause)")
        End If

        Dim launchArguments As String = String.Format(
            "/c start {0}{1}{0} /wait cmd /c {0}{2}{0}",
            quote,
            windowTitle,
            feedCommand.ToString())

        process.StartInfo.FileName = "cmd.exe"
        process.StartInfo.Arguments = launchArguments
        process.StartInfo.WorkingDirectory = workingDirectory
        process.StartInfo.UseShellExecute = False
        process.StartInfo.CreateNoWindow = True
        process.StartInfo.RedirectStandardOutput = False
        process.StartInfo.RedirectStandardError = False
        outputBuilder.AppendLine("(Output displayed in a separate feed console window.)")
    End Sub

    Private Sub ConfigureHiddenLaunch(process As Process, feed As FeedConfig, outputBuilder As StringBuilder, errorBuilder As StringBuilder)
        process.StartInfo.FileName = feed.ExecutablePath
        process.StartInfo.Arguments = If(feed.Arguments, String.Empty)
        process.StartInfo.WorkingDirectory = ResolveWorkingDirectory(feed)
        process.StartInfo.UseShellExecute = False
        process.StartInfo.CreateNoWindow = True
        process.StartInfo.RedirectStandardOutput = True
        process.StartInfo.RedirectStandardError = True

        AddHandler process.OutputDataReceived,
            Sub(sender, e)
                If e IsNot Nothing AndAlso e.Data IsNot Nothing Then
                    SyncLock outputBuilder
                        outputBuilder.AppendLine(e.Data)
                    End SyncLock
                End If
            End Sub

        AddHandler process.ErrorDataReceived,
            Sub(sender, e)
                If e IsNot Nothing AndAlso e.Data IsNot Nothing Then
                    SyncLock errorBuilder
                        errorBuilder.AppendLine(e.Data)
                    End SyncLock
                End If
            End Sub
    End Sub

    Private Shared Function TryFindFeedProcessId(executablePath As String) As Integer
        Dim processName As String = Path.GetFileNameWithoutExtension(executablePath)
        If String.IsNullOrWhiteSpace(processName) Then
            Return 0
        End If

        Dim processes As Process() = Process.GetProcessesByName(processName)
        For Each candidate As Process In processes
            Try
                If candidate Is Nothing OrElse candidate.HasExited Then
                    Continue For
                End If

                Dim modulePath As String = candidate.MainModule.FileName
                If String.Equals(modulePath, executablePath, StringComparison.OrdinalIgnoreCase) Then
                    Return candidate.Id
                End If
            Catch
            Finally
                If candidate IsNot Nothing Then
                    candidate.Dispose()
                End If
            End Try
        Next

        Return 0
    End Function

    Private Shared Sub KillMatchingFeedProcesses(executablePath As String)
        Dim processName As String = Path.GetFileNameWithoutExtension(executablePath)
        If String.IsNullOrWhiteSpace(processName) Then
            Return
        End If

        Dim processes As Process() = Process.GetProcessesByName(processName)
        For Each candidate As Process In processes
            Try
                If candidate Is Nothing OrElse candidate.HasExited Then
                    Continue For
                End If

                Dim modulePath As String = candidate.MainModule.FileName
                If String.Equals(modulePath, executablePath, StringComparison.OrdinalIgnoreCase) Then
                    candidate.Kill()
                    candidate.WaitForExit(5000)
                End If
            Catch
            Finally
                If candidate IsNot Nothing Then
                    candidate.Dispose()
                End If
            End Try
        Next
    End Sub

    Private Shared Async Function WaitForExitAsync(process As Process, timeoutMs As Integer, cancellationToken As CancellationToken) As Task(Of Boolean)
        Dim tcs As New TaskCompletionSource(Of Boolean)()

        AddHandler process.Exited,
            Sub(sender, e)
                tcs.TrySetResult(True)
            End Sub

        If process.HasExited Then
            Return True
        End If

        Using timeoutCts As New CancellationTokenSource(timeoutMs)
            Using linkedCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken)
                Dim registration As CancellationTokenRegistration = linkedCts.Token.Register(
                    Sub()
                        tcs.TrySetResult(False)
                    End Sub)

                Try
                    Return Await tcs.Task.ConfigureAwait(False)
                Finally
                    registration.Dispose()
                End Try
            End Using
        End Using
    End Function

    Private Function ResolveWorkingDirectory(feed As FeedConfig) As String
        If Not String.IsNullOrWhiteSpace(feed.WorkingDirectory) AndAlso Directory.Exists(feed.WorkingDirectory) Then
            Return feed.WorkingDirectory
        End If

        Dim executableDirectory As String = Path.GetDirectoryName(feed.ExecutablePath)
        If Not String.IsNullOrWhiteSpace(executableDirectory) AndAlso Directory.Exists(executableDirectory) Then
            Return executableDirectory
        End If

        Return Environment.CurrentDirectory
    End Function

    Private Function BuildLogFilePath(feedName As String, startTime As DateTime) As String
        Dim safeFeedName As String = SanitizeFileName(feedName)
        Dim dayFolder As String = startTime.ToString("yyyy-MM-dd")
        Dim fileName As String = String.Format("{0}-{1:yyyyMMdd-HHmmss}.log", safeFeedName, startTime)
        Return Path.Combine(_logFolder, dayFolder, fileName)
    End Function

    Private Shared Function SanitizeFileName(value As String) As String
        Dim invalidChars As Char() = Path.GetInvalidFileNameChars()
        Dim builder As New StringBuilder()

        For Each ch As Char In value
            If Array.IndexOf(invalidChars, ch) >= 0 Then
                builder.Append("_"c)
            Else
                builder.Append(ch)
            End If
        Next

        Dim sanitized As String = builder.ToString().Trim()
        If String.IsNullOrWhiteSpace(sanitized) Then
            Return "Feed"
        End If

        Return sanitized
    End Function

    Private Sub WriteFeedLog(
            logFilePath As String,
            feed As FeedConfig,
            result As FeedExecutionResult,
            standardOutput As String,
            standardError As String,
            processId As Integer,
            separateWindow As Boolean)

        Try
            Dim builder As New StringBuilder()
            builder.AppendLine("Feed: " & feed.FeedName)
            builder.AppendLine("MLS Key: " & feed.MlsKey)
            builder.AppendLine("Executable: " & feed.ExecutablePath)
            builder.AppendLine("Arguments: " & If(feed.Arguments, String.Empty))
            builder.AppendLine("Working Directory: " & ResolveWorkingDirectory(feed))
            builder.AppendLine("Process ID: " & If(processId > 0, processId.ToString(), "N/A"))
            builder.AppendLine("Feed Console Window Mode: " & _windowMode.ToString())
            builder.AppendLine("Separate Console Window: " & separateWindow.ToString())
            builder.AppendLine("Keep Window Open On Error: " & _keepWindowOpenOnExit.ToString())
            builder.AppendLine("Start: " & result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"))
            builder.AppendLine("End: " & result.EndTime.ToString("yyyy-MM-dd HH:mm:ss"))
            builder.AppendLine("Duration: " & result.Duration.ToString())
            builder.AppendLine("Status: " & result.Status)
            builder.AppendLine("Exit Code: " & If(result.ExitCode.HasValue, result.ExitCode.Value.ToString(), "N/A"))

            If Not String.IsNullOrWhiteSpace(result.ErrorMessage) Then
                builder.AppendLine("Error: " & result.ErrorMessage)
            End If

            builder.AppendLine()
            builder.AppendLine("----- STDOUT -----")
            builder.AppendLine(If(standardOutput, String.Empty))
            builder.AppendLine()
            builder.AppendLine("----- STDERR -----")
            builder.AppendLine(If(standardError, String.Empty))

            File.WriteAllText(logFilePath, builder.ToString(), Encoding.UTF8)
        Catch ex As Exception
            _logger.LogError("Failed to write feed log for '" & feed.FeedName & "'.", ex)
        End Try
    End Sub

    Private Sub MaybeShowFailureWindow(feed As FeedConfig, result As FeedExecutionResult, logFilePath As String)
        If result.Succeeded Then
            Return
        End If

        If _windowMode = FeedConsoleWindowMode.Always Then
            Return
        End If

        If _windowMode <> FeedConsoleWindowMode.OnFailure Then
            Return
        End If

        ShowFailureConsoleWindow(feed, result, logFilePath)
    End Sub

    Private Sub ShowFailureConsoleWindow(feed As FeedConfig, result As FeedExecutionResult, logFilePath As String)
        Try
            Dim quote As String = Chr(34).ToString()
            Dim executableTitle As String = BuildExecutableWindowTitle(feed)
            Dim windowTitle As String = "FAILED - " & executableTitle

            Dim windowCommand As New StringBuilder()
            windowCommand.Append("title ")
            windowCommand.Append(windowTitle)
            windowCommand.Append(" & echo Feed failed: ")
            windowCommand.Append(feed.FeedName)
            windowCommand.Append(" (")
            windowCommand.Append(executableTitle)
            windowCommand.Append(")")
            windowCommand.Append(" & echo Status: ")
            windowCommand.Append(result.Status)
            windowCommand.Append(" & echo.")

            If Not String.IsNullOrWhiteSpace(result.ErrorMessage) Then
                windowCommand.Append("echo Error: ")
                windowCommand.Append(result.ErrorMessage)
                windowCommand.Append(" & echo.")
            End If

            If Not String.IsNullOrWhiteSpace(logFilePath) AndAlso File.Exists(logFilePath) Then
                windowCommand.Append("echo Log file: ")
                windowCommand.Append(logFilePath)
                windowCommand.Append(" & echo.")
                windowCommand.Append("echo ----- Feed Log -----")
                windowCommand.Append(" & type ")
                windowCommand.Append(quote)
                windowCommand.Append(logFilePath)
                windowCommand.Append(quote)
                windowCommand.Append(" & echo.")
            End If

            windowCommand.Append("echo Press any key to close this window...")
            windowCommand.Append(" & pause")

            Dim launchArguments As String = String.Format(
                "/c start {0}{1}{0} cmd /k {0}{2}{0}",
                quote,
                windowTitle,
                windowCommand.ToString())

            Dim failureWindow As New Process()
            failureWindow.StartInfo.FileName = "cmd.exe"
            failureWindow.StartInfo.Arguments = launchArguments
            failureWindow.StartInfo.UseShellExecute = False
            failureWindow.StartInfo.CreateNoWindow = True
            failureWindow.Start()
        Catch ex As Exception
            _logger.LogError("Failed to open failure console window for '" & feed.FeedName & "'.", ex)
        End Try
    End Sub

    Private Shared Function BuildExecutableWindowTitle(feed As FeedConfig) As String
        Dim title As String = String.Empty

        If feed IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(feed.ExecutablePath) Then
            title = Path.GetFileName(feed.ExecutablePath)
        End If

        If String.IsNullOrWhiteSpace(title) AndAlso feed IsNot Nothing Then
            title = feed.FeedName
        End If

        If String.IsNullOrWhiteSpace(title) Then
            title = "Feed"
        End If

        Return title.Replace(Chr(34), "'")
    End Function
End Class
