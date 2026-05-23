Imports System.IO
Imports Newtonsoft.Json

Public Class StatusStore
    Private ReadOnly _statusFilePath As String
    Private ReadOnly _logger As Logger
    Private ReadOnly _syncRoot As New Object()
    Private _statusByFeed As Dictionary(Of String, FeedStatus)

    Public Sub New(statusFilePath As String, logger As Logger)
        If String.IsNullOrWhiteSpace(statusFilePath) Then
            _statusFilePath = "feed-status.json"
        Else
            _statusFilePath = statusFilePath
        End If

        _logger = logger
        _statusByFeed = New Dictionary(Of String, FeedStatus)(StringComparer.OrdinalIgnoreCase)
    End Sub

    Public Sub LoadAndRecoverInterrupted(feedNames As IEnumerable(Of String))
        SyncLock _syncRoot
            _statusByFeed = New Dictionary(Of String, FeedStatus)(StringComparer.OrdinalIgnoreCase)

            If File.Exists(_statusFilePath) Then
                Try
                    Dim json As String = File.ReadAllText(_statusFilePath)
                    Dim loaded As List(Of FeedStatus) = JsonConvert.DeserializeObject(Of List(Of FeedStatus))(json)

                    If loaded IsNot Nothing Then
                        For Each item As FeedStatus In loaded
                            If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.FeedName) Then
                                Continue For
                            End If

                            If item.CurrentlyRunning Then
                                item.CurrentlyRunning = False
                                item.LastStatus = "Interrupted"
                                item.LastRunEnd = DateTime.Now
                                item.LastErrorMessage = "Runner was interrupted while this feed was running."
                                _logger.Info("Recovered interrupted feed: " & item.FeedName)
                            End If

                            _statusByFeed(item.FeedName) = item
                        Next
                    End If
                Catch ex As Exception
                    _logger.LogError("Failed to read status file. Starting with empty status.", ex)
                End Try
            End If

            EnsureFeedsExist(feedNames)
            SaveInternal()
        End SyncLock
    End Sub

    Public Sub ResetAll(feedNames As IEnumerable(Of String))
        SyncLock _syncRoot
            _statusByFeed = New Dictionary(Of String, FeedStatus)(StringComparer.OrdinalIgnoreCase)
            EnsureFeedsExist(feedNames)

            For Each status As FeedStatus In _statusByFeed.Values
                status.LastRunStart = Nothing
                status.LastRunEnd = Nothing
                status.LastExitCode = Nothing
                status.LastStatus = "Reset"
                status.NextEligibleRun = Nothing
                status.ConsecutiveFailures = 0
                status.LastErrorMessage = String.Empty
                status.CurrentlyRunning = False
            Next

            SaveInternal()
            _logger.Info("Feed status reset. All configured feeds are eligible to run immediately.")
        End SyncLock
    End Sub

    Public Function GetStatus(feedName As String) As FeedStatus
        SyncLock _syncRoot
            If _statusByFeed.ContainsKey(feedName) Then
                Return CloneStatus(_statusByFeed(feedName))
            End If

            Dim created As New FeedStatus()
            created.FeedName = feedName
            _statusByFeed(feedName) = created
            Return CloneStatus(created)
        End SyncLock
    End Function

    Public Function GetAllStatuses() As List(Of FeedStatus)
        SyncLock _syncRoot
            Dim copy As New List(Of FeedStatus)()
            For Each item As FeedStatus In _statusByFeed.Values
                copy.Add(CloneStatus(item))
            Next

            Return copy
        End SyncLock
    End Function

    Public Sub UpdateStatus(status As FeedStatus)
        If status Is Nothing OrElse String.IsNullOrWhiteSpace(status.FeedName) Then
            Return
        End If

        SyncLock _syncRoot
            _statusByFeed(status.FeedName) = CloneStatus(status)
            SaveInternal()
        End SyncLock
    End Sub

    Private Sub EnsureFeedsExist(feedNames As IEnumerable(Of String))
        If feedNames Is Nothing Then
            Return
        End If

        For Each feedName As String In feedNames
            If Not _statusByFeed.ContainsKey(feedName) Then
                Dim status As New FeedStatus()
                status.FeedName = feedName
                _statusByFeed(feedName) = status
            End If
        Next
    End Sub

    Private Sub SaveInternal()
        Try
            Dim statusDirectory As String = Path.GetDirectoryName(Path.GetFullPath(_statusFilePath))
            If Not String.IsNullOrWhiteSpace(statusDirectory) Then
                Directory.CreateDirectory(statusDirectory)
            End If

            Dim tempPath As String = _statusFilePath & ".tmp"
            Dim statuses As New List(Of FeedStatus)(_statusByFeed.Values)
            Dim json As String = JsonConvert.SerializeObject(statuses, Formatting.Indented)
            File.WriteAllText(tempPath, json)

            If File.Exists(_statusFilePath) Then
                File.Delete(_statusFilePath)
            End If

            File.Move(tempPath, _statusFilePath)
            _logger.Info("Status file saved: " & _statusFilePath)
        Catch ex As Exception
            _logger.LogError("Failed to save status file: " & _statusFilePath, ex)
        End Try
    End Sub

    Private Shared Function CloneStatus(source As FeedStatus) As FeedStatus
        Return New FeedStatus() With {
            .FeedName = source.FeedName,
            .LastRunStart = source.LastRunStart,
            .LastRunEnd = source.LastRunEnd,
            .LastExitCode = source.LastExitCode,
            .LastStatus = source.LastStatus,
            .NextEligibleRun = source.NextEligibleRun,
            .ConsecutiveFailures = source.ConsecutiveFailures,
            .LastErrorMessage = source.LastErrorMessage,
            .CurrentlyRunning = source.CurrentlyRunning
        }
    End Function
End Class
