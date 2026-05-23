Public Enum FeedConsoleWindowMode
    Always
    OnFailure
    Never
End Enum

Public Module FeedConsoleWindowModeHelper
    Public Function ParseMode(value As String) As FeedConsoleWindowMode
        If String.IsNullOrWhiteSpace(value) Then
            Return FeedConsoleWindowMode.OnFailure
        End If

        Select Case value.Trim().ToLowerInvariant()
            Case "always", "show", "visible", "all"
                Return FeedConsoleWindowMode.Always
            Case "onfailure", "on-failure", "failure", "errors", "error"
                Return FeedConsoleWindowMode.OnFailure
            Case "never", "hidden", "hide", "none", "off"
                Return FeedConsoleWindowMode.Never
            Case Else
                Return FeedConsoleWindowMode.OnFailure
        End Select
    End Function

    Public Function ResolveMode(settings As RunnerSettings) As FeedConsoleWindowMode
        If settings Is Nothing Then
            Return FeedConsoleWindowMode.OnFailure
        End If

        If Not String.IsNullOrWhiteSpace(settings.FeedConsoleWindowMode) Then
            Return ParseMode(settings.FeedConsoleWindowMode)
        End If

        If settings.ShowFeedConsoleWindows Then
            Return FeedConsoleWindowMode.Always
        End If

        If settings.ShowFeedConsoleWindowOnFailure Then
            Return FeedConsoleWindowMode.OnFailure
        End If

        Return FeedConsoleWindowMode.Never
    End Function
End Module
