Imports System.Linq

Public Class FeedConfig
    Public Property FeedName As String
    Public Property ExecutablePath As String
    Public Property Arguments As String
    Public Property WorkingDirectory As String
    Public Property MlsKey As String
    Public Property Enabled As Boolean = True
    Public Property Priority As Integer = 1
    Public Property RunEveryMinutes As Integer = 60
    Public Property WeekendRunEveryMinutes As Integer? = 180
    Public Property StartTime As String = "09:00"
    Public Property EndTime As String = "20:00"
    Public Property TimeoutMinutes As Integer = 120
    Public Property MaxRetries As Integer = 0
    Public Property RetryDelayMinutes As Integer = 10
    Public Property RunOnceDaily As Boolean = False

    ' When true, the feed does not run on Saturday or Sunday.
    Public Property SkipWeekends As Boolean = False

    ' Optional list of days to skip, e.g. ["Saturday", "Sunday", "Monday"].
    Public Property SkipDaysOfWeek As List(Of String)

    Public Function GetStartTimeOfDay() As TimeSpan
        Return ParseTimeOfDay(StartTime, New TimeSpan(9, 0, 0))
    End Function

    Public Function GetEndTimeOfDay() As TimeSpan
        Return ParseTimeOfDay(EndTime, New TimeSpan(20, 0, 0))
    End Function

    Public Function IsWeekend(atTime As DateTime) As Boolean
        Return atTime.DayOfWeek = DayOfWeek.Saturday OrElse atTime.DayOfWeek = DayOfWeek.Sunday
    End Function

    Public Function IsAllowedOnDay(atTime As DateTime) As Boolean
        If SkipWeekends AndAlso IsWeekend(atTime) Then
            Return False
        End If

        If SkipDaysOfWeek Is Nothing OrElse SkipDaysOfWeek.Count = 0 Then
            Return True
        End If

        Dim dayName As String = atTime.DayOfWeek.ToString()
        For Each skipDay As String In SkipDaysOfWeek
            If String.IsNullOrWhiteSpace(skipDay) Then
                Continue For
            End If

            If String.Equals(skipDay.Trim(), dayName, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
        Next

        Return True
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

    Public Function IsEligibleToRun(atTime As DateTime) As Boolean
        Return IsAllowedOnDay(atTime) AndAlso IsWithinTimeWindow(atTime)
    End Function

    Public Function GetEffectiveRunEveryMinutes(atTime As DateTime) As Integer
        If IsWeekend(atTime) AndAlso WeekendRunEveryMinutes.HasValue AndAlso WeekendRunEveryMinutes.Value > 0 Then
            Return WeekendRunEveryMinutes.Value
        End If

        Return Math.Max(1, RunEveryMinutes)
    End Function

    Public Function GetNextRunTimeAfterSuccess(fromTime As DateTime) As DateTime
        If RunOnceDaily Then
            Return GetNextAllowedDayStartTime(fromTime)
        End If

        Dim nextTime As DateTime = fromTime.AddMinutes(GetEffectiveRunEveryMinutes(fromTime))
        Return NormalizeToAllowedSchedule(nextTime)
    End Function

    Public Function GetNextRunTimeAfterFailure(fromTime As DateTime) As DateTime
        If RunOnceDaily Then
            Return GetNextAllowedDayStartTime(fromTime)
        End If

        Dim nextTime As DateTime = fromTime.AddMinutes(GetEffectiveRunEveryMinutes(fromTime))
        Return NormalizeToAllowedSchedule(nextTime)
    End Function

    Private Function GetNextAllowedDayStartTime(fromTime As DateTime) As DateTime
        Dim candidate As DateTime = fromTime.Date.AddDays(1).Add(GetStartTimeOfDay())
        Return AdvanceToAllowedDayStart(candidate)
    End Function

    Private Function AdvanceToAllowedDayStart(candidate As DateTime) As DateTime
        Dim adjusted As DateTime = candidate.Date
        Dim safety As Integer = 0

        While safety < 366
            If IsAllowedOnDay(adjusted) Then
                Return adjusted.Add(GetStartTimeOfDay())
            End If

            adjusted = adjusted.AddDays(1)
            safety += 1
        End While

        Return candidate
    End Function

    Private Function NormalizeToAllowedSchedule(candidate As DateTime) As DateTime
        Dim adjusted As DateTime = candidate
        Dim safety As Integer = 0

        While safety < 366
            If Not IsAllowedOnDay(adjusted) Then
                adjusted = adjusted.Date.AddDays(1).Add(GetStartTimeOfDay())
                safety += 1
                Continue While
            End If

            If IsWithinTimeWindow(adjusted) Then
                Return adjusted
            End If

            If adjusted.TimeOfDay < GetStartTimeOfDay() Then
                Return adjusted.Date.Add(GetStartTimeOfDay())
            End If

            adjusted = adjusted.Date.AddDays(1).Add(GetStartTimeOfDay())
            safety += 1
        End While

        Return candidate
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
End Class
