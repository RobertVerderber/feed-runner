Imports Spectre.Console
Imports Spectre.Console.Rendering
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Class ConsoleDashboard
    Private ReadOnly _service As FeedRunnerService
    Private ReadOnly _refreshSeconds As Integer

    Public Sub New(service As FeedRunnerService, refreshSeconds As Integer)
        _service = service
        _refreshSeconds = Math.Max(1, refreshSeconds)
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

    Private Shared Function BuildLayout(snapshot As DashboardSnapshot) As IRenderable
        Dim consoleHeight As Integer = GetConsoleHeight()
        Dim headerSize As Integer = 5
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

    Private Shared Function GetRunningSectionHeight(snapshot As DashboardSnapshot) As Integer
        Dim dataRowCount As Integer = Math.Max(1, snapshot.RunningFeeds.Count)
        Return Math.Max(7, dataRowCount + 5)
    End Function

    Private Shared Function RowsForLayoutHeight(layoutHeight As Integer) As Integer
        Return Math.Max(1, layoutHeight - 4)
    End Function

    Private Shared Function BuildHeaderPanel(snapshot As DashboardSnapshot) As Panel
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

    Private Shared Function BuildRunningPanel(snapshot As DashboardSnapshot) As Panel
        Return WrapSection("[yellow bold]Running[/]", BuildRunningTable(snapshot), Color.Yellow)
    End Function

    Private Shared Function BuildQueuedPanel(snapshot As DashboardSnapshot, maxRows As Integer) As Panel
        Dim title As String = "[deepskyblue1 bold]Queue[/] [dim](" & snapshot.QueuedFeeds.Count.ToString() & ")[/]"
        Return WrapSection(title, BuildQueuedTable(snapshot, maxRows), Color.DeepSkyBlue1)
    End Function

    Private Shared Function BuildCompletedPanel(snapshot As DashboardSnapshot, maxRows As Integer) As Panel
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

    Private Shared Sub ConfigureDataTable(table As Table)
        table.Border = TableBorder.None
        table.Expand = True
        table.ShowHeaders = True
    End Sub

    Private Shared Function BuildRunningTable(snapshot As DashboardSnapshot) As Table
        Dim table As New Table()
        ConfigureDataTable(table)

        Dim feedWidth As Integer = Math.Max(14, GetConsoleWidth() - 78)
        table.AddColumn(New TableColumn("[dim]Feed[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]MLS[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]PID[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Started[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Elapsed[/]").RightAligned())
        table.AddColumn(New TableColumn("[dim]Executable[/]").LeftAligned())

        If snapshot.RunningFeeds.Count = 0 Then
            table.AddRow(
                "[dim italic]No feeds running[/]",
                String.Empty,
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
                    "[yellow]" & EscapeMarkup(TruncateText(row.FeedName, feedWidth)) & "[/]",
                    EscapeMarkup(TruncateText(row.MlsKey, 10)),
                    processIdText,
                    row.StartTime.ToString("HH:mm:ss"),
                    "[bold yellow]" & FormatDuration(elapsed) & "[/]",
                    "[dim]" & EscapeMarkup(TruncateText(row.ExecutablePath, feedWidth + 10)) & "[/]")
            Next
        End If

        Return table
    End Function

    Private Shared Function BuildQueuedTable(snapshot As DashboardSnapshot, maxRows As Integer) As Table
        Dim table As New Table()
        ConfigureDataTable(table)

        Dim halfWidth As Integer = Math.Max(40, GetConsoleWidth() \ 2)
        Dim feedWidth As Integer = Math.Max(12, halfWidth - 44)

        table.AddColumn(New TableColumn("[dim]Feed[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]MLS[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]Next[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Pri[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Status[/]").LeftAligned())

        Dim rowsToShow As Integer = Math.Min(maxRows, snapshot.QueuedFeeds.Count)
        If rowsToShow = 0 Then
            table.AddRow(
                "[dim italic]Queue empty[/]",
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty)
        Else
            For index As Integer = 0 To rowsToShow - 1
                Dim row As QueuedFeedRow = snapshot.QueuedFeeds(index)
                Dim reasonText As String = If(String.IsNullOrWhiteSpace(row.Reason), "Ready", row.Reason)

                table.AddRow(
                    EscapeMarkup(TruncateText(row.FeedName, feedWidth)),
                    EscapeMarkup(TruncateText(row.MlsKey, 8)),
                    FormatDateTime(row.NextEligibleRun),
                    row.Priority.ToString(),
                    FormatQueueStatusText(reasonText))
            Next

            If snapshot.QueuedFeeds.Count > rowsToShow Then
                table.AddRow(
                    "[dim italic]... +" & (snapshot.QueuedFeeds.Count - rowsToShow).ToString() & " more[/]",
                    String.Empty,
                    String.Empty,
                    String.Empty,
                    String.Empty)
            End If
        End If

        Return table
    End Function

    Private Shared Function BuildCompletedTable(snapshot As DashboardSnapshot, maxRows As Integer) As Table
        Dim table As New Table()
        ConfigureDataTable(table)

        Dim halfWidth As Integer = Math.Max(40, GetConsoleWidth() \ 2)
        Dim feedWidth As Integer = Math.Max(12, halfWidth - 42)

        table.AddColumn(New TableColumn("[dim]Feed[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]MLS[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]Finished[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Result[/]").LeftAligned())
        table.AddColumn(New TableColumn("[dim]Code[/]").Centered())
        table.AddColumn(New TableColumn("[dim]Duration[/]").RightAligned())

        Dim rowsToShow As Integer = Math.Min(maxRows, snapshot.RecentCompletedFeeds.Count)
        If rowsToShow = 0 Then
            table.AddRow(
                "[dim italic]No completions yet[/]",
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty,
                String.Empty)
        Else
            For index As Integer = 0 To rowsToShow - 1
                Dim row As CompletedFeedRow = snapshot.RecentCompletedFeeds(index)
                Dim exitCodeText As String = If(row.ExitCode.HasValue, row.ExitCode.Value.ToString(), "-")

                table.AddRow(
                    EscapeMarkup(TruncateText(row.FeedName, feedWidth)),
                    EscapeMarkup(TruncateText(row.MlsKey, 8)),
                    row.EndTime.ToString("HH:mm:ss"),
                    FormatCompletionStatusText(row.Status),
                    FormatExitCodeText(exitCodeText, row.Status),
                    FormatDuration(row.Duration))
            Next
        End If

        Return table
    End Function

    Private Shared Function FormatQueueStatusText(reason As String) As String
        If String.Equals(reason, "Ready", StringComparison.OrdinalIgnoreCase) Then
            Return "[green]Ready[/]"
        End If

        If String.Equals(reason, "MLS in use", StringComparison.OrdinalIgnoreCase) Then
            Return "[orange1]" & EscapeMarkup(reason) & "[/]"
        End If

        If String.Equals(reason, "Max concurrent reached", StringComparison.OrdinalIgnoreCase) Then
            Return "[deepskyblue1]" & EscapeMarkup(reason) & "[/]"
        End If

        If String.Equals(reason, "Disabled", StringComparison.OrdinalIgnoreCase) Then
            Return "[dim]" & EscapeMarkup(reason) & "[/]"
        End If

        Return "[grey]" & EscapeMarkup(TruncateText(reason, 20)) & "[/]"
    End Function

    Private Shared Function FormatCompletionStatusText(status As String) As String
        If String.IsNullOrWhiteSpace(status) Then
            Return "[dim]-[/]"
        End If

        If String.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) Then
            Return "[green bold]Success[/]"
        End If

        Return "[red bold]" & EscapeMarkup(TruncateText(status, 14)) & "[/]"
    End Function

    Private Shared Function FormatExitCodeText(exitCodeText As String, status As String) As String
        If String.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) Then
            Return "[green]" & exitCodeText & "[/]"
        End If

        If exitCodeText = "-" Then
            Return "[dim]-[/]"
        End If

        Return "[red]" & exitCodeText & "[/]"
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
