Imports System.Runtime.InteropServices

Public Module ConsoleWindowHelper
    Private ReadOnly HWND_TOPMOST As New IntPtr(-1)
    Private ReadOnly HWND_NOTOPMOST As New IntPtr(-2)

    Private Const SWP_NOMOVE As Integer = &H2
    Private Const SWP_NOSIZE As Integer = &H1
    Private Const SWP_SHOWWINDOW As Integer = &H40

    <DllImport("kernel32.dll")>
    Private Function GetConsoleWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SetWindowPos(
            hWnd As IntPtr,
            hWndInsertAfter As IntPtr,
            x As Integer,
            y As Integer,
            cx As Integer,
            cy As Integer,
            uFlags As Integer) As Boolean
    End Function

    Public Function SetAlwaysOnTop(enabled As Boolean) As Boolean
        Dim handle As IntPtr = GetConsoleWindow()
        If handle = IntPtr.Zero Then
            Return False
        End If

        Dim zOrder As IntPtr = HWND_NOTOPMOST
        If enabled Then
            zOrder = HWND_TOPMOST
        End If

        Return SetWindowPos(handle, zOrder, 0, 0, 0, 0, SWP_NOMOVE Or SWP_NOSIZE Or SWP_SHOWWINDOW)
    End Function
End Module
