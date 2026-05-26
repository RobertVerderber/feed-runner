Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Mail
Imports System.Text
Imports System.Threading.Tasks

Public Class FeedFailureNotifier
    Private ReadOnly _settings As NotificationSettings
    Private ReadOnly _logger As Logger

    Public Sub New(settings As NotificationSettings, logger As Logger)
        _settings = settings
        _logger = logger
    End Sub

    Public Sub NotifyFeedFailure(
            feed As FeedConfig,
            result As FeedExecutionResult,
            consecutiveFailures As Integer,
            isFinalFailure As Boolean)
        If _settings Is Nothing OrElse Not _settings.IsConfigured() Then
            Return
        End If

        If Not isFinalFailure AndAlso Not _settings.NotifyOnRetry Then
            Return
        End If

        Task.Run(
            Sub()
                SendFailureEmail(feed, result, consecutiveFailures, isFinalFailure)
            End Sub)
    End Sub

    Private Sub SendFailureEmail(
            feed As FeedConfig,
            result As FeedExecutionResult,
            consecutiveFailures As Integer,
            isFinalFailure As Boolean)
        Try
            Using smtpClient As New SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
                smtpClient.Credentials = New NetworkCredential(_settings.SmtpUserName, _settings.SmtpPassword)
                smtpClient.EnableSsl = True

                Using mailMessage As New MailMessage()
                    mailMessage.From = New MailAddress(_settings.FromAddress, _settings.FromDisplayName)
                    For Each recipient As String In _settings.ToAddresses
                        If Not String.IsNullOrWhiteSpace(recipient) Then
                            mailMessage.To.Add(recipient.Trim())
                        End If
                    Next

                    If mailMessage.To.Count = 0 Then
                        _logger.LogError("Feed failure email not sent: no valid recipients configured.")
                        Return
                    End If

                    mailMessage.Subject = BuildSubject(feed, result, isFinalFailure)
                    mailMessage.Body = BuildBody(feed, result, consecutiveFailures, isFinalFailure)
                    mailMessage.IsBodyHtml = True

                    smtpClient.Send(mailMessage)
                End Using
            End Using

            _logger.Info("Feed failure email sent for '" & feed.FeedName & "'.")
        Catch ex As Exception
            _logger.LogError("Failed to send feed failure email for '" & feed.FeedName & "'.", ex)
        End Try
    End Sub

    Private Function BuildSubject(feed As FeedConfig, result As FeedExecutionResult, isFinalFailure As Boolean) As String
        Dim prefix As String = If(isFinalFailure, "Feed Failed", "Feed Failed (Retry Scheduled)")
        Return prefix & ": " & feed.FeedName & " [" & result.Status & "]"
    End Function

    Private Function BuildBody(
            feed As FeedConfig,
            result As FeedExecutionResult,
            consecutiveFailures As Integer,
            isFinalFailure As Boolean) As String
        Dim builder As New StringBuilder()
        builder.AppendLine("<html><body style=""font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;"">")
        builder.AppendLine("<h2 style=""color:#b00020;"">Feed Runner Failure</h2>")

        If isFinalFailure Then
            builder.AppendLine("<p>A feed failed after all retry attempts were exhausted.</p>")
        Else
            builder.AppendLine("<p>A feed failed and will be retried automatically.</p>")
        End If

        builder.AppendLine("<table cellpadding=""6"" cellspacing=""0"" style=""border-collapse:collapse;"">")
        AppendRow(builder, "Feed", feed.FeedName)
        AppendRow(builder, "MLS", feed.MlsKey)
        AppendRow(builder, "Status", result.Status)
        AppendRow(builder, "Exit Code", If(result.ExitCode.HasValue, result.ExitCode.Value.ToString(), "N/A"))
        AppendRow(builder, "Error", result.ErrorMessage)
        AppendRow(builder, "Started", result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"))
        AppendRow(builder, "Finished", result.EndTime.ToString("yyyy-MM-dd HH:mm:ss"))
        AppendRow(builder, "Duration", FormatDuration(result.Duration))
        AppendRow(builder, "Consecutive Failures", consecutiveFailures.ToString())
        AppendRow(builder, "Executable", feed.ExecutablePath)
        AppendRow(builder, "Arguments", feed.Arguments)
        AppendRow(builder, "Working Directory", feed.WorkingDirectory)
        AppendRow(builder, "Max Retries", feed.MaxRetries.ToString())
        AppendRow(builder, "Retry Delay (min)", feed.RetryDelayMinutes.ToString())

        If Not String.IsNullOrWhiteSpace(result.LogFilePath) Then
            AppendRow(builder, "Log File", result.LogFilePath)
        End If

        builder.AppendLine("</table>")

        Dim logExcerpt As String = GetLogExcerpt(result.LogFilePath)
        If Not String.IsNullOrWhiteSpace(logExcerpt) Then
            builder.AppendLine("<h3>Log Excerpt</h3>")
            builder.AppendLine("<pre style=""background:#f5f5f5;padding:12px;border:1px solid #ddd;white-space:pre-wrap;"">")
            builder.AppendLine(HtmlEncode(logExcerpt))
            builder.AppendLine("</pre>")
        End If

        builder.AppendLine("<p style=""color:#666;font-size:12px;"">Sent by Feed Runner at " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & ".</p>")
        builder.AppendLine("</body></html>")
        Return builder.ToString()
    End Function

    Private Shared Sub AppendRow(builder As StringBuilder, label As String, value As String)
        builder.AppendLine(
            "<tr><td style=""font-weight:bold;vertical-align:top;"">" &
            HtmlEncode(label) &
            "</td><td>" &
            HtmlEncode(If(value, String.Empty)) &
            "</td></tr>")
    End Sub

    Private Function GetLogExcerpt(logFilePath As String) As String
        If String.IsNullOrWhiteSpace(logFilePath) OrElse Not File.Exists(logFilePath) Then
            Return String.Empty
        End If

        Try
            Dim lines As String() = File.ReadAllLines(logFilePath)
            Dim maxLines As Integer = Math.Max(1, _settings.MaxLogLinesInBody)
            Dim startIndex As Integer = Math.Max(0, lines.Length - maxLines)
            Return String.Join(Environment.NewLine, lines.Skip(startIndex))
        Catch ex As Exception
            _logger.LogError("Failed to read feed log for failure email: " & logFilePath, ex)
            Return String.Empty
        End Try
    End Function

    Private Shared Function FormatDuration(duration As TimeSpan) As String
        If duration.TotalHours >= 1 Then
            Return CInt(Math.Floor(duration.TotalHours)).ToString() & "h " & duration.Minutes.ToString() & "m " & duration.Seconds.ToString() & "s"
        End If

        If duration.TotalMinutes >= 1 Then
            Return duration.Minutes.ToString() & "m " & duration.Seconds.ToString() & "s"
        End If

        Return duration.Seconds.ToString() & "s"
    End Function

    Private Shared Function HtmlEncode(value As String) As String
        If String.IsNullOrEmpty(value) Then
            Return String.Empty
        End If

        Return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("""", "&quot;")
    End Function
End Class
