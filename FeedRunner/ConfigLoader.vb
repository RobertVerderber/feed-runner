Imports System.IO
Imports Newtonsoft.Json

Public Class ConfigLoader
    Public Shared Function Load(configPath As String, logger As Logger) As AppConfig
        Dim config As New AppConfig()

        If String.IsNullOrWhiteSpace(configPath) OrElse Not File.Exists(configPath) Then
            logger.LogError("Configuration file not found: " & If(configPath, "(null)"))
            Return config
        End If

        Try
            Dim json As String = File.ReadAllText(configPath)
            Dim loaded As AppConfig = JsonConvert.DeserializeObject(Of AppConfig)(json)

            If loaded Is Nothing Then
                logger.LogError("Configuration file deserialized to null: " & configPath)
                Return config
            End If

            If loaded.RunnerSettings Is Nothing Then
                loaded.RunnerSettings = New RunnerSettings()
            End If

            If loaded.NotificationSettings Is Nothing Then
                loaded.NotificationSettings = New NotificationSettings()
            End If

            If loaded.NotificationSettings.ToAddresses Is Nothing Then
                loaded.NotificationSettings.ToAddresses = New List(Of String)()
            End If

            If loaded.Feeds Is Nothing Then
                loaded.Feeds = New List(Of FeedConfig)()
            End If

            Dim validFeeds As New List(Of FeedConfig)()
            For Each feed As FeedConfig In loaded.Feeds
                If feed Is Nothing Then
                    Continue For
                End If

                If String.IsNullOrWhiteSpace(feed.FeedName) Then
                    logger.LogError("Skipping feed with empty FeedName in configuration.")
                    Continue For
                End If

                If String.IsNullOrWhiteSpace(feed.ExecutablePath) Then
                    logger.LogError("Skipping feed '" & feed.FeedName & "' with empty ExecutablePath.")
                    Continue For
                End If

                If String.IsNullOrWhiteSpace(feed.MlsKey) Then
                    feed.MlsKey = "UNKNOWN"
                End If

                validFeeds.Add(feed)
            Next

            loaded.Feeds = validFeeds
            Return loaded
        Catch ex As JsonException
            logger.LogError("Malformed configuration file: " & configPath, ex)
            Return config
        Catch ex As Exception
            logger.LogError("Failed to load configuration file: " & configPath, ex)
            Return config
        End Try
    End Function
End Class
