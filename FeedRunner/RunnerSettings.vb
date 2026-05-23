Public Class RunnerSettings
    Public Property MaxConcurrentFeeds As Integer = 5
    Public Property StatusFilePath As String = "feed-status.json"
    Public Property LogFolder As String = "logs"
    Public Property RefreshSeconds As Integer = 5

    ' Always      = show a live console window for every feed while it runs
    ' OnFailure   = hide during run, open a window only when a feed fails
    ' Never       = no console windows, logs only
    Public Property FeedConsoleWindowMode As String = "OnFailure"

    ' Legacy settings. FeedConsoleWindowMode takes precedence when set.
    Public Property ShowFeedConsoleWindows As Boolean = False
    Public Property ShowFeedConsoleWindowOnFailure As Boolean = True
    Public Property KeepFeedWindowOpenOnExit As Boolean = True
    Public Property AlwaysOnTop As Boolean = False
End Class
