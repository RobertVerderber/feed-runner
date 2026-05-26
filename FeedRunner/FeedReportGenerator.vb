Imports System.IO
Imports System.Linq
Imports QuestPDF.Fluent
Imports QuestPDF.Helpers
Imports QuestPDF.Infrastructure

Public Class FeedReportGenerator
    Private ReadOnly _config As AppConfig
    Private ReadOnly _logger As Logger
    Private ReadOnly _generatedAt As DateTime

    Public Sub New(config As AppConfig, logger As Logger)
        _config = config
        _logger = logger
        _generatedAt = DateTime.Now
    End Sub

    Public Sub Generate(outputPath As String)
        QuestPDF.Settings.License = LicenseType.Community

        Dim outputDirectory As String = Path.GetDirectoryName(outputPath)
        If Not String.IsNullOrWhiteSpace(outputDirectory) Then
            Directory.CreateDirectory(outputDirectory)
        End If

        Dim sortedFeeds As List(Of FeedConfig) = GetSortedFeeds()

        Document.Create(
            Sub(container)
                container.Page(
                    Sub(page)
                        page.Size(PageSizes.A4.Landscape())
                        page.MarginHorizontal(28)
                        page.MarginVertical(24)
                        page.DefaultTextStyle(
                            Function(style As TextStyle) As TextStyle
                                Return style.FontSize(9)
                            End Function)

                        page.Header().Element(AddressOf ComposeHeader)
                        page.Content().Column(
                            Sub(column As ColumnDescriptor)
                                column.Item().Element(AddressOf ComposeSummary)
                                column.Item().PaddingTop(12).Element(
                                    Sub(cardsHost As IContainer)
                                        ComposeFeedCards(cardsHost, sortedFeeds)
                                    End Sub)
                            End Sub)
                        page.Footer().AlignCenter().Text(
                            Sub(text As TextDescriptor)
                                text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1)
                                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1)
                                text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1)
                                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1)
                            End Sub)
                    End Sub)
            End Sub).GeneratePdf(outputPath)

        _logger.Info("Feed report written to: " & outputPath)
    End Sub

    Private Sub ComposeHeader(container As IContainer)
        container.Column(
            Sub(column As ColumnDescriptor)
                column.Spacing(4)
                column.Item().Text("Feed Runner Configuration Report").FontSize(18).Bold().FontColor(Colors.Blue.Darken2)
                column.Item().Text("Generated " & _generatedAt.ToString("yyyy-MM-dd HH:mm:ss")).FontSize(10).FontColor(Colors.Grey.Darken1)
            End Sub)
    End Sub

    Private Sub ComposeSummary(container As IContainer)
        Dim settings As RunnerSettings = _config.RunnerSettings
        Dim totalFeeds As Integer = If(_config.Feeds Is Nothing, 0, _config.Feeds.Count)
        Dim enabledFeeds As Integer = If(_config.Feeds Is Nothing, 0, _config.Feeds.Where(Function(feed) feed.Enabled).Count())
        Dim disabledFeeds As Integer = totalFeeds - enabledFeeds

        container.Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(
            Sub(column As ColumnDescriptor)
                column.Spacing(6)
                column.Item().Text("Summary").FontSize(11).Bold()

                column.Item().Row(
                    Sub(row As RowDescriptor)
                        row.RelativeItem().Text("Total feeds: " & totalFeeds.ToString())
                        row.RelativeItem().Text("Enabled: " & enabledFeeds.ToString())
                        row.RelativeItem().Text("Disabled: " & disabledFeeds.ToString())
                        row.RelativeItem().Text("Max concurrent: " & settings.MaxConcurrentFeeds.ToString())
                    End Sub)

                column.Item().Row(
                    Sub(row As RowDescriptor)
                        row.RelativeItem().Text("Console window mode: " & settings.FeedConsoleWindowMode)
                        row.RelativeItem().Text("Refresh seconds: " & settings.RefreshSeconds.ToString())
                        row.RelativeItem().Text("Test run mode: " & settings.TestRunMode.ToString())
                        row.RelativeItem().Text("Log folder: " & settings.LogFolder)
                    End Sub)
            End Sub)
    End Sub

    Private Sub ComposeFeedCards(container As IContainer, feeds As List(Of FeedConfig))
        container.Column(
            Sub(column As ColumnDescriptor)
                column.Spacing(10)
                column.Item().Text("Feeds (" & feeds.Count.ToString() & ")").FontSize(12).Bold()

                Dim index As Integer = 0
                While index < feeds.Count
                    Dim leftIndex As Integer = index
                    index += 1

                    Dim middleIndex As Integer = -1
                    If index < feeds.Count Then
                        middleIndex = index
                        index += 1
                    End If

                    Dim rightIndex As Integer = -1
                    If index < feeds.Count Then
                        rightIndex = index
                        index += 1
                    End If

                    column.Item().Row(
                        Sub(row As RowDescriptor)
                            row.Spacing(10)
                            AddFeedCardToRow(row, feeds, leftIndex)

                            If middleIndex >= 0 Then
                                AddFeedCardToRow(row, feeds, middleIndex)
                            Else
                                row.RelativeItem()
                            End If

                            If rightIndex >= 0 Then
                                AddFeedCardToRow(row, feeds, rightIndex)
                            Else
                                row.RelativeItem()
                            End If
                        End Sub)
                End While
            End Sub)
    End Sub

    Private Shared Sub AddFeedCardToRow(row As RowDescriptor, feeds As List(Of FeedConfig), feedIndex As Integer)
        row.RelativeItem().Element(
            Sub(cardHost As IContainer)
                ComposeFeedCard(cardHost, feeds(feedIndex))
            End Sub)
    End Sub

    Private Shared Sub ComposeFeedCard(container As IContainer, feed As FeedConfig)
        Dim borderColor As String = If(feed.Enabled, Colors.Blue.Lighten2, Colors.Grey.Lighten1)
        Dim headerBackground As String = If(feed.Enabled, Colors.Blue.Lighten4, Colors.Grey.Lighten4)
        Dim statusColor As String = If(feed.Enabled, Colors.Green.Darken2, Colors.Grey.Darken1)
        Dim statusText As String = If(feed.Enabled, "Enabled", "Disabled")

        container.
            Border(1).
            BorderColor(borderColor).
            Background(Colors.White).
            Column(
                Sub(column As ColumnDescriptor)
                    column.Item().Background(headerBackground).PaddingHorizontal(8).PaddingVertical(6).Row(
                        Sub(row As RowDescriptor)
                            row.RelativeItem().Text(If(feed.FeedName, String.Empty)).Bold().FontSize(10)
                            row.AutoItem().Text(statusText).FontSize(8).SemiBold().FontColor(statusColor)
                        End Sub)

                    column.Item().Padding(8).Column(
                        Sub(body As ColumnDescriptor)
                            body.Spacing(3)
                            CardDetailRow(body, "MLS", feed.MlsKey)
                            CardDetailRow(body, "Priority", feed.Priority.ToString())
                            CardDetailRow(body, "Every", feed.RunEveryMinutes.ToString() & " min")
                            CardDetailRow(body, "Weekend", FormatWeekendRunMinutes(feed))
                            CardDetailRow(body, "Window", FormatTimeWindow(feed))
                            CardDetailRow(body, "Daily", If(feed.RunOnceDaily, "Yes", "No"))
                            CardDetailRow(body, "Skip Days", FormatSkipDays(feed))
                            CardDetailRow(body, "Timeout", feed.TimeoutMinutes.ToString() & " min")
                            CardDetailRow(body, "Retries", feed.MaxRetries.ToString())
                            CardDetailRow(body, "Retry Delay", feed.RetryDelayMinutes.ToString() & " min")
                            CardDetailRow(body, "Executable", feed.ExecutablePath)

                            If Not String.IsNullOrWhiteSpace(feed.WorkingDirectory) Then
                                CardDetailRow(body, "Working Dir", feed.WorkingDirectory)
                            End If

                            If Not String.IsNullOrWhiteSpace(feed.Arguments) Then
                                CardDetailRow(body, "Arguments", feed.Arguments)
                            End If
                        End Sub)
                End Sub)
    End Sub

    Private Shared Sub CardDetailRow(column As ColumnDescriptor, label As String, value As String)
        column.Item().Row(
            Sub(row As RowDescriptor)
                row.ConstantItem(68).Text(label & ":").FontSize(7.5F).SemiBold().FontColor(Colors.Grey.Darken1)
                row.RelativeItem().Text(If(value, "-")).FontSize(7.5F)
            End Sub)
    End Sub

    Private Function GetSortedFeeds() As List(Of FeedConfig)
        If _config.Feeds Is Nothing Then
            Return New List(Of FeedConfig)()
        End If

        Return _config.Feeds.
            OrderBy(Function(feed) feed.Priority).
            ThenBy(Function(feed) feed.FeedName, StringComparer.OrdinalIgnoreCase).
            ToList()
    End Function

    Private Shared Function FormatWeekendRunMinutes(feed As FeedConfig) As String
        If feed.WeekendRunEveryMinutes.HasValue Then
            Return feed.WeekendRunEveryMinutes.Value.ToString() & "m"
        End If

        Return "-"
    End Function

    Private Shared Function FormatTimeWindow(feed As FeedConfig) As String
        Dim startTime As String = If(feed.StartTime, "09:00")
        Dim endTime As String = If(feed.EndTime, "20:00")
        Return startTime & "-" & endTime
    End Function

    Private Shared Function FormatSkipDays(feed As FeedConfig) As String
        Dim parts As New List(Of String)()

        If feed.SkipWeekends Then
            parts.Add("Weekends")
        End If

        If feed.SkipDaysOfWeek IsNot Nothing Then
            For Each skipDay As String In feed.SkipDaysOfWeek
                If Not String.IsNullOrWhiteSpace(skipDay) Then
                    parts.Add(skipDay.Trim())
                End If
            Next
        End If

        If parts.Count = 0 Then
            Return "-"
        End If

        Return String.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase))
    End Function
End Class
