Public Class RunningFeedRow
    Public Property FeedName As String
    Public Property MlsKey As String
    Public Property StartTime As DateTime
    Public Property ProcessId As Integer
    Public Property ExecutablePath As String
End Class

Public Class QueuedFeedRow
    Public Property FeedName As String
    Public Property MlsKey As String
    Public Property NextEligibleRun As DateTime?
    Public Property Priority As Integer
    Public Property Reason As String
End Class

Public Class CompletedFeedRow
    Public Property FeedName As String
    Public Property MlsKey As String
    Public Property EndTime As DateTime
    Public Property Status As String
    Public Property ExitCode As Integer?
    Public Property Duration As TimeSpan
End Class

Public Class DashboardSnapshot
    Public Property CurrentTime As DateTime
    Public Property TotalFeeds As Integer
    Public Property TotalEnabledFeeds As Integer
    Public Property TotalDisabledFeeds As Integer
    Public Property RunningCount As Integer
    Public Property EligibleCount As Integer
    Public Property BlockedByMlsCount As Integer
    Public Property CompletedTodayCount As Integer
    Public Property FailedTodayCount As Integer
    Public Property RunningFeeds As List(Of RunningFeedRow)
    Public Property QueuedFeeds As List(Of QueuedFeedRow)
    Public Property RecentCompletedFeeds As List(Of CompletedFeedRow)

    Public Sub New()
        RunningFeeds = New List(Of RunningFeedRow)()
        QueuedFeeds = New List(Of QueuedFeedRow)()
        RecentCompletedFeeds = New List(Of CompletedFeedRow)()
    End Sub
End Class
