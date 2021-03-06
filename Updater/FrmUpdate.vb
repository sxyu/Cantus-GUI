﻿Imports System.Net

Namespace UI.Updater

    '' <summary>
    '' Very basic Google drive based updater for Cantus
    '' </summary>
    Public Class FrmUpdate
        Delegate Sub UpdateMessagesSafe(ByVal length As Long, ByVal position As Integer, ByVal percent As Integer, ByVal speed As Double)
        Delegate Sub DownloadCompleteSafe(ByVal cancelled As Boolean)

        ' urls: hosted on GitHub
        Private Const VERSION_URL As String = "https://raw.githubusercontent.com/sxyu/Cantus-Core/master/meta/ver"
        Private Const MANIFEST_URL As String = "https://raw.githubusercontent.com/sxyu/Cantus-Core/master/meta/manifest"

        Dim _saveLocation As String = ""
        Dim _toDownload As String = ""
        Dim _newVersion As String = ""
        Dim _manifestFile() As String
        Dim _downloadCount As Integer = 0
        Dim _reRun As Boolean = True

        Public Sub DownloadComplete(ByVal cancelled As Boolean)
            Me.BtnCancel.Enabled = False
            If cancelled Then
                Application.Exit()
            End If

            Me.pb.Value = 0
            Me.LbTSize.Text = ""
            Me.LbSpeed.Text = ""

            _downloadCount += 1
            If _downloadCount >= _manifestFile.Length Then
                LbStep.Text = "Preparing to start installer..."
                Me.LbDlSize.Text = "Finishing Up..."
                LbFile.Text = ""

                _reRun = False
                Try
                    Process.Start(_saveLocation)
                Catch ex As Exception
                    MsgBox("installer failed to start!", MsgBoxStyle.Critical, "Error launching Cantus installer")
                End Try
                Application.Exit()

            Else
                Me.LbDlSize.Text = "Sending Request..."
                _toDownload = _manifestFile(_downloadCount).Trim.Replace("%20", " ")
                Dim filename As String = IO.Path.GetFileName(_toDownload)
                LbFile.Text = filename
                LbStep.Text = _downloadCount + 1 & " of " & _manifestFile.Length
                _saveLocation = IO.Path.GetTempFileName() + ".exe"
                _reRun = True
            End If
        End Sub

        Public Sub UpdateMessages(ByVal length As Long, ByVal position As Integer, ByVal percent As Integer, ByVal speed As Double)
            Me.LbTSize.Text = "File Size: " & Math.Round((length / 1024), 2) & " KB"

            Me.LbDlSize.Text = "Downloaded " & Math.Round((position / 1024), 2) & " KB (" & Me.pb.Value & "%)"

            If speed = -1 Then
                Me.LbSpeed.Text = "Speed: calculating..."
            Else
                If speed < 750 Then
                    Me.LbSpeed.Text = "Speed: " & speed & " B/s"
                ElseIf speed < 750000 Then
                    Me.LbSpeed.Text = "Speed: " & Math.Round((speed / 1024), 1) & " KB/s"
                Else
                    Me.LbSpeed.Text = "Speed: " & Math.Round((speed / 1048576), 1) & " MB/s"
                End If
            End If
            Me.pb.Value = percent
        End Sub

        Private Sub frmUpdate_Load(sender As Object, e As EventArgs) Handles MyBase.Load
            Me.Icon = My.Resources.Update
            Try
                If Not My.Computer.Network.IsAvailable Then
                    MsgBox("We cannot update Cantus without an internet connection. Please try again later.",
               MsgBoxStyle.Critical Or MsgBoxStyle.MsgBoxSetForeground, "Network Connection Unavailable")
                    Application.Exit()
                Else
                    Using wc As New System.Net.WebClient()
                        _newVersion = wc.DownloadString(VERSION_URL)
                        LbVer.Text = "Installing Update " & _newVersion
                        _manifestFile = wc.DownloadString(MANIFEST_URL).Split(ControlChars.Lf)
                    End Using

                    If _manifestFile.Length > 0 Then
                        _toDownload = _manifestFile(0).Trim.Replace("%20", " ").Trim()
                        Dim filename As String = IO.Path.GetFileName(_toDownload)
                        LbFile.Text = filename
                        LbStep.Text = "1 of " & _manifestFile.Length
                        _saveLocation = IO.Path.GetTempFileName() + ".exe"
                        bw.RunWorkerAsync()
                    Else
                        MsgBox("Updater Error: the update manifest is corrupted or failed to download. If this persists, please contact Mr. Yu.",
                            MsgBoxStyle.Exclamation, "Error")
                        Application.Exit()
                    End If
                End If
            Catch ex As Exception
                MsgBox("Updater Error: An unknown error has occurred. Please send the following information to the Mr. Yu: " _
                   & vbCrLf & ex.Message,
                            MsgBoxStyle.Exclamation, "Unknown Error")
                Application.Exit() ' on error quit
            End Try
        End Sub

        Private Sub logo_Paint(sender As Object, e As PaintEventArgs) Handles logo.Paint
            Using whiteBr As New SolidBrush(Color.White)
                Using clrBr As New SolidBrush(Color.FromArgb(30, 120, 63))
                    e.Graphics.FillRectangle(clrBr, 0, 0, e.ClipRectangle.Width, e.ClipRectangle.Height)
                    e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAlias
                    e.Graphics.DrawImage(My.Resources.Cantus.ToBitmap, 0, 0, logo.Width, logo.Height)
                End Using
            End Using
        End Sub

        Private Sub bw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles bw.DoWork
            'Creating the request and getting the response
            Dim resp As WebResponse
            Dim req As WebRequest
            Try 'Checks if the file exist

                req = WebRequest.Create(Me._toDownload.Trim())
                resp = req.GetResponse

            Catch ex As Exception
                MessageBox.Show("An error occurred while downloading the newer version of Cantus.")

                Dim cancelDelegate As New DownloadCompleteSafe(AddressOf DownloadComplete)
                Me.Invoke(cancelDelegate, True)
                Exit Sub
            End Try

            Dim length As Long = resp.ContentLength ' Size of the response (in bytes)

            Dim safedelegate As New UpdateMessagesSafe(AddressOf UpdateMessages)
            Me.Invoke(safedelegate, length, 0, 0, 0) ' Initialize

            Dim writeStream As New IO.FileStream(Me._saveLocation, IO.FileMode.Create, IO.FileAccess.Write)

            'Replacement for Stream.Position (webResponse stream doesn't support seek)
            Dim nRead As Integer

            'To calculate the download speed
            Dim speedtimer As New Stopwatch
            Dim currentspeed As Double = -1
            Dim readings As Integer = 0

            Do
                If bw.CancellationPending Then 'If user aborts download
                    Exit Do
                End If

                speedtimer.Start()

                Dim readBytes(4095) As Byte
                Dim bytesread As Integer = resp.GetResponseStream.Read(readBytes, 0, 4096)

                nRead += bytesread
                Dim percent As Short = CShort((nRead * 100) / length)

                Me.Invoke(safedelegate, length, nRead, percent, currentspeed)

                If bytesread = 0 Then
                    Exit Do
                End If

                writeStream.Write(readBytes, 0, bytesread)

                speedtimer.Stop()

                readings += 1
                If readings >= 5 Then
                    ' For increased precision, 
                    ' the speed is calculated only every five cycles
                    currentspeed = 20480 / (speedtimer.ElapsedMilliseconds / 1000)
                    speedtimer.Reset()
                    readings = 0
                End If
            Loop Until Not _reRun

            'Close the streams
            resp.GetResponseStream.Close()
            writeStream.Close()

            If Me.bw.CancellationPending Then

                IO.File.Delete(Me._saveLocation)

                Dim cancelDelegate As New DownloadCompleteSafe(AddressOf DownloadComplete)
                Me.Invoke(cancelDelegate, True)

                Exit Sub

            End If

            Dim completeDelegate As New DownloadCompleteSafe(AddressOf DownloadComplete)
            Me.Invoke(completeDelegate, False)
        End Sub

        Private Sub BtnCancel_Click(sender As Object, e As EventArgs) Handles BtnCancel.Click
            bw.CancelAsync()
        End Sub

        Private Sub bw_RunWorkerCompleted(sender As Object, e As System.ComponentModel.RunWorkerCompletedEventArgs) Handles bw.RunWorkerCompleted
            If _reRun Then
                bw.RunWorkerAsync()
            End If
        End Sub

        Dim moving As Boolean = False
        Dim prevPt As New Point
        Private Sub LbTSize_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown, pb.MouseDown, logo.MouseDown,
        LbVer.MouseDown, LbTSize.MouseDown, LbTitle.MouseDown, LbStep.MouseDown, LbSpeed.MouseDown, LbFile.MouseDown, LbDlSize.MouseDown
            moving = True
            prevPt = e.Location
        End Sub

        Private Sub LbTSize_MouseMove(sender As Object, e As MouseEventArgs) Handles MyBase.MouseMove, pb.MouseMove, logo.MouseMove,
        LbVer.MouseMove, LbTSize.MouseMove, LbTitle.MouseMove, LbStep.MouseMove, LbSpeed.MouseMove, LbFile.MouseMove, LbDlSize.MouseMove
            If moving Then
                Me.Location = New Point(Me.Left + e.X - prevPt.X, Me.Top + e.Y - prevPt.Y)
                Me.Invalidate()
            End If
        End Sub

        Private Sub LbTSize_MouseUp(sender As Object, e As MouseEventArgs) Handles MyBase.MouseUp, pb.MouseUp, logo.MouseUp,
        LbVer.MouseUp, LbTSize.MouseUp, LbTitle.MouseUp, LbStep.MouseUp, LbSpeed.MouseUp, LbFile.MouseUp, LbDlSize.MouseUp
            moving = False
        End Sub
    End Class
End Namespace
