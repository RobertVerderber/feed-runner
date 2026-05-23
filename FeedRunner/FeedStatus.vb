Public Class FeedStatus
    Public Property FeedName As String
    Public Property LastRunStart As DateTime?
    Public Property LastRunEnd As DateTime?
    Public Property LastExitCode As Integer?
    Public Property LastStatus As String
    Public Property NextEligibleRun As DateTime?
    Public Property ConsecutiveFailures As Integer
    Public Property LastErrorMessage As String
    Public Property CurrentlyRunning As Boolean

    Public Sub New()
        LastStatus = "NeverRun"
        ConsecutiveFailures = 0
        CurrentlyRunning = False
    End Sub
End Class
