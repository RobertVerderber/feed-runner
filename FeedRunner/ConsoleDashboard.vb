Imports Spectre.Console
Imports Spectre.Console.Rendering
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Class ConsoleDashboard
    Private ReadOnly _service As FeedRunnerService
    Private ReadOnly _refreshSeconds As Integer
    Private ReadOnly _queueReadyOnly As Boolean

    Public Sub New(service As FeedRunnerService, settings As RunnerSettings)
        _service = service
        _refreshSeconds = Math.Max(1, settings.RefreshSeconds)
        _queueReadyOnly = settings.DashboardQueueReadyOnly
    End Sub

    Public Async Function RunAsync(cancellationToken As CancellationToken) As Task
        Dim liveTable As New Table()
        liveTable.AddColumn("Initializing")

        Dim liveDisplay As LiveDisplay = AnsiConsole.Live(liveTable)
        liveDisplay.AutoClear = False
        liveDisplay.Overflow = VerticalOverflow.Visible

        Await liveDisplay.StartAsync(
                Async Function(ctx As LiveDisplayContext) As Task
                    While Not cancellationToken.IsCancellationRequested
                        Dim snapshot As DashboardSnapshot = _service.GetSnapshot()
                        ctx.UpdateTarget(BuildLayout(snapshot))

                        Try
                            Await Task.Delay(_refreshSeconds * 1000, cancellationToken).ConfigureAwait(False)
                        Catch ex As TaskCanceledException
                            Exit While
                        End Try
                    End While
                End Function).ConfigureAwait(False)
    End Function

    Private Function BuildLayout(snapshot As DashboardSnapshot) As IRenderable
        Dim consoleHeight As Integer = GetConsoleHeight()
        Dim headerSize As Integer = GetHeaderSize()
        Dim runningSize As Integer = GetRunningSectionHeight(snapshot)
        Dim bottomSize As Integer = Math.Max(8, consoleHeight - headerSize - runningSize)
        Dim rowsPerBottomPanel As Integer = RowsForLayoutHeight(bottomSize)

        Dim headerLayout As New Layout(BuildHeaderPanel(snapshot))
        headerLayout.Size = headerSize

        Dim runningLayout As New Layout(BuildRunningPanel(snapshot))
        runningLayout.Size = runningSize

        Dim queuedLayout As New Layout(BuildQueuedPanel(snapshot, rowsPerBottomPanel))
        Dim completedLayout As New Layout(BuildCompletedPanel(snapshot, rowsPerBottomPanel))

        Dim bottomLayout As New Layout("Bottom")
        bottomLayout.Size = bottomSize
        bottomLayout.SplitColumns(queuedLayout, completedLayout)

        Dim layout As New Layout("Root")
        layout.SplitRows(headerLayout, runningLayout, bottomLayout)
        Return layout
    End Function

    Private Function GetHeaderSize() As Integer
        Return 7
    End Function

    Private Shared Function GetConsoleHeight() As Integer
        Try
            Return Math.Max(30, Console.WindowHeight)
        Catch
            Return 40
        End Try
    End Function

    Private Shared Function GetConsoleWidth() As Integer
        Try
            Return Math.Max(100, Console.WindowWidth)
        Catch
            Return 120
        End Try
    End Function

    Private Function GetRunningSectionHeight(snapshot As DashboardSnapshot) As Integer
        Dim dataRowCount As Integer = Math.Max(1, snapshot.RunningFeeds.Count)
        Return Math.Max(7, dataRowCount + 5)
    End Function

    Private Shared Function RowsForLayoutHeight(layoutHeight As Integer) As Integer
        Return Math.Max(1, layoutHeight - 4)
    End Function

    Private Function BuildHeaderPanel(snapshot As DashboardSnapshot) As Panel
        Dim readyCount As Integer = CountReadyFeeds(snapshot)
        Dim waitingCount As Integer = CountWaitingFeeds(snapshot)
        Dim builder As New StringBuilder()

        builder.Append("[dim]")
        builder.Append(snapshot.CurrentTime.ToString("yyyy-MM-dd HH:mm:ss"))
        builder.Append("[/]  ")
        builder.Append("[bold]")
        builder.Append(snapshot.TotalEnabledFeeds.ToString())
        builder.Append("[/] enabled")
        builder.Append("  ·  ")
        builder.Append(snapshot.TotalDisabledFeeds.ToString())
        builder.Append(" disabled")
        builder.Append("  ·  ")
        builder.Append(snapshot.TotalFeeds.ToString())
        builder.Append(" total")
        builder.AppendLine()
        builder.AppendLine()

        builder.Append("[yellow]●[/] Running [bold yellow]")
        builder.Append(snapshot.RunningCount.ToString())
        builder.Append("[/]    ")
        builder.Append("[green]●[/] Ready [bold green]")
        builder.Append(readyCount.ToString())
        builder.Append("[/]    ")
        builder.Append("[deepskyblue1]●[/] Waiting [bold deepskyblue1]")
        builder.Append(waitingCount.ToString())
        builder.Append("[/]    ")
        builder.Append("[grey]●[/] Done [bold]")
        builder.Append(snapshot.CompletedTodayCount.ToString())
        builder.Append("[/]    ")
        builder.Append("[red]●[/] Failed [bold red]")
        builder.Append(snapshot.FailedTodayCount.ToString())
        builder.Append("[/]")

        If snapshot.BlockedByMlsCount > 0 Then
            builder.Append("    ")
            builder.Append("[orange1]●[/] MLS blocked [bold orange1]")
            builder.Append(snapshot.BlockedByMlsCount.ToString())
            builder.Append("[/]")
        End If

        builder.AppendLine()
        builder.AppendLine()
        builder.Append(BuildBatchRunLine(snapshot))

        Dim panel As New Panel(New Markup(builder.ToString()))
        panel.Header = New PanelHeader(BuildHeaderTitle(snapshot))
        panel.Border = BoxBorder.Rounded
        panel.BorderStyle = New Style(Color.Grey23)
        panel.Expand = True
        Return panel
    End Function

    Private Shared Function BuildHeaderTitle(snapshot As DashboardSnapshot) As String
        If snapshot.TestRunMode Then
            Return "[bold white]Feed Runner[/]  [black on yellow] TEST RUN [/]  [dim](" & snapshot.TestRunDurationSeconds.ToString() & "s simulation)[/]"
        End If

        Return "[bold white]Feed Runner[/]"
    End Function

    Private Shared Function BuildBatchRunLine(snapshot As DashboardSnapshot) As String
        Dim builder As New StringBuilder()

        If snapshot.LastBatchRunDuration.HasValue AndAlso snapshot.LastBatchRunEndTime.HasValue Then
            builder.Append("[dim]Last full run:[/] [bold]")
            builder.Append(FormatDuration(snapshot.LastBatchRunDuration.Value))
            builder.Append("[/]")
            builder.Append("  ·  ")
            builder.Append(snapshot.LastBatchRunFeedCount.ToString())
            builder.Append(" feeds  ·  finished ")
            builder.Append(snapshot.LastBatchRunEndTime.Value.ToString("HH:mm:ss"))
        Else
            builder.Append("[dim]Last full run:[/] [dim italic]none yet[/]")
        End If

        If snapshot.CurrentBatchIsActive AndAlso snapshot.CurrentBatchElapsed.HasValue Then
            builder.Append("    ")
            builder.Append("[cyan]Current run:[/] [bold cyan]")
            builder.Append(FormatDuration(snapshot.CurrentBatchElapsed.Value))
            builder.Append("[/]")
            builder.Append("  ·  ")
            builder.Append(snapshot.CurrentBatchCompletedCount.ToString())
            builder.Append("/")
            builder.Append(snapshot.CurrentBatchFeedCount.ToString())
            builder.Append(" feeds")
        End If

        Return builder.ToString()
    End Function

    Private Function BuildRunningPanel(snapshot As DashboardSnapshot) As Panel
        Return WrapSection("[yellow bold]Running[/]", BuildRunningTable(snapshot), Color.Yellow)
    End Function

    Private Function BuildQueuedPanel(snapshot As DashboardSnapshot, maxRows As Integer) As Panel
        Dim queueRows As List(Of QueuedFeedRow) = GetQueueRows(snapshot)
        Dim title As String

        If _queueReadyOnly Then
            title = "[deepskyblue1 bold]Ready Queue[/] [dim](" & queueRows.Count.ToString() & " ready)[/]"
        Else
            title = "[deepskyblue1 bold]Queue[/] [dim](" & snapshot.QueuedFeeds.Count.ToString() & ")[/]"
        End If

        Return WrapSection(title, BuildQueuedTable(snapshot, queueRows, maxRows), Color.DeepSkyBlue1)
    End Function

    Private Function BuildCompletedPanel(snapshot As DashboardSnapshot, maxRows As Integer) As Panel
        Return WrapSection("[green bold]Recent Completed[/]", BuildCompletedTable(snapshot, maxRows), Color.Green)
    End Function

    Private Shared Function WrapSection(title As String, content As IRenderable, accentColor As Color) As Panel
        Dim panel As New Panel(content)
        panel.Header = New PanelHeader(title)
        panel.Border = BoxBorder.Rounded
        panel.BorderStyle = New Style(accentColor)
        panel.Expand = True
        Return panel
    End Function

    Private Class RunningColumnWidths
        Public Property Feed As Integer
        Public Property Pid As Integer
        Public Property Started As Integer
        Public Property Elapsed As Integer
        Public Property Executable As Integer
    End Class

    Private Class SplitPanelColumnWidths
        Public Property Feed As Integer
        Public Property NextRun As Integer
        Public Property Priority As Integer
        Public Property Status As Integer
        Public Property Finished As Integer
        Public Property Result As Integer
        Public Property Code As Integer
        Public Property Duration As Integer
    End Class

    Private Shared Sub ConfigureRunningTable(table As Table)
        table.Border = TableBorder.None
        table.Expand = False
        table.ShowHeaders = True
    End Sub

    Private Shared Sub ConfigureDataTable(table As Table)
        table.Border = TableBorder.None
        table.Expand = True
        table.ShowHeaders = True
    End Sub

    Private Shared Sub AddFixedColumn(
            table As Table,
            header As String,
            width As Integer,
            alignment As Justify,
            Optional columnPadding As Padding = Nothing)

        Dim column As New TableColumn("[dim]" & header & "[/]")
        column.Width = Math.Max(3, width)
        column.NoWrap = True

        If columnPadding = Nothing Then
            column.Padding = New Padding(0, 1)
        Else
            column.Padding = columnPadding
        End If

        Select Case alignment
            Case Justify.Center
                column.Centered()
            Case Justify.Right
                column.RightAligned()
            Case Else
                column.LeftAligned()
        End Select

        table.AddColumn(column)
    End Sub

    Private Shared Function GetQueueFeedWidth() As Integer
        Dim panelWidth As Integer = Math.Max(40, (GetConsoleWidth() \ 2) - 4)
        Dim queueRemainingMin As Integer = 6 + 4 + 18
        Dim completedRemainingMin As Integer = 6 + 14 + 6 + 8

        Return Math.Max(10, Math.Min(panelWidth - queueRemainingMin, panelWidth - completedRemainingMin))
    End Function

    Private Shared Function GetQueuePanelWidths() As SplitPanelColumnWidths
        Dim panelWidth As Integer = Math.Max(40, (GetConsoleWidth() \ 2) - 4)
        Const nextRunWidth As Integer = 6
        Const priorityWidth As Integer = 4
        Const minStatusWidth As Integer = 16

        Dim feedWidth As Integer = GetQueueFeedWidth()
        Dim statusWidth As Integer = panelWidth - feedWidth - nextRunWidth - priorityWidth

        If statusWidth < minStatusWidth Then
            feedWidth = Math.Max(10, panelWidth - nextRunWidth - priorityWidth - minStatusWidth)
            statusWidth = panelWidth - feedWidth - nextRunWidth - priorityWidth
        End If

        Dim widths As New SplitPanelColumnWidths()
        widths.Feed = feedWidth
        widths.NextRun = nextRunWidth
        widths.Priority = priorityWidth
        widths.Status = statusWidth
        Return widths
    End Function

    Private Shared Function GetCompletedPanelWidths() As SplitPanelColumnWidths
        Dim panelWidth As Integer = Math.Max(40, (GetConsoleWidth() \ 2) - 4)
        Const finishedWidth As Integer = 6
        Const codeWidth As Integer = 7
        Const durationWidth As Integer = 9
        Const minResultWidth As Integer = 12

        Dim feedWidth As Integer = GetQueueFeedWidth()
        Dim resultWidth As Integer = panelWidth - feedWidth - finishedWidth - codeWidth - durationWidth

        If resultWidth < minResultWidth Then
            feedWidth = Math.Max(10, panelWidth - finishedWidth - codeWidth - durationWidth - minResultWidth)
            resultWidth = panelWidth - feedWidth - finishedWidth - codeWidth - durationWidth
        End If

        Dim widths As New SplitPanelColumnWidths()
        widths.Feed = feedWidth
        widths.Finished = finishedWidth
        widths.Result = resultWidth
        widths.Code = codeWidth
        widths.Duration = durationWidth
        Return widths
    End Function

    Private Shared Sub ConfigureSplitPanelTable(table As Table)
        table.Border = TableBorder.None
        table.Expand = False
        table.ShowHeaders = True
    End Sub

    Private Shared Function GetRunningColumnWidths() As RunningColumnWidths
        Dim availableWidth As Integer = Math.Max(80, GetConsoleWidth() - 6)
        Dim feedWidth As Integer = GetQueueFeedWidth()
        Dim remainingWidth As Integer = Math.Max(40, availableWidth - feedWidth)

        Dim pidWidth As Integer = Math.Max(10, CInt(remainingWidth * 0.14))
        Dim startedWidth As Integer = Math.Max(6, CInt(remainingWidth * 0.1))
        Dim elapsedWidth As Integer = Math.Max(10, CInt(remainingWidth * 0.14))
        Dim executableWidth As Integer = remainingWidth - pidWidth - startedWidth - elapsedWidth

        If executableWidth < 24 Then
            executableWidth = 24
            Dim overflow As Integer = (pidWidth + startedWidth + elapsedWidth + executableWidth) - remainingWidth
            If overflow > 0 Then
                pidWidth = Math.Max(10, pidWidth - CInt(overflow * 0.3))
                elapsedWidth = Math.Max(10, elapsedWidth - CInt(overflow * 0.3))
                startedWidth = Math.Max(6, startedWidth - CInt(overflow * 0.2))
                executableWidth = remainingWidth - pidWidth - startedWidth - elapsedWidth
            End If
        End If

        Dim widths As New RunningColumnWidths()
        widths.Feed = feedWidth
        widths.Pid = pidWidth
        widths.Started = startedWidth
        widths.Elapsed = elapsedWidth
        widths.Executable = executableWidth
        Return widths
    End Function

    Private Function BuildRunningTable(snapshot As DashboardSnapshot) As Table
        Dim table As New Table()
        ConfigureRunningTable(table)

        Dim widths As RunningColumnWidths = GetRunningColumnWidths()

        Dim runningPadding As New Padding(1, 2)

        AddFixedColumn(table, "Feed", widths.Feed, Justify.Left, runningPadding)
        AddFixedColumn(table, "PID", widths.Pid, Justify.Right, runningPadding)
        AddFixedColumn(table, "Start", widths.Started, Justify.Center, runningPadding)
        AddFixedColumn(table, "Elapsed", widths.Elapsed, Justify.Right, runningPadding)
        AddFixedColumn(table, "Executable", widths.Executable, Justify.Left, runningPadding)

        If snapshot.RunningFeeds.Count = 0 Then
            table.AddRow(
                "[dim italic]No feeds running[/]",
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty)
        Else
            Dim sortedRunningFeeds As List(Of RunningFeedRow) =
                snapshot.RunningFeeds.OrderBy(Function(row) row.FeedName).ToList()

            For Each row As RunningFeedRow In sortedRunningFeeds
                Dim elapsed As TimeSpan = snapshot.CurrentTime - row.StartTime
                Dim processIdText As String = If(row.ProcessId > 0, row.ProcessId.ToString(), "-")

                table.AddRow(
                    "[yellow]" & EscapeMarkup(TruncateText(row.FeedName, widths.Feed)) & "[/]",
                    processIdText,
                    row.StartTime.ToString("HH:mm"),
                    FormatDuration(elapsed),
                    "[dim]" & EscapeMarkup(TruncateText(row.ExecutablePath, widths.Executable)) & "[/]")
            Next
        End If

        Return table
    End Function

    Private Function GetQueueRows(snapshot As DashboardSnapshot) As List(Of QueuedFeedRow)
        If Not _queueReadyOnly Then
            Return snapshot.QueuedFeeds
        End If

        Return snapshot.QueuedFeeds.
            Where(Function(row) String.IsNullOrWhiteSpace(row.Reason)).
            ToList()
    End Function

    Private Function BuildQueuedTable(
            snapshot As DashboardSnapshot,
            queueRows As List(Of QueuedFeedRow),
            maxRows As Integer) As Table

        Dim table As New Table()
        ConfigureSplitPanelTable(table)

        Dim widths As SplitPanelColumnWidths = GetQueuePanelWidths()
        Dim panelPadding As New Padding(0, 1)

        AddFixedColumn(table, "Feed", widths.Feed, Justify.Left, panelPadding)
        AddFixedColumn(table, "Next", widths.NextRun, Justify.Center, panelPadding)
        AddFixedColumn(table, "Pri", widths.Priority, Justify.Center, panelPadding)
        AddFixedColumn(table, "Status", widths.Status, Justify.Left, panelPadding)

        Dim rowsToShow As Integer = Math.Min(maxRows, queueRows.Count)
        If rowsToShow = 0 Then
            Dim emptyMessage As String = If(
                _queueReadyOnly,
                "[dim italic]No ready feeds[/]",
                "[dim italic]Queue empty[/]")
            table.AddRow(emptyMessage, String.Empty, String.Empty, String.Empty)
        Else
            For index As Integer = 0 To rowsToShow - 1
                Dim row As QueuedFeedRow = queueRows(index)
                Dim reasonText As String = If(String.IsNullOrWhiteSpace(row.Reason), "Ready", row.Reason)
                Dim feedMarkup As String = FormatQueueFeedName(row.FeedName, reasonText, widths.Feed)

                table.AddRow(
                    feedMarkup,
                    FormatDateTime(row.NextEligibleRun),
                    row.Priority.ToString(),
                    FormatQueueStatusText(reasonText, widths.Status))
            Next

            If queueRows.Count > rowsToShow Then
                table.AddRow(
                    "[dim italic]... +" & (queueRows.Count - rowsToShow).ToString() & " more[/]",
                    String.Empty,
                    String.Empty,
                    String.Empty)
            End If
        End If

        Return table
    End Function

    Private Shared Function FormatQueueFeedName(feedName As String, reason As String, feedWidth As Integer) As String
        Dim truncatedName As String = EscapeMarkup(TruncateText(feedName, feedWidth))

        If String.Equals(reason, "Ready", StringComparison.OrdinalIgnoreCase) Then
            Return "[green bold]" & truncatedName & "[/]"
        End If

        Return truncatedName
    End Function

    Private Shared Function BuildCompletedTable(snapshot As DashboardSnapshot, maxRows As Integer) As Table
        Dim table As New Table()
        ConfigureSplitPanelTable(table)

        Dim widths As SplitPanelColumnWidths = GetCompletedPanelWidths()
        Dim panelPadding As New Padding(0, 1)

        AddFixedColumn(table, "Feed", widths.Feed, Justify.Left, panelPadding)
        AddFixedColumn(table, "Done", widths.Finished, Justify.Center, panelPadding)
        AddFixedColumn(table, "Result", widths.Result, Justify.Left, panelPadding)
        AddFixedColumn(table, "Code", widths.Code, Justify.Center, panelPadding)
        AddFixedColumn(table, "Duration", widths.Duration, Justify.Right, panelPadding)

        Dim rowsToShow As Integer = Math.Min(maxRows, snapshot.RecentCompletedFeeds.Count)
        If rowsToShow = 0 Then
            table.AddRow(
                "[dim italic]No completions yet[/]",
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty)
        Else
            For index As Integer = 0 To rowsToShow - 1
                Dim row As CompletedFeedRow = snapshot.RecentCompletedFeeds(index)
                Dim exitCodeText As String = If(row.ExitCode.HasValue, row.ExitCode.Value.ToString(), "-")

                table.AddRow(
                    EscapeMarkup(TruncateText(row.FeedName, widths.Feed)),
                    row.EndTime.ToString("HH:mm"),
                    FormatCompletionStatusText(row.Status, widths.Result),
                    FormatExitCodeText(exitCodeText, row.Status, widths.Code),
                    FormatDuration(row.Duration))
            Next
        End If

        Return table
    End Function

    Private Shared Function FormatQueueStatusText(reason As String, maxWidth As Integer) As String
        If String.Equals(reason, "Ready", StringComparison.OrdinalIgnoreCase) Then
            Return "[green]" & TruncateText("Ready", maxWidth) & "[/]"
        End If

        If String.Equals(reason, "MLS in use", StringComparison.OrdinalIgnoreCase) Then
            Return "[orange1]" & EscapeMarkup(TruncateText(reason, maxWidth)) & "[/]"
        End If

        If String.Equals(reason, "Max concurrent reached", StringComparison.OrdinalIgnoreCase) Then
            Return "[deepskyblue1]" & EscapeMarkup(TruncateText(reason, maxWidth)) & "[/]"
        End If

        If String.Equals(reason, "Disabled", StringComparison.OrdinalIgnoreCase) Then
            Return "[dim]" & EscapeMarkup(TruncateText(reason, maxWidth)) & "[/]"
        End If

        If reason.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase) Then
            Return "[dim]" & EscapeMarkup(TruncateText(reason, maxWidth)) & "[/]"
        End If

        Return "[grey]" & EscapeMarkup(TruncateText(reason, maxWidth)) & "[/]"
    End Function

    Private Shared Function FormatCompletionStatusText(status As String, maxWidth As Integer) As String
        If String.IsNullOrWhiteSpace(status) Then
            Return "[dim]-[/]"
        End If

        If String.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) Then
            Return "[green bold]" & TruncateText("Success", maxWidth) & "[/]"
        End If

        Return "[red bold]" & EscapeMarkup(TruncateText(status, maxWidth)) & "[/]"
    End Function

    Private Shared Function FormatExitCodeText(exitCodeText As String, status As String, maxWidth As Integer) As String
        Dim displayText As String = TruncateText(exitCodeText, maxWidth)

        If String.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) Then
            Return "[green]" & displayText & "[/]"
        End If

        If exitCodeText = "-" Then
            Return "[dim]-[/]"
        End If

        Return "[red]" & displayText & "[/]"
    End Function

    Private Shared Function CountReadyFeeds(snapshot As DashboardSnapshot) As Integer
        Return snapshot.QueuedFeeds.Where(Function(row) String.IsNullOrWhiteSpace(row.Reason)).Count()
    End Function

    Private Shared Function CountWaitingFeeds(snapshot As DashboardSnapshot) As Integer
        Return snapshot.QueuedFeeds.Where(Function(row) Not String.IsNullOrWhiteSpace(row.Reason)).Count()
    End Function

    Private Shared Function FormatDateTime(value As DateTime?) As String
        If Not value.HasValue Then
            Return "-"
        End If

        Return value.Value.ToString("HH:mm")
    End Function

    Private Shared Function FormatDuration(duration As TimeSpan) As String
        If duration.TotalHours >= 1 Then
            Return String.Format("{0:%h}h{0:%m}m", duration)
        End If

        If duration.TotalMinutes >= 1 Then
            Return String.Format("{0:%m}m{0:%s}s", duration)
        End If

        Return String.Format("{0:%s}s", duration)
    End Function

    Private Shared Function TruncateText(value As String, maxLength As Integer) As String
        If String.IsNullOrWhiteSpace(value) Then
            Return String.Empty
        End If

        If value.Length <= maxLength Then
            Return value
        End If

        If maxLength <= 3 Then
            Return value.Substring(0, maxLength)
        End If

        Return value.Substring(0, maxLength - 3) & "..."
    End Function

    Private Shared Function EscapeMarkup(value As String) As String
        If String.IsNullOrEmpty(value) Then
            Return String.Empty
        End If

        Return value.Replace("[", "[[").Replace("]", "]]")
    End Function
End Class
