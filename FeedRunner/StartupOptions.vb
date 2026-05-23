Public Class StartupOptions
    Public Property ResetStatus As Boolean

    Public Shared Function Parse(args As String()) As StartupOptions
        Dim options As New StartupOptions()

        If args Is Nothing Then
            Return options
        End If

        For Each arg As String In args
            If String.IsNullOrWhiteSpace(arg) Then
                Continue For
            End If

            Dim normalized As String = NormalizeArgument(arg)

            If String.Equals(normalized, "reset", StringComparison.OrdinalIgnoreCase) Then
                options.ResetStatus = True
            End If
        Next

        Return options
    End Function

    Private Shared Function NormalizeArgument(arg As String) As String
        Dim normalized As String = arg.Trim()

        While normalized.Length > 0 AndAlso (normalized.StartsWith("-", StringComparison.Ordinal) OrElse normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.Substring(1)
        End While

        Return normalized
    End Function
End Class
