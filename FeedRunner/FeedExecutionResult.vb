Public Class FeedExecutionResult
    Public Property FeedName As String
    Public Property ExitCode As Integer?
    Public Property Status As String
    Public Property ErrorMessage As String
    Public Property StartTime As DateTime
    Public Property EndTime As DateTime
    Public Property LogFilePath As String

    Public ReadOnly Property Duration As TimeSpan
        Get
            Return EndTime - StartTime
        End Get
    End Property

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return String.Equals(Status, "Success", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property
End Class
