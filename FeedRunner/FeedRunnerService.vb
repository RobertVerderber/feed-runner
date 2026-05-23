Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

Public Class FeedRunnerService
    Private Class RunningFeedInfo
        Public Property FeedName As String
        Public Property MlsKey As String
        Public Property StartTime As DateTime
        Public Property ProcessId As Integer
        Public Property ExecutablePath As String
    End Class

    Private ReadOnly _config As AppConfig
    Private ReadOnly _statusStore As StatusStore
    Private ReadOnly _processRunner As FeedProcessRunner
    Private ReadOnly _logger As Logger
    Private ReadOnly _semaphore As SemaphoreSlim
    Private ReadOnly _stateLock As New Object()
    Private ReadOnly _snapshotLock As New Object()

    Private _activeMlsKeys As HashSet(Of String)
    Private _runningFeeds As Dictionary(Of String, RunningFeedInfo)
    Private _pendingStarts As HashSet(Of String)
    Private _retryAttempts As Dictionary(Of String, Integer)
    Private _recentCompletions As List(Of CompletedFeedRow)
    Private _completedTodayCount As Integer
    Private _failedTodayCount As Integer
    Private _todayDate As Date
    Private _stopRequested As Boolean
    Private _latestSnapshot As DashboardSnapshot

    Public Sub New(config As AppConfig, statusStore As StatusStore, processRunner As FeedProcessRunner, logger As Logger)
        _config = config
        _statusStore = statusStore
        _processRunner = processRunner
        _logger = logger

        Dim maxConcurrent As Integer = 5
        If config IsNot Nothing AndAlso config.RunnerSettings IsNot Nothing Then
            maxConcurrent = Math.Max(1, config.RunnerSettings.MaxConcurrentFeeds)
        End If

        _semaphore = New SemaphoreSlim(maxConcurrent, maxConcurrent)
        _activeMlsKeys = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        _runningFeeds = New Dictionary(Of String, RunningFeedInfo)(StringComparer.OrdinalIgnoreCase)
        _pendingStarts = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        _retryAttempts = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        _recentCompletions = New List(Of CompletedFeedRow)()
        _todayDate = DateTime.Today
        _latestSnapshot = New DashboardSnapshot()
    End Sub

    Public Sub RequestStop()
        _stopRequested = True
    End Sub

    Public Function GetSnapshot() As DashboardSnapshot
        SyncLock _snapshotLock
            Return CloneSnapshot(_latestSnapshot)
        End SyncLock
    End Function

    Public Async Function RunAsync(cancellationToken As CancellationToken) As Task
        _logger.Info("Feed runner service started.")

        Dim refreshSeconds As Integer = 5
        If _config.RunnerSettings IsNot Nothing Then
            refreshSeconds = Math.Max(1, _config.RunnerSettings.RefreshSeconds)
        End If

        While Not _stopRequested AndAlso Not cancellationToken.IsCancellationRequested
            ResetDailyCountersIfNeeded()
            UpdateSnapshot(DateTime.Now)
            TryStartEligibleFeeds(DateTime.Now)

            Try
                Await Task.Delay(refreshSeconds * 1000, cancellationToken).ConfigureAwait(False)
            Catch ex As TaskCanceledException
                Exit While
            End Try
        End While

        _logger.Info("Feed runner service stopping.")
    End Function

    Private Sub ResetDailyCountersIfNeeded()
        If DateTime.Today <> _todayDate Then
            _todayDate = DateTime.Today
            _completedTodayCount = 0
            _failedTodayCount = 0
        End If
    End Sub

    Private Sub TryStartEligibleFeeds(now As DateTime)
        If _config Is Nothing OrElse _config.Feeds Is Nothing Then
            Return
        End If

        Dim candidates As List(Of FeedConfig) = BuildSortedCandidates(now)
        For Each feed As FeedConfig In candidates
            If _stopRequested Then
                Exit For
            End If

            If IsFeedStartingOrRunning(feed.FeedName) Then
                Continue For
            End If

            If IsMlsKeyActive(feed.MlsKey) Then
                Continue For
            End If

            If _semaphore.CurrentCount <= 0 Then
                Exit For
            End If

            StartFeedExecution(feed)
        Next
    End Sub

    Private Function BuildSortedCandidates(now As DateTime) As List(Of FeedConfig)
        Dim candidates As New List(Of FeedConfig)()

        For Each feed As FeedConfig In _config.Feeds
            If Not feed.Enabled Then
                Continue For
            End If

            If Not feed.IsWithinTimeWindow(now) AndAlso Not IsTestRunMode() Then
                Continue For
            End If

            If IsFeedStartingOrRunning(feed.FeedName) Then
                Continue For
            End If

            Dim status As FeedStatus = _statusStore.GetStatus(feed.FeedName)
            If Not IsTestRunMode() AndAlso status.NextEligibleRun.HasValue AndAlso status.NextEligibleRun.Value > now Then
                Continue For
            End If

            If Not IsTestRunMode() AndAlso feed.RunOnceDaily AndAlso HasCompletedSuccessfullyToday(status, now) Then
                Continue For
            End If

            candidates.Add(feed)
        Next

        candidates.Sort(
            Function(left As FeedConfig, right As FeedConfig)
                Dim priorityCompare As Integer = left.Priority.CompareTo(right.Priority)
                If priorityCompare <> 0 Then
                    Return priorityCompare
                End If

                Dim leftStatus As FeedStatus = _statusStore.GetStatus(left.FeedName)
                Dim rightStatus As FeedStatus = _statusStore.GetStatus(right.FeedName)
                Dim leftLastRun As DateTime = If(leftStatus.LastRunStart.HasValue, leftStatus.LastRunStart.Value, DateTime.MinValue)
                Dim rightLastRun As DateTime = If(rightStatus.LastRunStart.HasValue, rightStatus.LastRunStart.Value, DateTime.MinValue)
                Dim lastRunCompare As Integer = leftLastRun.CompareTo(rightLastRun)
                If lastRunCompare <> 0 Then
                    Return lastRunCompare
                End If

                Return String.Compare(left.FeedName, right.FeedName, StringComparison.OrdinalIgnoreCase)
            End Function)

        Return candidates
    End Function

    Private Sub StartFeedExecution(feed As FeedConfig)
        SyncLock _stateLock
            If _runningFeeds.ContainsKey(feed.FeedName) OrElse _pendingStarts.Contains(feed.FeedName) Then
                Return
            End If

            If _activeMlsKeys.Contains(feed.MlsKey) Then
                Return
            End If

            _pendingStarts.Add(feed.FeedName)
        End SyncLock

        Dim ignored As Task = ExecuteFeedAsync(feed)
    End Sub

    Private Async Function ExecuteFeedAsync(feed As FeedConfig) As Task
        Dim started As Boolean = False

        Try
            Await _semaphore.WaitAsync().ConfigureAwait(False)

            SyncLock _stateLock
                If _runningFeeds.ContainsKey(feed.FeedName) OrElse _activeMlsKeys.Contains(feed.MlsKey) Then
                    Return
                End If

                Dim runningInfo As New RunningFeedInfo()
                runningInfo.FeedName = feed.FeedName
                runningInfo.MlsKey = feed.MlsKey
                runningInfo.StartTime = DateTime.Now
                runningInfo.ExecutablePath = feed.ExecutablePath

                _runningFeeds(feed.FeedName) = runningInfo
                _activeMlsKeys.Add(feed.MlsKey)
                started = True
            End SyncLock

            If Not started Then
                Return
            End If

            Dim startStatus As FeedStatus = _statusStore.GetStatus(feed.FeedName)
            startStatus.CurrentlyRunning = True
            startStatus.LastRunStart = DateTime.Now
            startStatus.LastStatus = "Running"
            startStatus.LastErrorMessage = String.Empty
            _statusStore.UpdateStatus(startStatus)
            _logger.Info("Feed started: " & feed.FeedName)
            UpdateSnapshot(DateTime.Now)

            Dim onProcessStarted As Action(Of Integer) =
                Sub(processId As Integer)
                    SyncLock _stateLock
                        If _runningFeeds.ContainsKey(feed.FeedName) Then
                            _runningFeeds(feed.FeedName).ProcessId = processId
                        End If
                    End SyncLock

                    _logger.Info("Feed process started: " & feed.FeedName & " (PID " & processId.ToString() & ")")
                    UpdateSnapshot(DateTime.Now)
                End Sub

            Dim result As FeedExecutionResult = Await _processRunner.RunFeedAsync(feed, CancellationToken.None, onProcessStarted).ConfigureAwait(False)
            HandleFeedCompletion(feed, result)
        Catch ex As Exception
            _logger.LogError("Unhandled exception in feed execution for '" & feed.FeedName & "'.", ex)

            If started Then
                Dim failureResult As New FeedExecutionResult()
                failureResult.FeedName = feed.FeedName
                failureResult.StartTime = DateTime.Now
                failureResult.EndTime = DateTime.Now
                failureResult.Status = "Exception"
                failureResult.ExitCode = -3
                failureResult.ErrorMessage = ex.Message
                HandleFeedCompletion(feed, failureResult)
            End If
        Finally
            SyncLock _stateLock
                _pendingStarts.Remove(feed.FeedName)

                If _runningFeeds.ContainsKey(feed.FeedName) Then
                    _runningFeeds.Remove(feed.FeedName)
                End If

                If _activeMlsKeys.Contains(feed.MlsKey) Then
                    _activeMlsKeys.Remove(feed.MlsKey)
                End If
            End SyncLock

            _semaphore.Release()
            UpdateSnapshot(DateTime.Now)
        End Try
    End Function

    Private Sub HandleFeedCompletion(feed As FeedConfig, result As FeedExecutionResult)
        Dim status As FeedStatus = _statusStore.GetStatus(feed.FeedName)
        status.CurrentlyRunning = False
        status.LastRunEnd = result.EndTime
        status.LastExitCode = result.ExitCode
        status.LastStatus = result.Status
        status.LastErrorMessage = If(result.ErrorMessage, String.Empty)

        Dim retryAttempt As Integer = GetRetryAttempt(feed.FeedName)
        Dim failed As Boolean = Not result.Succeeded

        If failed Then
            status.ConsecutiveFailures += 1
            RecordCompletion(feed, result, True)

            If retryAttempt < feed.MaxRetries Then
                SetRetryAttempt(feed.FeedName, retryAttempt + 1)
                status.NextEligibleRun = DateTime.Now.AddMinutes(Math.Max(1, feed.RetryDelayMinutes))
                status.LastStatus = result.Status & " (Retry " & (retryAttempt + 1).ToString() & " of " & feed.MaxRetries.ToString() & " scheduled)"
                _logger.Info("Feed failed and will retry: " & feed.FeedName)
            Else
                SetRetryAttempt(feed.FeedName, 0)
                status.NextEligibleRun = feed.GetNextRunTimeAfterFailure(result.EndTime)
                _logger.LogError("Feed failed: " & feed.FeedName & " | " & status.LastErrorMessage)
            End If
        Else
            status.ConsecutiveFailures = 0
            SetRetryAttempt(feed.FeedName, 0)
            status.NextEligibleRun = feed.GetNextRunTimeAfterSuccess(result.EndTime)
            RecordCompletion(feed, result, False)
            _logger.Info("Feed completed successfully: " & feed.FeedName)
        End If

        _statusStore.UpdateStatus(status)
        UpdateSnapshot(DateTime.Now)
    End Sub

    Private Sub RecordCompletion(feed As FeedConfig, result As FeedExecutionResult, isFailure As Boolean)
        SyncLock _stateLock
            If isFailure Then
                _failedTodayCount += 1
            Else
                _completedTodayCount += 1
            End If

            Dim row As New CompletedFeedRow()
            row.FeedName = feed.FeedName
            row.MlsKey = feed.MlsKey
            row.EndTime = result.EndTime
            row.Status = result.Status
            row.ExitCode = result.ExitCode
            row.Duration = result.Duration

            _recentCompletions.Insert(0, row)
            If _recentCompletions.Count > 20 Then
                _recentCompletions.RemoveAt(_recentCompletions.Count - 1)
            End If
        End SyncLock
    End Sub

    Private Sub UpdateSnapshot(now As DateTime)
        Dim snapshot As New DashboardSnapshot()
        snapshot.CurrentTime = now

        If _config IsNot Nothing AndAlso _config.Feeds IsNot Nothing Then
            snapshot.TotalFeeds = _config.Feeds.Count
            snapshot.TotalEnabledFeeds = _config.Feeds.Where(Function(f) f.Enabled).Count()
            snapshot.TotalDisabledFeeds = _config.Feeds.Where(Function(f) Not f.Enabled).Count()
        End If

        SyncLock _stateLock
            snapshot.RunningCount = _runningFeeds.Count
            snapshot.CompletedTodayCount = _completedTodayCount
            snapshot.FailedTodayCount = _failedTodayCount

            For Each item As RunningFeedInfo In _runningFeeds.Values
                Dim row As New RunningFeedRow()
                row.FeedName = item.FeedName
                row.MlsKey = item.MlsKey
                row.StartTime = item.StartTime
                row.ProcessId = item.ProcessId
                row.ExecutablePath = item.ExecutablePath
                snapshot.RunningFeeds.Add(row)
            Next

            snapshot.RecentCompletedFeeds.AddRange(_recentCompletions)
        End SyncLock

        Dim maxConcurrent As Integer = Math.Max(1, _config.RunnerSettings.MaxConcurrentFeeds)
        Dim eligibleCount As Integer = 0
        Dim blockedByMlsCount As Integer = 0
        Dim queuedRows As New List(Of QueuedFeedRow)()

        If _config.Feeds IsNot Nothing Then
            For Each feed As FeedConfig In _config.Feeds
                Dim reason As String = GetQueueReason(feed, now, maxConcurrent)
                If String.IsNullOrWhiteSpace(reason) Then
                    eligibleCount += 1
                ElseIf String.Equals(reason, "MLS in use", StringComparison.OrdinalIgnoreCase) Then
                    blockedByMlsCount += 1
                End If

                If Not feed.Enabled Then
                    Continue For
                End If

                If IsFeedStartingOrRunning(feed.FeedName) Then
                    Continue For
                End If

                Dim status As FeedStatus = _statusStore.GetStatus(feed.FeedName)
                Dim row As New QueuedFeedRow()
                row.FeedName = feed.FeedName
                row.MlsKey = feed.MlsKey
                row.NextEligibleRun = status.NextEligibleRun
                row.Priority = feed.Priority
                row.Reason = reason
                queuedRows.Add(row)
            Next
        End If

        queuedRows.Sort(
            Function(left As QueuedFeedRow, right As QueuedFeedRow)
                Dim priorityCompare As Integer = left.Priority.CompareTo(right.Priority)
                If priorityCompare <> 0 Then
                    Return priorityCompare
                End If

                Dim leftNext As DateTime = If(left.NextEligibleRun.HasValue, left.NextEligibleRun.Value, DateTime.MinValue)
                Dim rightNext As DateTime = If(right.NextEligibleRun.HasValue, right.NextEligibleRun.Value, DateTime.MinValue)
                Dim nextCompare As Integer = leftNext.CompareTo(rightNext)
                If nextCompare <> 0 Then
                    Return nextCompare
                End If

                Return String.Compare(left.FeedName, right.FeedName, StringComparison.OrdinalIgnoreCase)
            End Function)

        snapshot.EligibleCount = eligibleCount
        snapshot.BlockedByMlsCount = blockedByMlsCount
        snapshot.QueuedFeeds = queuedRows

        If _config.RunnerSettings IsNot Nothing Then
            snapshot.TestRunMode = _config.RunnerSettings.TestRunMode
            snapshot.TestRunDurationSeconds = Math.Max(1, _config.RunnerSettings.TestRunDurationSeconds)
        End If

        SyncLock _snapshotLock
            _latestSnapshot = snapshot
        End SyncLock
    End Sub

    Private Function GetQueueReason(feed As FeedConfig, now As DateTime, maxConcurrent As Integer) As String
        If Not feed.Enabled Then
            Return "Disabled"
        End If

        If IsFeedStartingOrRunning(feed.FeedName) Then
            Return "Already running"
        End If

        If Not feed.IsWithinTimeWindow(now) AndAlso Not IsTestRunMode() Then
            Return "Outside time window"
        End If

        Dim status As FeedStatus = _statusStore.GetStatus(feed.FeedName)

        If Not IsTestRunMode() AndAlso feed.RunOnceDaily AndAlso HasCompletedSuccessfullyToday(status, now) Then
            Return "Completed for today"
        End If

        If Not IsTestRunMode() AndAlso status.NextEligibleRun.HasValue AndAlso status.NextEligibleRun.Value > now Then
            If feed.RunOnceDaily Then
                Return "Waiting until tomorrow"
            End If

            Return "Waiting for next run"
        End If

        If IsMlsKeyActive(feed.MlsKey) Then
            Return "MLS in use"
        End If

        If GetRunningCount() >= maxConcurrent Then
            Return "Max concurrent reached"
        End If

        Return String.Empty
    End Function

    Private Function IsTestRunMode() As Boolean
        Return _config IsNot Nothing AndAlso
            _config.RunnerSettings IsNot Nothing AndAlso
            _config.RunnerSettings.TestRunMode
    End Function

    Private Shared Function HasCompletedSuccessfullyToday(status As FeedStatus, now As DateTime) As Boolean
        If Not status.LastRunEnd.HasValue Then
            Return False
        End If

        If status.LastRunEnd.Value.Date <> now.Date Then
            Return False
        End If

        If String.Equals(status.LastStatus, "Success", StringComparison.OrdinalIgnoreCase) Then
            Return True
        End If

        Return status.LastExitCode.HasValue AndAlso status.LastExitCode.Value = 0
    End Function

    Private Function IsFeedStartingOrRunning(feedName As String) As Boolean
        SyncLock _stateLock
            Return _runningFeeds.ContainsKey(feedName) OrElse _pendingStarts.Contains(feedName)
        End SyncLock
    End Function

    Private Function IsMlsKeyActive(mlsKey As String) As Boolean
        SyncLock _stateLock
            Return _activeMlsKeys.Contains(mlsKey)
        End SyncLock
    End Function

    Private Function GetRunningCount() As Integer
        SyncLock _stateLock
            Return _runningFeeds.Count
        End SyncLock
    End Function

    Private Function GetRetryAttempt(feedName As String) As Integer
        SyncLock _stateLock
            If _retryAttempts.ContainsKey(feedName) Then
                Return _retryAttempts(feedName)
            End If

            Return 0
        End SyncLock
    End Function

    Private Sub SetRetryAttempt(feedName As String, attempt As Integer)
        SyncLock _stateLock
            _retryAttempts(feedName) = attempt
        End SyncLock
    End Sub

    Private Shared Function CloneSnapshot(source As DashboardSnapshot) As DashboardSnapshot
        Dim copy As New DashboardSnapshot()
        copy.CurrentTime = source.CurrentTime
        copy.TotalFeeds = source.TotalFeeds
        copy.TotalEnabledFeeds = source.TotalEnabledFeeds
        copy.TotalDisabledFeeds = source.TotalDisabledFeeds
        copy.RunningCount = source.RunningCount
        copy.EligibleCount = source.EligibleCount
        copy.BlockedByMlsCount = source.BlockedByMlsCount
        copy.CompletedTodayCount = source.CompletedTodayCount
        copy.FailedTodayCount = source.FailedTodayCount
        copy.RunningFeeds.AddRange(source.RunningFeeds)
        copy.QueuedFeeds.AddRange(source.QueuedFeeds)
        copy.RecentCompletedFeeds.AddRange(source.RecentCompletedFeeds)
        Return copy
    End Function
End Class
