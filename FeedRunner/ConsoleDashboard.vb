Imports Spectre.Console
Imports Spectre.Console.Rendering
Imports System.Linq
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
        liveDisplay.Overflow = VerticalOverflow.Ellipsis

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
        Dim summaryLayout As New Layout(BuildSummaryPanel(snapshot))
        summaryLayout.Size = 9

        Dim runningLayout As New Layout(BuildRunningTable(snapshot))
        runningLayout.Size = GetRunningTableHeight(snapshot)

        Dim queuedLayout As New Layout(BuildQueuedTable(snapshot))
        queuedLayout.Size = 12

        Dim completedLayout As New Layout(BuildCompletedTable(snapshot))
        completedLayout.Size = 12

        Dim layout As New Layout("Root")
        layout.SplitRows(summaryLayout, runningLayout, queuedLayout, completedLayout)
        Return layout
    End Function

    Private Shared Function GetRunningTableHeight(snapshot As DashboardSnapshot) As Integer
        Dim dataRowCount As Integer = snapshot.RunningFeeds.Count
        If dataRowCount = 0 Then
            dataRowCount = 1
        End If

        ' Title, header, borders, and padding need extra lines beyond the data rows.
        Return Math.Max(10, dataRowCount + 7)
    End Function

    Private Shared Function BuildSummaryPanel(snapshot As DashboardSnapshot) As Panel
        Dim grid As New Grid()

        Dim columnOne As New GridColumn()
        columnOne.NoWrap = True
        grid.AddColumn(columnOne)

        Dim columnTwo As New GridColumn()
        columnTwo.NoWrap = True
        grid.AddColumn(columnTwo)

        Dim columnThree As New GridColumn()
        columnThree.NoWrap = True
        grid.AddColumn(columnThree)

        Dim columnFour As New GridColumn()
        columnFour.NoWrap = True
        grid.AddColumn(columnFour)

        grid.AddRow(
            "Current Time: " & snapshot.CurrentTime.ToString("yyyy-MM-dd HH:mm:ss"),
            "Total Feeds: " & snapshot.TotalFeeds.ToString(),
            "Enabled: " & snapshot.TotalEnabledFeeds.ToString(),
            "Disabled: " & snapshot.TotalDisabledFeeds.ToString())

        grid.AddRow(
            "Running: " & snapshot.RunningCount.ToString(),
            "Eligible: " & snapshot.EligibleCount.ToString(),
            "Blocked (MLS): " & snapshot.BlockedByMlsCount.ToString(),
            "Refresh: " & snapshot.CurrentTime.ToString("HH:mm:ss"))

        grid.AddRow(
            "Completed Today: " & snapshot.CompletedTodayCount.ToString(),
            "Failed Today: " & snapshot.FailedTodayCount.ToString(),
            String.Empty,
            String.Empty)

        Dim panel As New Panel(grid)
        panel.Header = New PanelHeader("[bold]Feed Runner Dashboard[/]")
        panel.Border = BoxBorder.Rounded
        Return panel
    End Function

    Private Shared Function BuildRunningTable(snapshot As DashboardSnapshot) As Table
        Dim table As New Table()
        table.Title = New TableTitle("[yellow]Currently Running[/]")
        table.Border = TableBorder.Rounded
        table.AddColumn("FeedName")
        table.AddColumn("MlsKey")
        table.AddColumn("PID")
        table.AddColumn("StartTime")
        table.AddColumn("Elapsed")
        table.AddColumn("ExecutablePath")

        If snapshot.RunningFeeds.Count = 0 Then
            table.AddRow("-", "-", "-", "-", "-", "-")
        Else
            Dim sortedRunningFeeds As List(Of RunningFeedRow) = snapshot.RunningFeeds.OrderBy(Function(row) row.FeedName).ToList()

            For Each row As RunningFeedRow In sortedRunningFeeds
                Dim elapsed As TimeSpan = snapshot.CurrentTime - row.StartTime
                Dim processIdText As String = "-"
                If row.ProcessId > 0 Then
                    processIdText = row.ProcessId.ToString()
                End If

                table.AddRow(
                    row.FeedName,
                    row.MlsKey,
                    processIdText,
                    row.StartTime.ToString("HH:mm:ss"),
                    FormatDuration(elapsed),
                    row.ExecutablePath)
            Next
        End If

        Return table
    End Function

    Private Shared Function BuildQueuedTable(snapshot As DashboardSnapshot) As Table
        Dim table As New Table()
        table.Title = New TableTitle("[blue]Queued / Waiting Feeds[/]")
        table.Border = TableBorder.Rounded
        table.AddColumn("FeedName")
        table.AddColumn("MlsKey")
        table.AddColumn("NextEligibleRun")
        table.AddColumn("Priority")
        table.AddColumn("Reason")

        Dim rowsToShow As Integer = Math.Min(10, snapshot.QueuedFeeds.Count)
        If rowsToShow = 0 Then
            table.AddRow("-", "-", "-", "-", "-")
        Else
            For index As Integer = 0 To rowsToShow - 1
                Dim row As QueuedFeedRow = snapshot.QueuedFeeds(index)
                Dim nextRunText As String = "-"
                If row.NextEligibleRun.HasValue Then
                    nextRunText = row.NextEligibleRun.Value.ToString("yyyy-MM-dd HH:mm:ss")
                End If

                Dim reasonText As String = If(String.IsNullOrWhiteSpace(row.Reason), "Ready", row.Reason)
                table.AddRow(
                    row.FeedName,
                    row.MlsKey,
                    nextRunText,
                    row.Priority.ToString(),
                    reasonText)
            Next
        End If

        Return table
    End Function

    Private Shared Function BuildCompletedTable(snapshot As DashboardSnapshot) As Table
        Dim table As New Table()
        table.Title = New TableTitle("[green]Recent Completed Feeds[/]")
        table.Border = TableBorder.Rounded
        table.AddColumn("FeedName")
        table.AddColumn("MlsKey")
        table.AddColumn("EndTime")
        table.AddColumn("Status")
        table.AddColumn("ExitCode")
        table.AddColumn("Duration")

        If snapshot.RecentCompletedFeeds.Count = 0 Then
            table.AddRow("-", "-", "-", "-", "-", "-")
        Else
            For Each row As CompletedFeedRow In snapshot.RecentCompletedFeeds
                Dim exitCodeText As String = "-"
                If row.ExitCode.HasValue Then
                    exitCodeText = row.ExitCode.Value.ToString()
                End If

                table.AddRow(
                    row.FeedName,
                    row.MlsKey,
                    row.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    row.Status,
                    exitCodeText,
                    FormatDuration(row.Duration))
            Next
        End If

        Return table
    End Function

    Private Shared Function FormatDuration(duration As TimeSpan) As String
        If duration.TotalHours >= 1 Then
            Return String.Format("{0:%h}h {0:%m}m {0:%s}s", duration)
        End If

        If duration.TotalMinutes >= 1 Then
            Return String.Format("{0:%m}m {0:%s}s", duration)
        End If

        Return String.Format("{0:%s}s", duration)
    End Function
End Class
