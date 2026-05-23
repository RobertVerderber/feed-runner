Public Class FeedConfig
    Public Property FeedName As String
    Public Property ExecutablePath As String
    Public Property Arguments As String
    Public Property WorkingDirectory As String
    Public Property MlsKey As String
    Public Property Enabled As Boolean = True
    Public Property Priority As Integer = 1
    Public Property RunEveryMinutes As Integer = 60
    Public Property StartTime As String = "09:00"
    Public Property EndTime As String = "20:00"
    Public Property TimeoutMinutes As Integer = 120
    Public Property MaxRetries As Integer = 0
    Public Property RetryDelayMinutes As Integer = 10
    Public Property RunOnceDaily As Boolean = False

    Public Function GetStartTimeOfDay() As TimeSpan
        Return ParseTimeOfDay(StartTime, New TimeSpan(9, 0, 0))
    End Function

    Public Function GetEndTimeOfDay() As TimeSpan
        Return ParseTimeOfDay(EndTime, New TimeSpan(20, 0, 0))
    End Function

    Public Function IsWithinTimeWindow(atTime As DateTime) As Boolean
        Dim current As TimeSpan = atTime.TimeOfDay
        Dim startTime As TimeSpan = GetStartTimeOfDay()
        Dim endTime As TimeSpan = GetEndTimeOfDay()

        If startTime <= endTime Then
            Return current >= startTime AndAlso current <= endTime
        End If

        Return current >= startTime OrElse current <= endTime
    End Function

    Private Shared Function ParseTimeOfDay(value As String, defaultValue As TimeSpan) As TimeSpan
        If String.IsNullOrWhiteSpace(value) Then
            Return defaultValue
        End If

        Dim parsed As TimeSpan
        If TimeSpan.TryParse(value, parsed) Then
            Return parsed
        End If

        Return defaultValue
    End Function

    Public Function GetNextRunTimeAfterSuccess(fromTime As DateTime) As DateTime
        If RunOnceDaily Then
            Return GetNextDayStartTime(fromTime)
        End If

        Return fromTime.AddMinutes(Math.Max(1, RunEveryMinutes))
    End Function

    Public Function GetNextRunTimeAfterFailure(fromTime As DateTime) As DateTime
        If RunOnceDaily Then
            Return GetNextDayStartTime(fromTime)
        End If

        Return fromTime.AddMinutes(Math.Max(1, RunEveryMinutes))
    End Function

    Private Function GetNextDayStartTime(fromTime As DateTime) As DateTime
        Return fromTime.Date.AddDays(1).Add(GetStartTimeOfDay())
    End Function
End Class
