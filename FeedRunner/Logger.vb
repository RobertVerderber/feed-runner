Imports System.IO
Imports System.Text

Public Class Logger
    Private ReadOnly _logFolder As String
    Private ReadOnly _syncRoot As New Object()

    Public Sub New(logFolder As String)
        If String.IsNullOrWhiteSpace(logFolder) Then
            _logFolder = "logs"
        Else
            _logFolder = logFolder
        End If

        Directory.CreateDirectory(_logFolder)
    End Sub

    Public Sub Info(message As String)
        WriteLog("INFO", message)
    End Sub

    Public Sub LogError(message As String)
        WriteLog("ERROR", message)
    End Sub

    Public Sub LogError(message As String, ex As Exception)
        Dim details As String = message
        If ex IsNot Nothing Then
            details = details & " | " & ex.Message
            If ex.StackTrace IsNot Nothing Then
                details = details & Environment.NewLine & ex.StackTrace
            End If
        End If

        WriteLog("ERROR", details)
    End Sub

    Public Function ClearLogFolder() As Integer
        Dim deletedCount As Integer = 0

        SyncLock _syncRoot
            Try
                Dim logRoot As String = Path.GetFullPath(_logFolder)
                If Not Directory.Exists(logRoot) Then
                    Return 0
                End If

                Dim files As String() = Directory.GetFiles(logRoot, "*.*", SearchOption.AllDirectories)
                For Each filePath As String In files
                    Try
                        File.Delete(filePath)
                        deletedCount += 1
                    Catch
                    End Try
                Next
            Catch ex As Exception
                LogError("Failed to clear log folder: " & _logFolder, ex)
            End Try
        End SyncLock

        Return deletedCount
    End Function

    Private Sub WriteLog(level As String, message As String)
        SyncLock _syncRoot
            Try
                Dim logFileName As String = String.Format("runner-{0:yyyy-MM-dd}.log", DateTime.Now)
                Dim logPath As String = Path.Combine(_logFolder, logFileName)
                Dim line As String = String.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, level, message)

                File.AppendAllText(logPath, line & Environment.NewLine, Encoding.UTF8)
            Catch
            End Try
        End SyncLock
    End Sub
End Class
