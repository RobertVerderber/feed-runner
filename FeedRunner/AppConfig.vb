Public Class AppConfig
    Public Property RunnerSettings As RunnerSettings
    Public Property Feeds As List(Of FeedConfig)

    Public Sub New()
        RunnerSettings = New RunnerSettings()
        Feeds = New List(Of FeedConfig)()
    End Sub
End Class
