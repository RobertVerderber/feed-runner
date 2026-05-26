Public Class NotificationSettings
    Public Property Enabled As Boolean = False
    Public Property NotifyOnRetry As Boolean = False
    Public Property SmtpHost As String = "in-v3.mailjet.com"
    Public Property SmtpPort As Integer = 587
    Public Property SmtpUserName As String
    Public Property SmtpPassword As String
    Public Property FromAddress As String
    Public Property FromDisplayName As String
    Public Property ToAddresses As List(Of String)
    Public Property MaxLogLinesInBody As Integer = 100

    Public Sub New()
        ToAddresses = New List(Of String)()
    End Sub

    Public Function IsConfigured() As Boolean
        If Not Enabled Then
            Return False
        End If

        If String.IsNullOrWhiteSpace(SmtpHost) Then
            Return False
        End If

        If String.IsNullOrWhiteSpace(SmtpUserName) OrElse String.IsNullOrWhiteSpace(SmtpPassword) Then
            Return False
        End If

        If String.IsNullOrWhiteSpace(FromAddress) Then
            Return False
        End If

        If ToAddresses Is Nothing OrElse ToAddresses.Count = 0 Then
            Return False
        End If

        Return True
    End Function
End Class
