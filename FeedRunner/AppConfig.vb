Public Class AppConfig
    Public Property RunnerSettings As RunnerSettings
    Public Property NotificationSettings As NotificationSettings
    Public Property Feeds As List(Of FeedConfig)

    Public Sub New()
        RunnerSettings = New RunnerSettings()
        NotificationSettings = New NotificationSettings()
        Feeds = New List(Of FeedConfig)()
    End Sub
End Class
