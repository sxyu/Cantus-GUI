﻿Imports System.ComponentModel
Imports System.Drawing.Drawing2D
Imports System.Threading
Imports Cantus.Core.CantusEvaluator
Imports ScintillaNET

Namespace UI.Graphing
    Public Class GraphingSystem
        Private Const WID As Integer = 4096
        Private Const HIGH As Integer = 2160
        Private Const PRECISION As Integer = 4
        Private Const SCALESPEED As Double = 1 / 800
        Private Const MINSCALE As Double = 0.0000000001
        Private Const MAXSCALE As Double = 100000000.0

        Private _ended As Boolean = False
        Private _ctl As ScintillaForCantus.ScintillaController

        Public Structure Coord
            Dim X As Double
            Dim Y As Double
            Public Sub New(x As Decimal, y As Decimal)
                Me.X = x
                Me.Y = y
            End Sub
            Public Sub New(x As Double, y As Double)
                Me.X = x
                Me.Y = y
            End Sub
        End Structure

        Dim _loc As New Coord(0, 0)
        Dim _center As New Coord(0, 0)
        Dim _centerBackup As New Coord(0, 0)

        Dim _scale As New Coord(1, 1)
        Dim _scaleBackup As New Coord(1, 1)


        Dim _functions As New List(Of String)
        Dim _functiontype As New List(Of FunctionType)
        Dim _curfn As Integer = 0

        Dim _buffer As Bitmap
        Dim _tmpbuffer As Bitmap

        Dim _traceOn As Boolean = False
        Dim _eval As Core.CantusEvaluator
        Dim _eval2 As Core.CantusEvaluator

        Dim SCREENFACT As Integer = 30

        Public Enum FunctionType
            Cartesian = 1
            Inverse
            Parametric
            Polar
            Differential
            OriginRay
            Custom
        End Enum

        Private Sub FrmGraph_Load(sender As Object, e As EventArgs) Handles MyBase.Load
            ' set icon, styles, etc.
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, True)
            Me.Split.SplitterDistance = Me.Height - My.Settings.GraphSplitter

            ' position
            Me.Top = Screen.PrimaryScreen.WorkingArea.Top
            Me.Left = Screen.PrimaryScreen.WorkingArea.Left
            Me.Height = Screen.PrimaryScreen.WorkingArea.Height
            Me.Width = Screen.PrimaryScreen.WorkingArea.Width - FrmEditor.Width

            ' initialize buffers
            _buffer = New Bitmap(WID, HIGH)
            _tmpbuffer = New Bitmap(WID, HIGH)
            Dim g As Graphics = Graphics.FromImage(_buffer)
            g.Clear(Color.Transparent)

            _eval = Globals.RootEvaluator ' use global evaluator

            _curfn = 0

            ' function label color
            UpdateFnLabelColor()

            _functions.Add("")
            _functiontype.Add(FunctionType.Cartesian)

            Tb.Select()
            If Tb.Text = "" Then
                ResetFunc()
            Else
                _functions(0) = Tb.Text
                UpdateFunc()
            End If

            _ctl = New ScintillaForCantus.ScintillaController(Tb, RootEvaluator)
            Dim margin As Margin = Tb.Margins(0)
            margin.Width = 0

            TmrStart.Start()
        End Sub

        Private Sub TmrStart_Tick(sender As Object, e As EventArgs) Handles TmrStart.Tick
            TmrStart.Stop()
            DrawWorker.RunWorkerAsync()
        End Sub

        Private Sub SetX(x As Object, Optional useEval2 As Boolean = False)
            If useEval2 Then
                _eval2.SetVariable("x"c, ObjectTypes.DetectType(x))
            Else
                _eval.SetVariable("x"c, ObjectTypes.DetectType(x))
            End If
        End Sub

        Function GetX(Optional useEval2 As Boolean = False) As Object
            If useEval2 Then
                If Not _eval2.HasVariable("x") Then Return Double.NaN
                Return _eval2.GetVariableObj("x"c)
            Else
                If Not _eval.HasVariable("x") Then Return Double.NaN
                Return _eval.GetVariableObj("x"c)
            End If
        End Function

        Private Sub SetY(y As Object, Optional useEval2 As Boolean = False)
            If useEval2 Then
                _eval2.SetVariable("y"c, ObjectTypes.DetectType(y))
            Else
                _eval.SetVariable("y"c, ObjectTypes.DetectType(y))
            End If
        End Sub

        Function GetY(Optional useEval2 As Boolean = False) As Object
            If useEval2 Then
                If Not _eval2.HasVariable("y") Then Return Double.NaN
                Return _eval2.GetVariableObj("y"c)
            Else
                If Not _eval.HasVariable("y") Then Return Double.NaN
                Return _eval.GetVariableObj("y"c)
            End If
        End Function

        Private Sub SetT(t As Object, Optional useEval2 As Boolean = False)
            If useEval2 Then
                _eval2.SetVariable("t"c, ObjectTypes.DetectType(t))
                _eval2.SetVariable("θ"c, ObjectTypes.DetectType(t))
            Else
                _eval.SetVariable("t"c, ObjectTypes.DetectType(t))
                _eval.SetVariable("θ"c, ObjectTypes.DetectType(t))
            End If
        End Sub

        Function GetT(Optional useEval2 As Boolean = False) As Object
            If useEval2 Then
                If Not _eval2.HasVariable("t") Then Return Double.NaN
                Return _eval2.GetVariableObj("t"c)
            Else
                If Not _eval.HasVariable("t") Then Return Double.NaN
                Return _eval.GetVariableObj("t"c)
            End If
        End Function

        Private Sub DeltaX(dx As Double, Optional useEval2 As Boolean = False)
            If useEval2 Then
                _eval2.SetVariable("x"c, New ObjectTypes.Number(CDbl(GetX(useEval2)) + dx))
            Else
                _eval.SetVariable("x"c, New ObjectTypes.Number(CDbl(GetX(useEval2)) + dx))
            End If
        End Sub

        Private Sub DeltaY(dy As Double, Optional useEval2 As Boolean = False)
            If useEval2 Then
                _eval2.SetVariable("y"c, New ObjectTypes.Number(CDbl(GetY(useEval2)) + dy))
            Else
                _eval.SetVariable("y"c, New ObjectTypes.Number(CDbl(GetY(useEval2)) + dy))
            End If
        End Sub

        Private Sub DeltaT(dt As Double, Optional useEval2 As Boolean = False)
            If useEval2 Then
                SetT(CDbl(GetT(useEval2)) + dt, useEval2)
            Else
                SetT(CDbl(GetT(useEval2)) + dt, useEval2)
            End If
        End Sub

        ' absolute point converters, first c = convert
        '                            s = screen
        '                            c = canvas
        '                            f = function
        Private Function cscx(x As Double, Optional useBackup As Boolean = False) As Double
            Return x - _loc.X + If(useBackup, _centerBackup.X, _center.X) + WID / 2 - Canvas.Width / 2
        End Function

        Private Function cscy(y As Double, Optional useBackup As Boolean = False) As Double
            Return y + _loc.Y - If(useBackup, _centerBackup.Y, _center.Y) + HIGH / 2 - Canvas.Height / 2
        End Function

        Private Function ccsx(x As Double, Optional useBackup As Boolean = False) As Double
            Return x + _loc.X - If(useBackup, _centerBackup.X, _center.X) - WID / 2 + Canvas.Width / 2
        End Function

        Private Function ccsy(y As Double, Optional useBackup As Boolean = False) As Double
            Return y - _loc.Y + If(useBackup, _centerBackup.Y, _center.Y) - HIGH / 2 + Canvas.Height / 2
        End Function

        Private Function cfcx(x As Double, Optional useBackup As Boolean = False) As Double
            Return x * (SCREENFACT / If(useBackup, _scaleBackup.X, _scale.X)) + If(useBackup, _centerBackup.X, _center.X) + WID / 2
        End Function

        Private Function cfcy(y As Double, Optional useBackup As Boolean = False) As Double
            Return (HIGH / 2 - y * SCREENFACT / If(useBackup, _scaleBackup.Y, _scale.Y)) - If(useBackup, _centerBackup.Y, _center.Y)
        End Function

        Private Function ccfx(x As Double, Optional useBackup As Boolean = False) As Double
            Return (x - If(useBackup, _centerBackup.X, _center.X) - WID / 2) / (SCREENFACT / If(useBackup, _scaleBackup.X, _scale.X))
        End Function

        Private Function ccfy(y As Double, Optional useBackup As Boolean = False) As Double
            Return (y + If(useBackup, _centerBackup.Y, _center.Y) - HIGH / 2) / (-SCREENFACT / If(useBackup, _scaleBackup.Y, _scale.Y))
        End Function

        Private Function cfsx(x As Double, Optional useBackup As Boolean = False) As Double
            Return ccsx(cfcx(x), useBackup)
        End Function

        Private Function cfsy(y As Double, Optional useBackup As Boolean = False) As Double
            Return ccsy(cfcy(y), useBackup)
        End Function

        Private Function csfx(x As Double, Optional useBackup As Boolean = False) As Double
            Return ccfx(cscx(x), useBackup)
        End Function

        Private Function csfy(y As Double, Optional useBackup As Boolean = False) As Double
            Return ccfy(cscy(y), useBackup)
        End Function

        ' distance converters
        Private Function cdfcx(x As Double) As Double
            Return x * (SCREENFACT / _scale.X)
        End Function

        Private Function cdfcy(y As Double) As Double
            Return y * (SCREENFACT / _scale.Y)
        End Function

        Private Function cdcfx(x As Double) As Double
            Return x / (SCREENFACT / _scale.X)
        End Function

        Private Function cdcfy(y As Double) As Double
            Return y / (SCREENFACT / _scale.Y)
        End Function

        Private Function NameFromFuncId(fnid As Integer) As String
            Dim fnp As String = fnid.ToString()
            If fnp = "0" Then fnp = ""
            Select Case _functiontype(fnid)
                Case FunctionType.Parametric
                    Return "<x" & fnp & "(t), y" & fnp & "(t)>"
                Case FunctionType.Polar
                    Return "r" & fnp & "(ϴ)"
                Case FunctionType.Inverse
                    Return "x" & fnp
                Case FunctionType.OriginRay
                    Return "ϴ" & fnp
                Case Else
                    Dim fname As String = ChrW(AscW("f"c) + _curfn)
                    Dim n As Integer = 0
                    While fname > "z"c
                        fname = ChrW(AscW(fname) - AscW("z"c) + AscW("f"c) - 1)
                        n += 1
                    End While
                    If _functiontype(fnid) = FunctionType.Differential Then
                        Return "d" & fname & If(n > 0, n.ToString(), "") & "(x)" & "/dx"
                    Else
                        Return fname & If(n > 0, n.ToString(), "") & "(x)"
                    End If
            End Select
        End Function

        Private Function ColorFromFuncId(fnid As Integer) As Color
            Select Case fnid
                Case 0
                    Return Color.Violet
                Case 1
                    Return Color.CornflowerBlue
                Case 2
                    Return Color.SeaGreen
                Case 3
                    Return Color.Orange
                Case 4
                    Return Color.IndianRed
                Case 5
                    Return Color.Teal
                Case 6
                    Return Color.DarkGray
                Case 7
                    Return Color.BurlyWood
                Case 8
                    Return Color.DarkSlateBlue
                Case 9
                    Return Color.DarkSeaGreen
                Case 10
                    Return Color.SaddleBrown
                Case 11
                    Return Color.Cyan
                Case 12
                    Return Color.Gold
                Case Else
                    Return Color.FromArgb(fnid * 100 Mod 255, fnid * 97 Mod 255, fnid * 142 Mod 255)
            End Select
        End Function

        Private Sub DrawFunc(fnid As Integer, g As Graphics, image As Bitmap, center As Coord, Optional ByVal preview As Boolean = False)
            Try
                Dim prevt As Object
                Dim prevsf As Boolean
                prevsf = _eval.SignificantMode
                _eval.SignificantMode = False

                g.SmoothingMode = SmoothingMode.HighQuality

                Dim fn As String = _functions(fnid)
                Dim ofn As String = ""

                If fn = "" Then Exit Sub ' ignore if empty
                prevt = GetT()

                Dim pts As New List(Of PointF)
                Dim p As New Pen(ColorFromFuncId(fnid), 1)

                Select Case _functiontype(fnid)
                    Case FunctionType.Cartesian
                        Dim prevx As Object = GetX()
                        Dim hstep As Double = SelectScale(_scaleBackup.X)
                        Dim i As Double = 0
                        Dim prev As Double = Double.NaN
                        Dim delta As Double = 0.05

                        While i <= WID
                            SetX(ccfx(i, True))

                            Dim res As Double = Eval(fn)

                            Dim y As Double = cfcy(res, True)
                            Dim x As Double = i
                            If Double.IsNaN(prev) OrElse Double.IsNaN(res) OrElse y < 0 OrElse y > HIGH OrElse Math.Abs(y - prev) > HIGH / 2 Then
                                If preview Then
                                    delta = 3
                                Else
                                    delta = 1
                                End If
                                i += delta
                                If pts.Count > 1 Then
                                    g.DrawLines(p, pts.ToArray())
                                End If
                                pts.Clear()
                            Else
                                Dim inner As Double = 100 + Math.Abs((y - prev) / delta)
                                If preview Then
                                    delta = Math.Max(Math.Log(inner) / Math.Log(2), 0.001)
                                Else
                                    delta = Math.Max(Math.Log(inner) / Math.Log(1.1) * 0.01, 0.00005)
                                End If
                                If y < HIGH / 4 OrElse y > HIGH / 4 * 3 Then
                                    delta = Math.Max(delta, 2)
                                End If
                                i += delta
                                pts.Add(New PointF(CSng(x), CSng(y)))
                            End If
                            prev = y
                        End While

                        If pts.Count > 0 Then
                            g.DrawLines(p, pts.ToArray())
                        End If
                        SetX(prevx)
                    Case FunctionType.Inverse
                        Dim prevy As Object = GetY()
                        Dim vstep As Double = SelectScale(_scaleBackup.Y)
                        Dim i As Double = 0
                        Dim prev As Double = Double.NaN
                        Dim delta As Double = 0.05

                        While i <= HIGH
                            SetY(ccfy(i, True))
                            Dim res As Double = Eval(fn)
                            Dim x As Double = cfcx(res, True)
                            Dim y As Double = i
                            If Double.IsNaN(prev) OrElse Double.IsNaN(res) OrElse x < 0 OrElse x > WID Then
                                If preview Then
                                    delta = 3
                                Else
                                    delta = 1
                                End If
                                i += delta
                                If pts.Count > 1 Then
                                    g.DrawLines(p, pts.ToArray())
                                End If
                                pts.Clear()

                            Else
                                Dim inner As Double = 100 + Math.Abs((x - prev) / delta)
                                If preview Then
                                    delta = Math.Max(Math.Log(inner) / Math.Log(2), 0.001)
                                Else
                                    delta = Math.Max(Math.Log(inner) / Math.Log(1.1) * 0.01, 0.00005)
                                End If
                                If x < WID / 4 OrElse x > WID / 4 * 3 Then
                                    delta = Math.Max(delta, 2)
                                End If
                                i += delta
                                pts.Add(New PointF(CSng(x), CSng(y)))
                            End If
                            prev = x
                        End While
                        SetX(prevy)

                    Case FunctionType.Parametric
                        If Not fn.Contains("<"c) OrElse Not fn.Contains(">"c) OrElse
                            fn.IndexOf(","c) > fn.LastIndexOf(">"c) OrElse
                            fn.IndexOf(","c) < fn.IndexOf("<"c) Then
                            _eval.SignificantMode = prevsf
                            ' no range specified or invalid format
                            Return
                        End If

                        If Not fn.Contains("["c) OrElse Not fn.Contains("]"c) Then
                            _eval.SignificantMode = prevsf
                            Return ' no range specified
                        End If
                        Dim range As String = fn.Remove(fn.LastIndexOf("]"c)).Substring(fn.LastIndexOf("["c) + 1)
                        ' convert >= to > and <= to < then split for parsing
                        Dim spl As String() = range.Replace("<=", "<").Replace("<", " < ").Replace(">=", ">") _
                                          .Replace(">", " > ").Replace("  ", " ").Split(" "c)
                        Dim tstart As Double = 0
                        Dim tend As Double = 2
                        ' 0 1 2 3 4
                        ' a < t < b
                        If spl.Count <> 5 OrElse (spl(2).Trim().Length() <> 1) Then
                            _eval.SignificantMode = prevsf
                            Return ' invalid variable (only t and theta are allowed) or range format
                        ElseIf spl(1).Trim() = "<" AndAlso spl(3).Trim() = "<" Then ' normal range a < t < b
                            tstart = Eval(spl(0).Trim())
                            tend = Eval(spl(4).Trim()) + 0.0001
                        ElseIf spl(1).Trim() = ">" AndAlso spl(3).Trim() = ">" Then ' reversed range b > t > a
                            tstart = Eval(spl(4).Trim())
                            tend = Eval(spl(0).Trim()) + 0.0001
                        Else
                            _eval.SignificantMode = prevsf
                            Return ' invalid range format
                        End If

                        Dim i As Double = tstart
                        Dim fnx As String = fn.Remove(fn.IndexOf(","c)).Trim({" "c, "<"c})
                        Dim fny As String = fn.Remove(fn.LastIndexOf(">"c)).Substring(fn.IndexOf(","c) + 1).Trim()
                        Dim inv As Double = If(preview, (tend - tstart) / 200, (tend - tstart) / 1000)
                        While i <= tend
                            SetT(i)
                            Dim resx As Double = Eval(fnx)
                            Dim resy As Double = Eval(fny)
                            i += inv
                            Dim x As Double = cfcx(resx, True)
                            Dim y As Double = cfcy(resy, True)
                            If Double.IsNaN(resx) OrElse Double.IsInfinity(resy) OrElse
                                Double.IsNaN(resy) OrElse y < 0 OrElse y > HIGH OrElse x < 0 OrElse x > WID Then
                                If pts.Count > 1 Then
                                    g.DrawLines(p, pts.ToArray())
                                End If
                                pts.Clear()
                            Else
                                pts.Add(New PointF(CSng(x), CSng(y)))
                            End If
                        End While
                        If pts.Count > 1 Then
                            g.DrawLines(p, pts.ToArray())
                        End If
                    Case FunctionType.Polar
                        If Not fn.Contains("["c) OrElse Not fn.Contains("]"c) Then
                            _eval.SignificantMode = prevsf
                            Return ' no range specified
                        End If
                        Dim range As String = fn.Remove(fn.LastIndexOf("]"c)).Substring(fn.LastIndexOf("["c) + 1)
                        ' convert >= to > and <= to < then split for parsing
                        Dim spl As String() = range.Replace("<=", "<").Replace("<", " < ").Replace(">=", ">") _
                                          .Replace(">", " > ").Replace("  ", " ").Split(" "c)
                        Dim tstart As Double = 0
                        Dim tend As Double = 2
                        ' 0 1 2 3 4
                        ' a < t < b
                        If spl.Count <> 5 OrElse (spl(2).Trim().Length() <> 1) Then
                            _eval.SignificantMode = prevsf
                            Return ' invalid variable or range format
                        ElseIf spl(1).Trim() = "<" AndAlso spl(3).Trim() = "<" Then ' normal range a < t < b
                            tstart = Eval(spl(0).Trim())
                            tend = Eval(spl(4).Trim()) + 0.0001
                        ElseIf spl(1).Trim() = ">" AndAlso spl(3).Trim() = ">" Then ' reversed range b > t > a
                            tstart = Eval(spl(4).Trim())
                            tend = Eval(spl(0).Trim()) + 0.0001
                        Else
                            _eval.SignificantMode = prevsf
                            Return ' invalid range format
                        End If
                        Dim i As Double = tstart
                        Dim polarfn As String = fn.Remove(fn.LastIndexOf("["c)).Trim()
                        While i <= tend
                            SetT(i)
                            Dim resr As Double = Eval(polarfn)
                            Dim resx As Double = resr * Math.Cos(i)
                            Dim resy As Double = resr * Math.Sin(i)
                            If preview Then
                                i += (tend - tstart) / 1500 / Math.Max(Math.Log10((tend - tstart) / 6 / Math.PI), 0.5)
                            Else
                                i += (tend - tstart) / 10000 / Math.Max(Math.Log10((tend - tstart) / Math.PI), 1)
                            End If
                            Dim y As Double = cfcy(resy, True)
                            Dim x As Double = cfcx(resx, True)
                            If Double.IsInfinity(resx) OrElse Double.IsNaN(resx) OrElse Double.IsInfinity(resy) OrElse Double.IsNaN(resy) OrElse y < -5000 OrElse y > 5000 OrElse x < -5000 OrElse x > 5000 Then
                                If pts.Count > 1 Then
                                    g.DrawLines(p, pts.ToArray())
                                End If
                                pts.Clear()
                            Else
                                pts.Add(New PointF(CSng(x), CSng(y)))
                            End If
                        End While
                    Case FunctionType.Differential
                        Dim prevy As Object = GetY()
                        Dim hstep As Double = SelectScale(_scaleBackup.X)
                        Dim vstep As Double = SelectScale(_scaleBackup.Y)
                        Dim deltax As Double = cdfcx(hstep)
                        Dim deltay As Double = cdfcy(vstep)
                        If preview Then
                            deltax *= 3
                            deltay *= 3
                        End If
                        Dim offsetx As Double = (center.X + WID / 2) Mod (deltax)
                        Dim offsety As Double = (center.Y - HIGH / 2) Mod (deltay)
                        Dim LEN As Double = 0.5
                        Dim hlen As Double = Math.Sqrt((LEN * deltax) ^ 2 + (LEN * deltay) ^ 2) / 2

                        For i As Double = offsetx To WID Step deltax
                            For j As Double = -offsety To HIGH Step deltay
                                SetX(ccfx(i, True))
                                SetY(ccfy(j, True))
                                Dim res As Double = Eval(fn)
                                Dim dx As Double = hlen / Math.Sqrt(1 + res * res)
                                Dim dy As Double = CSng(res * dx)
                                If Double.IsInfinity(res) Then
                                    dx = 0
                                    dy = hlen
                                End If
                                g.DrawLine(p, CSng(i + dx), CSng(j - dy), CSng(i - dx), CSng(j + dy))
                            Next
                        Next
                        SetY(prevy)
                    Case FunctionType.OriginRay
                        Dim res As Double = Eval(fn)
                        DrawOriginRay(g, p, res)
                End Select
                If pts.Count > 1 Then g.DrawLines(p, pts.ToArray())
                p.Dispose()
                SetT(prevt)
                _eval.SignificantMode = prevsf
            Catch 'ex As Exception
                'MsgBox(ex.ToString())
            End Try
        End Sub

        Private Sub DrawOriginRay(g As Graphics, p As Pen, rad As Double, Optional ByVal screenCoords As Boolean = False)
            Dim xdist As Double = Math.Cos(rad)
            Dim ydist As Double = Math.Sin(rad)
            Dim slope As Double = ydist / xdist
            If Math.Abs(slope) < 0.00000001 Then
                If xdist > 0 Then
                    g.DrawLine(p, CSng(cfcx(0)), CSng(cfcy(0)), WID, CSng(cfcy(0)))
                Else
                    g.DrawLine(p, CSng(cfcx(0)), CSng(cfcy(0)), 0, CSng(cfcy(0)))
                End If
            ElseIf ydist > 0 Then
                g.DrawLine(p, CSng(cfcx(0)), CSng(cfcy(0)), CSng(cfcx((ccfy(0)) / slope)), 0)
            Else
                g.DrawLine(p, CSng(cfcx(0)), CSng(cfcy(0)), CSng(cfcx((ccfy(HIGH)) / slope)), HIGH)
            End If
        End Sub

        Dim sui As New Font(OpenSans, 15)
        Dim hstep As Double
        Dim vstep As Double

        Private Sub PaintAxes(g As Graphics, center As Coord)
            Using p As New Pen(Color.FromArgb(60, 60, 60), 1)
                Using b As New SolidBrush(Color.FromArgb(100, 100, 100))

                    Dim offset As Double = center.X Mod (hstep * SCREENFACT / _scaleBackup.X)
                    For i As Double = 0 To WID \ 2 Step hstep * SCREENFACT / _scaleBackup.X
                        g.DrawLine(p, CSng(WID / 2 + offset + i), 0, CSng(WID / 2 + offset + i), HIGH)
                        g.DrawLine(p, CSng(WID / 2 + offset - i), 0, CSng(WID / 2 + offset - i), HIGH)
                    Next

                    offset = center.Y Mod (vstep * SCREENFACT / _scaleBackup.Y)
                    For i As Double = 0 To HIGH \ 2 Step vstep * SCREENFACT / _scaleBackup.Y
                        g.DrawLine(p, 0, CSng(HIGH / 2 - offset + i), WID, CSng(HIGH / 2 - offset + i))
                        g.DrawLine(p, 0, CSng(HIGH / 2 - offset - i), WID, CSng(HIGH / 2 - offset - i))
                    Next
                    p.Color = Color.DimGray
                    p.Width = 2

                    If Math.Abs(center.X) <= WID / 2 Then
                        g.DrawLine(p, CSng(WID / 2 + center.X), 0, CSng(WID / 2 + center.X), HIGH)
                        If Math.Abs(center.Y) <= HIGH / 2 Then
                            g.DrawString(hstep.ToString, sui, b, CSng(WID / 2 + center.X + hstep * SCREENFACT / _scaleBackup.X - 3), CSng(HIGH / 2 - center.Y - 4))
                            g.DrawString(vstep.ToString, sui, b, CSng(WID / 2 + center.X) + 2, CSng(HIGH / 2 - center.Y - vstep * SCREENFACT / _scaleBackup.Y - 16))
                        End If
                    End If
                    If Math.Abs(center.Y) <= HIGH / 2 Then
                        g.DrawLine(p, 0, CSng(HIGH / 2 - center.Y), WID, CSng(HIGH / 2 - center.Y))
                    End If
                End Using
            End Using
        End Sub

        Dim _zmfact As Double = 1

        Private Sub Redraw(Optional ByVal preview As Boolean = False)
            If Math.Round(_zmfact, 13) <> 1 Then
                ' zoom
                Dim p As Point = Canvas.PointToClient(Cursor.Position)

                Dim origfx As Double = ccfx(cscx(p.X))
                Dim origfy As Double = ccfy(cscy(p.Y))
                Dim nsx As Double = _scale.X * _zmfact
                Dim nsy As Double = _scale.Y * _zmfact
                If nsx < MINSCALE OrElse nsy < MINSCALE OrElse nsx > MAXSCALE OrElse nsx > MAXSCALE Then Exit Sub
                _scale.X = nsx
                _scale.Y = nsy
                Dim newcx As Double = cscx(p.X)
                Dim newcy As Double = cscy(p.Y)
                Dim origcx As Double = cfcx(origfx)
                Dim origcy As Double = cfcy(origfy)

                _loc.X -= origcx - newcx
                _loc.Y += origcy - newcy
                _zmfact = 1
            End If

            Dim g As Graphics = Graphics.FromImage(_tmpbuffer)

            g.Clear(Canvas.BackColor)
            g.SmoothingMode = SmoothingMode.HighSpeed
            g.CompositingQuality = CompositingQuality.HighSpeed

            _scaleBackup = _scale

            hstep = SelectScale(_scaleBackup.X)
            vstep = SelectScale(_scaleBackup.Y)

            _centerBackup = New Coord(_loc.X, _loc.Y)

            ' draw the grid
            PaintAxes(g, _centerBackup)

            For i As Integer = 0 To _functions.Count - 1
                DrawFunc(i, g, _tmpbuffer, _centerBackup, preview)
            Next
            _center = _centerBackup
            '_scale = _scaleBackup
            '_loc = New Coord(_center.X, _center.Y)
        End Sub

        Private Sub GetPtCartesian(ByRef x As Double, ByRef y As Double, ByRef d As Double, ByRef ptname As String)
            If x + _scale.X / 300 - x < Double.Epsilon Then Return ' too big, will cause a crash due to precision limitations
            For mid As Double = x - _scale.X / 12 To x + _scale.X / 12 Step _scale.X / 300
                Dim lstr As New List(Of Double)
                For i As Double = -_scale.X / 200 To _scale.X / 200 Step _scale.X / 400
                    SetX(mid + i, True)
                    lstr.Add(Eval(_functions(_curfn), True))
                Next
                If _functions.Count <= 10 Then
                    SetX(mid, True)
                    For i As Integer = 0 To _functions.Count - 1
                        If i = _curfn OrElse _functions(i) = _functions(_curfn) Then Continue For
                        If _functiontype(i) = FunctionType.Inverse Then
                            SetY(lstr(2), True)
                            If Math.Abs(mid - Eval(_functions(i), True)) < 0.001 Then
                                ptname = "Intersection" & vbCrLf
                                x = mid
                                y = lstr(2)
                                Exit For
                            End If
                        ElseIf _functiontype(i) = FunctionType.Cartesian Then

                            If Math.Abs(Eval(_functions(i), True) - lstr(2)) <
                                                0.005 Then
                                ptname = "Intersection" & vbCrLf
                                x = mid
                                y = lstr(2)
                                Exit For
                            End If
                        End If
                    Next
                End If
                If lstr(3) * lstr(1) < 0 Then
                    If Math.Abs(y - lstr(1)) <= 5000 Then
                        ptname = "Zero" & vbCrLf
                        lstr(2) = 0
                    End If
                ElseIf (lstr(2) - lstr(0)) * (lstr(4) - lstr(2)) < 0 Then
                    If (lstr(4) - lstr(2)) - (lstr(2) - lstr(0)) < 0 Then
                        ptname = "Maximum" & vbCrLf
                    Else
                        ptname = "Minimum" & vbCrLf
                    End If
                End If
                If Not String.IsNullOrEmpty(ptname) Then
                    x = mid
                    y = lstr(2)
                    Exit For
                End If
            Next
            d = x
        End Sub
        Private Sub GetPtInverse(ByRef x As Double, ByRef y As Double, ByRef d As Double, ByRef ptname As String)
            If x + _scale.X / 300 - x < Double.Epsilon Then Return ' too big, will cause a crash due to precision limitations
            For mid As Double = y - _scale.Y / 12 To y + _scale.Y / 12 Step _scale.Y / 300
                Dim lstr As New List(Of Double)
                For i As Double = -_scale.Y / 200 To _scale.Y / 200 Step _scale.Y / 400
                    SetY(mid + i, True)
                    lstr.Add(Eval(_functions(_curfn), True))
                Next
                If _functions.Count <= 10 Then
                    SetY(mid, True)
                    For i As Integer = 0 To _functions.Count - 1
                        If i = _curfn OrElse _functions(i) = _functions(_curfn) Then Continue For
                        If _functiontype(i) = FunctionType.Inverse Then
                            SetY(lstr(2), True)
                            If Math.Abs(mid - Eval(_functions(i), True)) < 0.001 Then
                                ptname = "Intersection" & vbCrLf
                                y = mid
                                x = lstr(2)
                                Exit For
                            End If
                        ElseIf _functiontype(i) = FunctionType.Cartesian Then

                            If Math.Abs(Eval(_functions(i), True) - lstr(2)) <
                                                0.005 Then
                                ptname = "Intersection" & vbCrLf
                                y = mid
                                x = lstr(2)
                                Exit For
                            End If
                        End If
                    Next
                End If
                If lstr(3) * lstr(1) < 0 Then
                    If Math.Abs(x - lstr(1)) <= 5000 Then
                        ptname = "Zero" & vbCrLf
                        lstr(2) = 0
                    End If
                ElseIf (lstr(2) - lstr(0)) * (lstr(4) - lstr(2)) < 0 Then
                    If (lstr(4) - lstr(2)) - (lstr(2) - lstr(0)) < 0 Then
                        ptname = "Maximum" & vbCrLf
                    Else
                        ptname = "Minimum" & vbCrLf
                    End If
                End If
                If Not String.IsNullOrEmpty(ptname) Then
                    y = mid
                    x = lstr(2)
                    Exit For
                End If
            Next
            d = y
        End Sub

        Private Sub GetPtPolar(ByRef x As Double, ByRef y As Double, ByRef d As Double, ByRef ptname As String)
            For mid As Double = x - Math.PI / 30 To x + Math.PI / 30 Step Math.PI / 1800
                SetT(mid, True)
                Dim fn As String = _functions(_curfn)
                fn = fn.Remove(fn.IndexOf("["c)).Trim()
                Dim lstr As New List(Of Double)
                lstr.Add(0)
                For i As Double = -Math.PI / 30000 To Math.PI / 30000 Step Math.PI / 30000
                    SetT(mid + i, True)
                    lstr.Add(Eval(fn, True))
                Next
                Dim mr As Double = lstr(2)
                If _functions.Count <= 10 Then
                    For i As Integer = 0 To _functions.Count - 1
                        If i = _curfn OrElse _functions(i) = _functions(_curfn) Then Continue For
                        If _functiontype(i) = FunctionType.OriginRay Then
                            If Math.Abs(mid - Eval(_functions(i), True)) < _scale.X / 1000 Then
                                ptname = "Intersection" & vbCrLf
                                Exit For
                            End If
                        ElseIf _functiontype(i) = FunctionType.Polar Then

                            If Math.Abs(Eval(_functions(i), True) - mr) <
                                                0.005 Then
                                ptname = "Intersection" & vbCrLf
                                Exit For
                            End If
                        End If
                    Next
                End If
                If lstr(3) * lstr(1) < 0 OrElse Math.Abs(lstr(2)) < 0.0000005 Then
                    If Math.Abs(x - lstr(1)) <= 5000 Then
                        ptname = "Zero" & vbCrLf
                        d = mid
                        x = 0
                        y = 0
                        Exit Sub
                    End If
                End If
                If Not String.IsNullOrEmpty(ptname) Then
                    d = mid
                    x = Math.Cos(d) * mr
                    y = Math.Sin(d) * mr
                    Exit Sub
                End If
            Next
            d = x
            x = Math.Cos(d) * y
            y = Math.Sin(d) * y
        End Sub

        Private Function Eval(str As String, Optional useEval2 As Boolean = False) As Double
            Try
                Dim ret As Double
                If useEval2 Then
                    ret = CDbl(CType(_eval2.EvalExprRaw(str, True), Core.CommonTypes.BigDecimal))
                Else
                    ret = CDbl(CType(_eval.EvalExprRaw(str, True), Core.CommonTypes.BigDecimal))
                End If
                Return ret
            Catch 'ex As Exception
                Return Double.NaN
            End Try
        End Function

        Private Function SigFig(ByVal value As Double, Optional ByVal digits As Integer = 1) As Double
            Return Math.Sign(value) *
                Math.Round(Math.Abs(value) / 10 ^ Math.Ceiling(Math.Log10(Math.Abs(value))),
                           digits) * 10 ^ Math.Ceiling(Math.Log10(Math.Abs(value)))
        End Function

        Private Sub canvas_Paint(sender As Object, e As PaintEventArgs) Handles Canvas.Paint
            Dim lf As Double = cscx(0)
            Dim tp As Double = cscy(0)
            Dim rt As Double = cscx(Canvas.Width)
            Dim bt As Double = cscy(Canvas.Height)
            If lf <= 0 OrElse tp <= 0 OrElse rt >= WID OrElse bt >= HIGH Then
                If Not DrawWorker.IsBusy Then DrawWorker.RunWorkerAsync()
                Return
            End If
            Dim g As Graphics = e.Graphics
            g.CompositingQuality = CompositingQuality.HighSpeed

            Try
                lf = cscx(0)
                tp = cscy(0)
                rt = cscx(Canvas.Width)
                bt = cscy(Canvas.Height)
                g.DrawImage(_buffer, 0, 0, New RectangleF(CSng(lf), CSng(tp), Canvas.Width, Canvas.Height), GraphicsUnit.Pixel)
            Catch 'ex As Exception
            End Try

            Using cb As New SolidBrush(ColorFromFuncId(_curfn))
                Using b As New SolidBrush(Color.FromArgb(100, 100, 100))
                    ' end of screen range displays
                    Dim sui As New Font(OpenSans, 14)
                    Dim s As String = SigFig(ccfx(cscx(Canvas.Width)), PRECISION).ToString
                    Dim sz As SizeF
                    g.DrawString(SigFig(ccfx(cscx(0)), PRECISION).ToString, sui, b, 1, CSng(Canvas.Height / 2) - 2)
                    sz = g.MeasureString(s, sui)
                    g.DrawString(s, sui, b, Canvas.Width - sz.Width - 1, CSng(Canvas.Height / 2) - 2)

                    s = SigFig(ccfy(cscy(Canvas.Height)), PRECISION).ToString
                    g.DrawString(SigFig(ccfy(cscy(0)), PRECISION).ToString,
                                 sui, b, CSng(Canvas.Width / 2) - 2, 1)
                    sz = g.MeasureString(s, sui)
                    g.DrawString(s, sui, b, CSng(Canvas.Width / 2) - 2, Canvas.Height - sz.Height - 1)

                    If _traceOn Then
                        Try
                            Dim x As Double = 0
                            Dim y As Double = 0
                            Dim d As Double = 0
                            If _functiontype(_curfn) <> FunctionType.Differential Then x = Eval(NpdTVal.Text, True)
                            Dim coords As String = ""
                            Dim initd As Double = x
                            Dim oldx As Double = CDbl(GetX(True))
                            Dim oldy As Double = CDbl(GetY(True))
                            Dim oldt As Double = CDbl(GetT(True))
                            g.SmoothingMode = SmoothingMode.HighQuality
                            Dim ptname As String = ""

                            Dim fn As String = _functions(_curfn)
                            Select Case _functiontype(_curfn)

                                Case FunctionType.Cartesian

                                    SetX(x, True)
                                    y = Eval(fn, True)
                                    GetPtCartesian(x, y, d, ptname)
                                    coords = "(" & Math.Round(x, 3) & ", " & Math.Round(y, 3) & ")"

                                Case FunctionType.Inverse
                                    y = x
                                    SetY(y, True)
                                    x = Eval(fn, True)
                                    GetPtInverse(x, y, d, ptname)
                                    coords = "(" & Math.Round(y, 3) & ", " & Math.Round(x, 3) & ")"
                                Case FunctionType.Parametric
                                    SetT(x, True)
                                    d = x
                                    x = Eval(fn.Remove(fn.IndexOf(","c)).Substring(fn.IndexOf("<"c) + 1), True)
                                    y = Eval(fn.Remove(fn.LastIndexOf(">"c)).Substring(fn.IndexOf(","c) + 1), True)
                                    coords = "(" & Math.Round(x, 3) & ", " & Math.Round(y, 3) & ")" & vbCrLf & "t = " & CDbl(GetT(True))
                                Case FunctionType.Polar
                                    ' check if valid
                                    If Not fn.Contains("["c) OrElse Not fn.Contains("]"c) Then Exit Sub
                                    SetT(x, True)
                                    y = Eval(fn.Remove(fn.IndexOf("["c)).Trim(), True)
                                    ' x is set to theta, y is set to r
                                    GetPtPolar(x, y, d, ptname)
                                    coords = "(" & Math.Round(Math.Sqrt(x ^ 2 + y ^ 2), 3) & ", " & Math.Round(d, 3) & ")"
                                Case FunctionType.Differential
                                    Dim tuple() As String = NpdTVal.Text.Trim({vbCr(0), vbLf(0), " "c, "("c, ")"c, "<"c, ">"c}).Split(","c)
                                    If tuple.Length <> 2 Then
                                        LbTVal.Text = "Invalid Format"
                                        Return
                                    End If
                                    x = Eval(tuple(0), True)
                                    y = Eval(tuple(1), True)
                            End Select
                            SetX(oldx, True)
                            SetY(oldy, True)
                            SetT(oldt, True)

                            Using txTb As New SolidBrush(Color.Silver)
                                Using linep As New Pen(Color.SlateGray)
                                    Dim cx As Double = ccsx(cfcx(x))
                                    Dim cy As Double = ccsy(cfcy(y))
                                    If Math.Abs(initd - d) > 0.000001 AndAlso _functiontype(_curfn) <> FunctionType.Differential Then
                                        NpdTVal.Text = Math.Round(d, 5).ToString()
                                    End If
                                    If cx > Canvas.Width OrElse cx < 0 Then
                                        Return
                                    End If

                                    Select Case _functiontype(_curfn)
                                        Case FunctionType.Cartesian
                                            g.DrawLine(linep, CSng(cx), 0, CSng(cx), HIGH)
                                        Case FunctionType.Inverse
                                            g.DrawLine(linep, 0, CSng(cy), WID, CSng(cy))
                                        Case FunctionType.Differential
                                            g.DrawLine(linep, CSng(cx), 0, CSng(cx), HIGH)
                                            g.DrawLine(linep, 0, CSng(cy), WID, CSng(cy))
                                        Case FunctionType.Polar
                                            Dim originx As Double = cfsx(0)
                                            Dim originy As Double = cfsy(0)
                                            Dim slope As Double = -Math.Tan(d)
                                            Dim projx As Double
                                            Dim disty As Double = Double.NaN
                                            Dim modangle As Double = d Mod Math.PI * 2
                                            If d < 0 Then modangle = Math.PI * 2 - modangle
                                            If originy > 0 AndAlso modangle <= Math.PI Then
                                                disty = 0 - originy
                                            ElseIf originy < Canvas.Height AndAlso modangle >= Math.PI Then
                                                disty = Canvas.Height - originy
                                            End If
                                            If Not Double.IsNaN(disty) Then
                                                If Math.Abs(slope) < 0.0000001 Then
                                                    If Math.Abs(modangle) < Math.PI / 2 Then
                                                        g.DrawLine(linep, CSng(originx),
                                                               CSng(originy), Canvas.Width, CSng(originy))
                                                    Else
                                                        g.DrawLine(linep, CSng(originx),
                                                               CSng(originy), 0, CSng(originy))
                                                    End If
                                                Else
                                                    projx = disty / slope + originx
                                                    g.DrawLine(linep, CSng(originx), CSng(originy), CSng(projx), CSng(originy + disty))
                                                End If
                                            End If
                                    End Select

                                    If (cy > Canvas.Height OrElse cy < 0) AndAlso (cx > Canvas.Height OrElse cx < 0) Then Return
                                    g.FillEllipse(cb, CSng(cx - 3), CSng(cy - 3), 6, 6)
                                    g.DrawString(ptname & coords,
                               New Font(Me.Font.FontFamily, 15, FontStyle.Regular, GraphicsUnit.Pixel) _
                                , txTb, New PointF(CSng(cx + 10), CSng(cy - 10)))
                                End Using
                            End Using
                        Catch ex As Exception
                        End Try
                    End If
                End Using
            End Using
        End Sub

        Private Function SelectScale(scale As Double) As Double
            Dim pow10 As Double = Math.Pow(10, Math.Floor(Math.Log10(scale)))
            Dim propscale As Double = scale / pow10

            If propscale <= 1.5 Then
                Return pow10
            ElseIf propscale <= 3 Then
                Return 2 * pow10
            ElseIf propscale <= 7 Then
                Return 5 * pow10
            Else
                Return 10 * pow10
            End If
        End Function
        Private Sub UpdateGraph()
            Dim ltText As String = Tb.Text.ToLower().Trim()
            'quick clear
            If ltText = "cls" OrElse ltText = "clear" OrElse ltText.EndsWith(" cls") OrElse ltText.EndsWith(" clear") Then
                Tb.Text = ""
                Exit Sub
            ElseIf ltText.Replace(" ", "") = "allclear" Then
                Tb.Text = ""
                _eval.ClearVariables()
                _eval.PrevAns.Clear()
                Exit Sub
            ElseIf ltText.EndsWith(" ans") Then
                Tb.Text = "ans"
                Tb.SelectionStart = Tb.Text.Length
                Exit Sub
            End If
            If Tb.Text.Contains(":=") AndAlso Not Tb.Text.StartsWith("{") Then
                Eval(Tb.Text)
                Tb.Text = ""
                Tb.SelectionStart = Tb.Text.Length
            Else
                _functions(_curfn) = Tb.Text
            End If
        End Sub
        Private Sub BtnGraph_Click(sender As Object, e As EventArgs) Handles BtnGraph.Click
            UpdateGraph()
            Tb.Focus()
        End Sub
        Private Sub EnablePrevNextFnBtns(ByVal prev As Boolean, ByVal nxt As Boolean)
            BtnPrevFn.Enabled = prev
            BtnNextFn.Enabled = nxt
            If BtnPrevFn.Enabled Then
                Dim c As Color = ColorFromFuncId(_curfn - 1)
                BtnPrevFn.ForeColor = Color.FromArgb((c.R + 255) \ 2, (c.G + 255) \ 2, (c.B + 255) \ 2)
            End If
            If BtnNextFn.Enabled Then
                Dim c As Color = ColorFromFuncId(_curfn + 1)
                BtnNextFn.ForeColor = Color.FromArgb((c.R + 255) \ 2, (c.G + 255) \ 2, (c.B + 255) \ 2)
            End If
        End Sub

        Private Sub BtnAdd_Click(sender As Object, e As EventArgs) Handles BtnAdd.Click
            If _functions.Count <= 101 Then
                _functions.Add(Tb.Text)
                _functiontype.Add(_functiontype(_curfn))
                _curfn = _functions.Count - 1
                EnablePrevNextFnBtns(True, False)
                Dim fname As String = NameFromFuncId(_curfn)
                LbFx.Text = fname & " = "
                If _functions.Count = 101 Then
                    BtnAdd.Enabled = False
                End If
                Tb.Focus()
                UpdateFnLabelColor()
                UpdateFunc()
            Else
                MsgBox("Function number limit reached")
            End If
        End Sub

        Private Sub PnlInput_MouseDown(sender As Object, e As MouseEventArgs) Handles PnlInput.MouseDown
            Tb.Focus()
        End Sub

        Dim dragging As Boolean = False
        Dim ppt As New Coord

        Private Sub TraceUpdateCoords(p As Point)
            Select Case _functiontype(_curfn)
                Case FunctionType.Cartesian
                    NpdTVal.Text = Math.Round(ccfx(cscx(p.X)), 3).ToString()
                Case FunctionType.Inverse
                    NpdTVal.Text = Math.Round((ccfy(cscy(p.Y))), 3).ToString()
                Case FunctionType.Polar
                    Dim fx As Double = ccfx(cscx(p.X))
                    Dim fy As Double = ccfy(cscy(p.Y))
                    Dim atan As Double = Math.Atan2(Math.Abs(fy), Math.Abs(fx))
                    If fx > 0 Then
                        If fy < 0 Then
                            atan = 2 * Math.PI - atan
                        End If
                    Else
                        If fy > 0 Then
                            atan = Math.PI - atan
                        Else
                            atan += Math.PI
                        End If
                    End If
                    NpdTVal.Text = Math.Round(atan, 3).ToString()
                Case FunctionType.Differential
                    NpdTVal.Text = "(" & Math.Round(ccfx(cscx(p.X)), 3).ToString() & "," & Math.Round(ccfy(cscy(p.Y)), 3).ToString() & ")"
            End Select
        End Sub

        Private Sub Canvas_MouseDown(sender As Object, e As MouseEventArgs) Handles Canvas.MouseDown
            PnlFnType.Hide()

            If _traceOn AndAlso _functiontype(_curfn) <> FunctionType.Parametric AndAlso e.Button = MouseButtons.Left Then
                TraceUpdateCoords(e.Location)
                TmrTraceUpdate.Start()
            ElseIf e.Button <> MouseButtons.Right Then
                ppt.X = Cursor.Position.X
                ppt.Y = Cursor.Position.Y
                dragging = True
                TmrDrag.Start()
                Canvas.Cursor = Cursors.Hand
            End If
        End Sub

        Private Sub Canvas_MouseUp(sender As Object, e As MouseEventArgs) Handles Canvas.MouseUp
            If _traceOn AndAlso _functiontype(_curfn) <> FunctionType.Parametric AndAlso e.Button = MouseButtons.Left Then
                TmrTraceUpdate.Stop()
                NpdTVal.Select(0, NpdTVal.Text.Length)
            ElseIf e.Button = MouseButtons.Right Then
                BtnTrace.PerformClick()
                TraceUpdateCoords(Canvas.PointToClient(Cursor.Position))
            Else
                TmrDrag.Stop()
                dragging = False
                _loc.X += Cursor.Position.X - ppt.X
                _loc.Y -= Cursor.Position.Y - ppt.Y
                Canvas.Cursor = Cursors.Arrow
            End If
        End Sub

        Private Sub tmrDrag_Tick(sender As Object, e As EventArgs) Handles TmrDrag.Tick
            If dragging Then
                _loc.X += CSng(Cursor.Position.X - ppt.X)
                _loc.Y -= CSng(Cursor.Position.Y - ppt.Y)
                ppt.X = Cursor.Position.X
                ppt.Y = Cursor.Position.Y
                Canvas.Invalidate()
            End If
        End Sub

        Private Sub canvas_MouseWheel(sender As Object, e As MouseEventArgs) Handles Canvas.MouseWheel, Tb.MouseWheel
            _zmfact = 1 - (e.Delta * SCALESPEED)

            If PnlWindow.Visible Then
                BtnWCancel.PerformClick()
            End If

            preview = True
            If Not DrawWorker.IsBusy Then DrawWorker.RunWorkerAsync()
            TmrHighQuality.Start()
        End Sub

        Private Sub LbFx_MouseDown(sender As Object, e As MouseEventArgs) Handles LbFx.MouseDown
            PnlFnType.Top = Split.SplitterDistance - PnlFnType.Height + 3
            PnlFnType.Left = BtnPrevFn.Width + Split.Panel2.Left
            If Not PnlFnType.Visible Then
                If _functions.Count <= 1 Then
                    BtnFnDel.Enabled = False
                Else
                    BtnFnDel.Enabled = True
                End If
            End If
            PnlFnType.Visible = Not PnlFnType.Visible
        End Sub

        Private Sub Tb_KeyUp(sender As Object, e As KeyEventArgs) Handles Tb.KeyUp
            If e.KeyCode = Keys.Enter And e.Alt Then
                BtnGraph.PerformClick()
            End If
        End Sub

        Private Sub TraceCartesian()
            Dim prevx As Object
            prevx = GetX(True)
            Try
                SetX(Double.NaN, True)
                SetX(Eval(NpdTVal.Text, True), True)
                Dim fn As String = _functions(_curfn)
                Dim o As Object = Eval(fn, True)
                Try
                    o = Math.Round(CDbl(o), 8)
                Catch
                    SetX(prevx, True)
                    Exit Sub
                End Try
                DeltaX(-0.0000005, True)
                Dim olf As Double = Eval(fn, True)
                DeltaX(0.000001, True)
                Dim ort As Double = Eval(fn, True)
                DeltaX(-0.0000005, True)
                Dim dxdy As Double = Math.Round((ort - olf) / 0.000001, 5)
                If Math.Abs(dxdy) > 100000 * _scale.Y Then
                    dxdy = Double.NaN
                End If

                LbTVal.Text = LbFx.Text.Remove(LbFx.Text.IndexOf("(") + 1) & Math.Round(CDbl(GetX(True)), 3) & ") = " & o.ToString().Replace("NaN", "Undefined") & vbCrLf & LbFx.Text.Remove(LbFx.Text.IndexOf("(")) & "(" & Math.Round(CDbl(GetX(True)), 3) & ") = " & dxdy.ToString().Replace("NaN", "Undefined")
            Catch 'ex As Exception
                'MsgBox(ex.ToString()) ' DEBUG
            End Try
            SetX(prevx, True)
        End Sub

        Private Sub TraceInverse()
            Dim prevy As Object
            prevy = GetY(True)
            Try
                SetY(Double.NaN, True)
                SetY(Eval(NpdTVal.Text, True), True)
                Dim fn As String = _functions(_curfn)
                Dim o As Object = Eval(fn, True)
                Try
                    o = Math.Round(CDbl(o), 8)
                Catch
                    SetY(prevy, True)
                    Exit Sub
                End Try
                DeltaY(-0.0000005, True)
                Dim olf As Double = Eval(fn, True)
                DeltaY(0.000001, True)
                Dim ort As Double = Eval(fn, True)
                DeltaY(-0.0000005, True)
                Dim dxdy As Double = Math.Round((ort - olf) / 0.000001, 5)
                If Math.Abs(dxdy) > 100000 * _scale.Y Then
                    dxdy = Double.NaN
                End If

                LbTVal.Text = NameFromFuncId(_curfn) & "(" & Math.Round(CDbl(GetY(True)), 3) & ") = " & o.ToString().Replace("NaN", "Undefined") & vbCrLf & NameFromFuncId(_curfn) & " '(" & Math.Round(CDbl(GetY(True)), 3) & ") = " & dxdy.ToString().Replace("NaN", "Undefined")
            Catch 'ex As Exception
                'MsgBox(ex.ToString()) ' DEBUG
            End Try
            SetY(prevy, True)
        End Sub

        Private Sub TracePolar()
            Dim prevt As Object
            prevt = GetT(True)
            Try
                SetT(Eval(NpdTVal.Text, True), True)
                Dim fn As String = _functions(_curfn)
                Dim range As String = fn.Remove(fn.LastIndexOf("]"c)).Substring(fn.LastIndexOf("["c) + 1)
                fn = fn.Remove(fn.LastIndexOf("["c)).Trim()

                ' convert >= to > and <= to < then split for parsing
                Dim spl As String() = range.Replace("<=", "<").Replace("<", " < ").Replace(">=", ">") _
                                          .Replace(">", " > ").Replace("  ", " ").Split(" "c)
                Dim tstart As Double = 0
                Dim tend As Double = 2
                If spl.Count <> 5 OrElse (spl(2).Trim().Length() <> 1) Then
                    Return ' invalid variable or range format
                ElseIf spl(1).Trim() = "<" AndAlso spl(3).Trim() = "<" Then ' normal range a < t < b
                    tstart = Eval(spl(0).Trim(), True)
                    tend = Eval(spl(4).Trim(), True) + 0.0001
                ElseIf spl(1).Trim() = ">" AndAlso spl(3).Trim() = ">" Then ' reversed range b > t > a
                    tstart = Eval(spl(4).Trim(), True)
                    tend = Eval(spl(0).Trim(), True) + 0.0001
                Else
                    Return ' invalid range format
                End If

                If CDbl(GetT(True)) > tend OrElse CDbl(GetT(True)) < tstart Then
                    LbTVal.Text = NameFromFuncId(_curfn).Replace("t", Math.Round(CDbl(GetT(True)), 3).ToString()) & ") = " & "Undefined" & vbCrLf & "tan. line slope Undefined"
                    Return
                End If

                Dim o As Object = Eval(fn, True)
                Try
                    o = Math.Round(CDbl(o), 8)
                Catch
                    SetT(prevt, True)
                    Exit Sub
                End Try
                DeltaT(-0.0005, True)
                Dim olf As Double = Eval(fn, True)
                DeltaT(0.001, True)
                Dim ort As Double = Eval(fn, True)
                DeltaT(-0.0005, True)
                Dim dydx As Double = Math.Round((Math.Sin(CDbl(GetT(True)) + 0.0005) * ort - Math.Sin(CDbl(GetT(True)) - 0.0005) * olf) /
                                            (Math.Cos(CDbl(GetT(True)) + 0.0005) * ort - Math.Cos(CDbl(GetT(True)) - 0.0005) * olf),
                                            5)

                LbTVal.Text = LbFx.Text.Remove(LbFx.Text.IndexOf("(") + 1) & Math.Round(CDbl(GetT(True)), 3) & ") = " & o.ToString().Replace("NaN", "Undefined") & vbCrLf & "tan. line slope " & dydx.ToString().Replace("NaN", "Undefined")
            Catch 'ex As Exception
                'MsgBox(ex.ToString()) ' DEBUG
            End Try
            SetT(prevt, True)
        End Sub

        Private Sub TraceParametric()
            Dim prevt As Object
            prevt = GetT(True)
            Try
                SetT(Eval(NpdTVal.Text, True), True)
                Dim fn As String = _functions(_curfn)
                Dim fnx As String = fn.Remove(fn.IndexOf(","c)).Substring(fn.IndexOf("<"c) + 1).Trim()
                Dim fny As String = fn.Remove(fn.IndexOf(">"c)).Substring(fn.IndexOf(","c) + 1).Trim()
                Dim range As String = fn.Remove(fn.LastIndexOf("]"c)).Substring(fn.LastIndexOf("["c) + 1)
                fn = fn.Remove(fn.LastIndexOf("["c)).Trim()

                ' convert >= to > and <= to < then split for parsing
                Dim spl As String() = range.Replace("<=", "<").Replace("<", " < ").Replace(">=", ">") _
                                          .Replace(">", " > ").Replace("  ", " ").Split(" "c)
                Dim tstart As Double = 0
                Dim tend As Double = 2
                If spl.Count <> 5 OrElse (spl(2).Trim().Length() <> 1) Then
                    Return ' invalid variable (only t and theta are allowed) or range format
                ElseIf spl(1).Trim() = "<" AndAlso spl(3).Trim() = "<" Then ' normal range a < t < b
                    tstart = Eval(spl(0).Trim(), True)
                    tend = Eval(spl(4).Trim(), True) + 0.0001
                ElseIf spl(1).Trim() = ">" AndAlso spl(3).Trim() = ">" Then ' reversed range b > t > a 
                    tstart = Eval(spl(4).Trim(), True)
                    tend = Eval(spl(0).Trim(), True) + 0.0001
                Else
                    LbTVal.Text = "Range Format Error"
                    Return ' invalid range format
                End If
                If CDbl(GetT(True)) > tend OrElse CDbl(GetT(True)) < tstart Then
                    LbTVal.Text = NameFromFuncId(_curfn).Replace("t", Math.Round(CDbl(GetT(True)), 3).ToString()) & vbCrLf & "= (" & "Undefined, Undefined)" & vbCrLf & "tan. line slope Undefined"
                    Return
                End If

                Dim ox As Object = Eval(fnx, True)
                Dim oy As Object = Eval(fny, True)
                Try
                    ox = Math.Round(CDbl(ox), 8)
                    oy = Math.Round(CDbl(oy), 8)
                Catch
                    SetT(prevt, True)
                    Exit Sub
                End Try
                DeltaT(-0.05, True)
                Dim oylf As Double = Eval(fny, True)
                Dim oxlf As Double = Eval(fnx, True)
                DeltaT(0.1, True)
                Dim oyrt As Double = Eval(fny, True)
                Dim oxrt As Double = Eval(fnx, True)
                Dim dydx As Double = Math.Round((oyrt - oylf) / (oxrt - oxlf), 5)
                DeltaT(-0.05, True)

                LbTVal.Text = NameFromFuncId(_curfn).Replace("t", Math.Round(CDbl(GetT(True)), 3).ToString()) & vbCrLf & "= (" & ox.ToString().Replace("NaN", "Undefined") & ", " & oy.ToString().Replace("NaN", "Undefined") & ")" & vbCrLf & "tan. line slope " & dydx.ToString().Replace("NaN", "Undefined")

            Catch 'ex As Exception
                'MsgBox(ex.ToString()) ' DEBUG
            End Try
            SetT(prevt, True)
        End Sub

        Private Sub TraceDifferential()
            Try
                Dim prevx As Object = GetX(True)
                Dim prevy As Object = GetY(True)
                Dim tuple() As String = NpdTVal.Text.Trim({vbCr(0), vbLf(0), " "c, "("c, ")"c, "<"c, ">"c}).Split(","c)
                If tuple.Length <> 2 Then
                    LbTVal.Text = "Invalid Format"
                    Return
                End If
                Dim x As Double = Eval(tuple(0), True)
                Dim y As Double = Eval(tuple(1), True)
                SetX(x, True)
                SetY(y, True)
                LbTVal.Text = NameFromFuncId(_curfn) & vbCrLf & " [" & x & "," & y & "]" & vbCrLf & " = " &
                    Math.Round(CDbl(Eval(_functions(_curfn), True)), 5).ToString().Replace("NaN", "Undefined")
                SetX(prevx, True)
                SetY(prevy, True)
            Catch
            End Try
        End Sub

        Private Sub Trace()
            _eval2 = _eval.DeepCopy()
            _eval2.SignificantMode = False
            Select Case _functiontype(_curfn)
                Case FunctionType.Cartesian
                    TraceCartesian()
                Case FunctionType.Inverse
                    TraceInverse()
                Case FunctionType.Parametric
                    TraceParametric()
                Case FunctionType.Polar
                    TracePolar()
                Case FunctionType.Differential
                    TraceDifferential()
            End Select
            Canvas.Invalidate()
        End Sub

        Private Sub TraceSpecialCartesian(Optional primary_range As Integer = 26)
            Try
                Dim gx As Func(Of Object)
                Dim gy As Func(Of Object)
                Dim sx As Action(Of Object)
                Dim sy As Action(Of Object)
                Dim delta As Action(Of Double)
                Dim scl As Double
                If _functiontype(_curfn) = FunctionType.Cartesian Then
                    gx = New Func(Of Object)(Function() As Object
                                                 Return GetX(True)
                                             End Function)
                    gy = New Func(Of Object)(Function() As Object
                                                 Return GetY(True)
                                             End Function)
                    sx = New Action(Of Object)(Sub(x As Object)
                                                   SetX(x, True)
                                               End Sub)
                    sy = New Action(Of Object)(Sub(y As Object)
                                                   SetY(y, True)
                                               End Sub)
                    delta = New Action(Of Double)(Sub(x As Double)
                                                      DeltaX(x, True)
                                                  End Sub)
                    scl = _scale.X
                Else
                    gx = New Func(Of Object)(Function() As Object
                                                 Return GetY(True)
                                             End Function)
                    gy = New Func(Of Object)(Function() As Object
                                                 Return GetX(True)
                                             End Function)
                    sx = New Action(Of Object)(Sub(x As Object)
                                                   SetY(x, True)
                                               End Sub)
                    sy = New Action(Of Object)(Sub(y As Object)
                                                   SetX(y, True)
                                               End Sub)
                    delta = New Action(Of Double)(Sub(x As Double)
                                                      DeltaY(x, True)
                                                  End Sub)
                    scl = _scale.Y
                End If
                Dim oldx As Object = gx()
                Dim oldy As Object = gy()
                Dim res As Double = Eval(NpdTVal.Text.Trim(), True) + scl / 200
                sx(res)
                Dim mfn As String = _functions(_curfn)
                Dim count As Integer = 0
                While count <= 5
                    count += 1
                    Dim foundzerox As Double = Double.NaN
                    Dim foundzeroprec As Double = 100000
                    Dim prevy As Double = Double.NaN
                    Dim increase As Integer = -2
                    Dim curiszero As Boolean = Math.Abs(res - 0) < 0.001
                    Dim startdetail As Double = Double.NaN
                    Dim pco(_functions.Count) As Double
                    For i As Integer = 0 To _functions.Count - 1
                        pco(i) = Double.NaN
                    Next

                    While CDbl(gx()) < res + scl * primary_range
                        delta(scl / 1000)
                        sx(Math.Round(CDbl(gx()), 4))
                        Dim o As Double = Eval(mfn, True)
                        Dim pi As Integer = increase
                        If _functions.Count <= 10 Then
                            For i As Integer = 0 To _functions.Count - 1
                                If i = _curfn Then Continue For
                                If _functiontype(i) = FunctionType.Inverse Then
                                    sy(o)
                                    If Math.Abs(CDbl(gx()) - Eval(_functions(i), True)) < 0.001 Then
                                        startdetail = CDbl(gx()) - scl / 50
                                        Exit While
                                    End If
                                ElseIf _functiontype(i) = FunctionType.Cartesian Then
                                    Dim combine As String = "(" + mfn + ")-(" + _functions(i) + ")"
                                    Dim co As Double = Eval(combine, True)
                                    If Not Double.IsNaN(co) AndAlso Not Double.IsInfinity(co) AndAlso Not Double.IsNaN(pco(i)) AndAlso pco(i) * co <= 0 AndAlso Math.Abs(pco(i) - co) < 5000 * _scale.Y Then
                                        startdetail = CDbl(gx()) - scl / 50
                                        pco(i) = co
                                        Exit While
                                    End If
                                    pco(i) = co
                                End If
                            Next
                        End If

                        If Not Double.IsNaN(prevy) Then
                            If o > prevy Then
                                increase = 1
                            ElseIf o < prevy Then
                                increase = -1
                            Else
                                increase = 0
                            End If
                            If pi <> increase AndAlso pi <> -2 Then
                                startdetail = CDbl(gx()) - scl / 100
                                Exit While
                            End If
                        End If

                        If o * prevy <= 0 AndAlso Math.Abs(o - prevy) < 5000 * scl Then
                            startdetail = CDbl(gx()) - scl / 100
                            Exit While
                        ElseIf Math.Abs(o - prevy) < 0.00001 Then
                            startdetail = CDbl(gx()) - scl / 100
                            Exit While
                        End If
                        prevy = o
                    End While
                    If Not Double.IsNaN(startdetail) Then
                        sx(startdetail)
                        foundzerox = Double.NaN
                        foundzeroprec = 100000
                        prevy = Double.NaN
                        For i As Integer = 0 To _functions.Count - 1
                            pco(i) = Double.NaN
                        Next
                        increase = -2
                        curiszero = Math.Abs(res - 0) < 0.001
                        Dim found As Boolean = False
                        While CDbl(gx()) < startdetail + scl / 10
                            sx(CDbl(gx()) - scl / 20000)
                            prevy = Eval(mfn, True)
                            sx(CDbl(gx()) + scl / 20000 + scl / 200000)
                            Dim o As Double = Eval(mfn, True)
                            Dim pi As Integer = increase

                            If _functions.Count <= 10 AndAlso _functions.Count > 1 Then
                                For i As Integer = 0 To _functions.Count - 1
                                    If i = _curfn OrElse _functions(i).Equals(_functions(_curfn)) Then Continue For
                                    If _functiontype(i) = FunctionType.Inverse Then
                                        sy(o)
                                        If Math.Abs(CDbl(gx()) - Eval(_functions(i), True)) < 0.00001 Then
                                            found = True
                                            Exit For
                                        End If
                                    ElseIf _functiontype(i) = FunctionType.Cartesian Then
                                        Dim combine As String = "(" + mfn + ")-(" + _functions(i) + ")"
                                        Dim co As Double = Eval(combine, True)
                                        If Not Double.IsNaN(co) AndAlso Not Double.IsInfinity(co) AndAlso Not Double.IsNaN(pco(i)) AndAlso pco(i) * co <= 0 AndAlso Math.Abs(pco(i) - co) < 5000 * _scale.Y Then
                                            found = True
                                            pco(i) = co
                                            Exit For
                                        End If
                                        pco(i) = co
                                    End If
                                Next
                                If found Then
                                    NpdTVal.Text = Math.Round(CDbl(gx()), 3).ToString()
                                    LbTVal.Text &= vbCrLf & "Intersection: " & NpdTVal.Text
                                    Exit While
                                End If
                            End If

                            If Not Double.IsNaN(prevy) Then
                                If o > prevy Then
                                    increase = 1
                                ElseIf o < prevy Then
                                    increase = -1
                                Else
                                    increase = 0
                                End If
                                If pi <> increase AndAlso pi <> -2 Then
                                    NpdTVal.Text = Math.Round(CDbl(gx()), 3).ToString()
                                    LbTVal.Text &= vbCrLf & If(Math.Abs(prevy - o) > 5000, "Asymptote: ", If(increase = -1, "Maximum: ", "Minimum: ")) & NpdTVal.Text
                                    found = True
                                    Exit While
                                End If
                            End If

                            If o * prevy < 0 Then
                                NpdTVal.Text = Math.Round(CDbl(gx()), 3).ToString()
                                LbTVal.Text &= vbCrLf & "Zero: " & NpdTVal.Text
                                found = True
                                Exit While
                            End If
                        End While
                        If CDbl(gx()) >= startdetail + scl * 10 OrElse String.IsNullOrWhiteSpace(mfn) Then
                            If LbTVal.Text.Contains(vbCrLf) Then LbTVal.Text = LbTVal.Text.Remove(LbTVal.Text.IndexOf(vbCrLf))
                            LbTVal.Text &= vbCrLf & "Nothing Found."
                            LbTVal.BackColor = Color.FromArgb(60, 60, 60)
                        Else
                            LbTVal.BackColor = Color.Brown
                            If Not found Then Continue While
                        End If
                        Exit While
                    Else
                        count = 6
                        Exit While
                    End If
                End While
                If count > 5 Then
                    If LbTVal.Text.Contains(vbCrLf) Then LbTVal.Text = LbTVal.Text.Remove(LbTVal.Text.IndexOf(vbCrLf))
                    LbTVal.Text &= vbCrLf & "Nothing Found."
                    LbTVal.BackColor = Color.FromArgb(60, 60, 60)
                End If
                sx(oldx)
                sy(oldy)
            Catch 'ex As Exception
            End Try
        End Sub

        Private Sub TraceSpecialPolar()
            Dim oldt As Object = GetT(True)
            Dim x As Double = Eval(NpdTVal.Text.Trim(), True) + 0.01
            Dim ct As Integer = 0
            While ct <= 5
                ct += 1
                Dim startdetail As Double = Double.NaN
                For mid As Double = x To x + Math.PI / 3 * 2 Step Math.PI / 900
                    SetT(mid Mod Math.PI * 2, True)
                    Dim fn As String = _functions(_curfn)
                    fn = fn.Remove(fn.IndexOf("["c)).Trim()
                    Dim lstr As New List(Of Double)
                    lstr.Add(0)
                    For i As Double = -Math.PI / 5000 To Math.PI / 5000 Step Math.PI / 5000
                        SetT(mid + i, True)
                        lstr.Add(Eval(fn, True))
                    Next
                    Dim mr As Double = lstr(2)
                    If _functions.Count <= 10 Then
                        For i As Integer = 0 To _functions.Count - 1
                            If i = _curfn Then Continue For
                            If _functiontype(i) = FunctionType.OriginRay Then
                                If Math.Abs(mid - Eval(_functions(i), True)) < _scale.X / 10 Then
                                    startdetail = mid - 0.01
                                    Exit For
                                End If
                            ElseIf _functiontype(i) = FunctionType.Polar Then

                                If Math.Abs(Eval(_functions(i), True) - mr) <
                                                _scale.X / 5 Then
                                    startdetail = mid - 0.01
                                    Exit For
                                End If
                            End If
                        Next
                    End If
                    If lstr(3) * lstr(1) < 0 OrElse Math.Abs(lstr(2)) < 0.005 Then
                        If Math.Abs(x - lstr(1)) <= 5000 Then
                            startdetail = mid - 0.01
                            Exit For
                        End If
                    End If
                Next
                If startdetail = Double.NaN Then
                    Exit While
                End If
                For mid As Double = startdetail To startdetail + Math.PI / 12 Step Math.PI / 30000
                    SetT(mid Mod Math.PI * 2, True)
                    Dim fn As String = _functions(_curfn)
                    fn = fn.Remove(fn.IndexOf("["c)).Trim()
                    Dim lstr As New List(Of Double)
                    lstr.Add(0)
                    For i As Double = -Math.PI / 15000 To Math.PI / 15000 Step Math.PI / 15000
                        SetT((mid + i) Mod Math.PI * 2, True)
                        lstr.Add(Eval(fn, True))
                    Next
                    Dim mr As Double = lstr(2)
                    If _functions.Count <= 10 Then
                        For i As Integer = 0 To _functions.Count - 1
                            If i = _curfn Then Continue For
                            If _functiontype(i) = FunctionType.OriginRay Then
                                If Math.Abs(mid - Eval(_functions(i), True)) < _scale.X / 100 Then
                                    NpdTVal.Text = Math.Round(CDbl(GetT()), 3).ToString()
                                    LbTVal.Text &= vbCrLf & "Intersection: " & NpdTVal.Text
                                    LbTVal.BackColor = Color.Brown
                                    Exit For
                                End If
                            ElseIf _functiontype(i) = FunctionType.Polar Then

                                If Math.Abs(Eval(_functions(i), True) - mr) <
                                                _scale.X / 50 Then
                                    NpdTVal.Text = Math.Round(CDbl(GetT()), 3).ToString()
                                    LbTVal.Text &= vbCrLf & "Intersection: " & NpdTVal.Text
                                    LbTVal.BackColor = Color.Brown
                                    Exit For
                                End If
                            End If
                        Next
                    End If
                    If lstr(3) * lstr(1) < 0 OrElse Math.Abs(lstr(2)) < 0.00005 Then
                        If Math.Abs(x - lstr(1)) <= 5000 Then
                            NpdTVal.Text = Math.Round(CDbl(GetT(True)), 3).ToString()
                            LbTVal.Text &= vbCrLf & "Zero: " & NpdTVal.Text
                            LbTVal.BackColor = Color.Brown
                            Exit Sub
                        End If
                    End If
                Next
            End While
            If LbTVal.Text.Split(vbLf(0)).Length >= 2 Then
                LbTVal.Text = LbTVal.Text.Remove(LbTVal.Text.LastIndexOf(vbCrLf))
            End If
            LbTVal.Text &= vbCrLf & "Nothing Found."
            LbTVal.BackColor = Color.FromArgb(60, 60, 60)
            SetT(oldt, True)
        End Sub

        Private Sub TraceSpecial(Optional primary_range As Integer = 26)
            _eval2 = _eval.DeepCopy()
            _eval2.SignificantMode = False
            Select Case _functiontype(_curfn)
                Case FunctionType.Cartesian, FunctionType.Inverse
                    TraceSpecialCartesian(primary_range)
                Case FunctionType.Polar
                    TraceSpecialPolar()
            End Select
            Canvas.Invalidate()
        End Sub

        Private Sub BtnTNext_Click(sender As Object, e As EventArgs) Handles BtnTNext.Click
            TraceSpecial()
        End Sub

        Private Sub BtnTrace_Enter(sender As Object, e As EventArgs) Handles BtnTrace.Enter, BtnTNext.Enter
            NpdTVal.Focus()
        End Sub
        Private Sub BtnTrace_Click(sender As Object, e As EventArgs) Handles BtnTrace.Click
            If BtnTrace.Text = "Trace" Then
                BtnTrace.BackColor = Color.Brown
                BtnTrace.FlatAppearance.MouseOverBackColor = Color.Brown
                BtnTrace.FlatAppearance.MouseDownBackColor = Color.FromArgb(170, 60, 60)
                BtnTrace.Text = "✗"
                If _functiontype(_curfn) = FunctionType.Differential Then
                    NpdTVal.Text = "(0, 0)"
                Else
                    NpdTVal.Text = "0"
                End If
                _traceOn = True
                Trace()
                PnlTrace.Show()
                NpdTVal.Select(0, NpdTVal.Text.Length)
                NpdTVal.Focus()
            Else
                BtnTrace.Text = "Trace"
                BtnTrace.BackColor = Color.FromArgb(160, 70, 10)
                BtnTrace.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 70, 10)
                BtnTrace.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 80, 20)
                _traceOn = False
                PnlTrace.Hide()
                Tb.Focus()
            End If
            Canvas.Invalidate()
        End Sub

        Private Sub npdTVal_ValueChanged(sender As Object, e As EventArgs) Handles NpdTVal.TextChanged
            LbTVal.BackColor = Color.FromArgb(60, 60, 60)
            Trace()
        End Sub

        Private Sub tmrTraceUpdate_Tick(sender As Object, e As EventArgs) Handles TmrTraceUpdate.Tick
            TraceUpdateCoords(Canvas.PointToClient(Cursor.Position))
            Canvas.Invalidate()
        End Sub

        Private Sub npdTVal_KeyDown(sender As Object, e As KeyEventArgs) Handles NpdTVal.KeyDown
            Try
                If e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down Then
                    SetX(Double.NaN)
                    If e.KeyCode = Keys.Up Then
                        NpdTVal.Text = (Math.Ceiling(Eval(NpdTVal.Text) * 10) / 10 + 0.1).ToString
                    Else
                        NpdTVal.Text = (Math.Floor(Eval(NpdTVal.Text) * 10) / 10 - 0.1).ToString
                    End If
                    NpdTVal.SelectionStart = NpdTVal.Text.Length
                    e.SuppressKeyPress = True
                ElseIf e.KeyCode = Keys.Enter Then
                    BtnTNext.PerformClick()
                ElseIf e.KeyCode = Keys.Escape OrElse (e.KeyCode = Keys.T AndAlso e.Alt) Then
                    BtnTrace.PerformClick()
                ElseIf e.KeyCode = Keys.A AndAlso e.Control Then
                    NpdTVal.SelectAll()
                    e.SuppressKeyPress = True
                End If
            Catch ex As Exception
            End Try
        End Sub

        Private Sub BtnScale_Click(sender As Object, e As EventArgs) Handles BtnScale.Click
            PnlWindow.Visible = Not PnlWindow.Visible
            If PnlWindow.Visible Then
                'scaleX.Value = Math.Max(Math.Min(1000 - Math.Abs(CInt((Math.Log10(_scale.X) + 3) ^ 1.17647058824 * 100)), 1000), 0)
                'scaleY.Value = Math.Max(Math.Min(1000 - Math.Abs(CInt((Math.Log10(_scale.Y) + 3) ^ 1.17647058824 * 100)), 1000), 0)
                PnlWindow.Left = CInt(Split.Panel1.Width / 2 - PnlWindow.Width / 2)
                PnlWindow.Top = CInt(Split.Panel1.Height / 2 - PnlWindow.Height / 2)
                TbWBot.Text = Math.Round(ccfy(cscy(Canvas.Height)), 4).ToString()
                TbWTop.Text = Math.Round(ccfy(cscy(0)), 4).ToString()
                TbWLft.Text = Math.Round(ccfx(cscx(0)), 4).ToString()
                TbWRht.Text = Math.Round(ccfx(cscx(Canvas.Width)), 4).ToString()
                TbWTop.Focus()
            Else
                Tb.Focus()
            End If
        End Sub

        'Private Sub scaleY_Enter(sender As Object, e As EventArgs) Handles scaleY.Enter, scaleX.Enter
        '    canvas.Focus()
        'End Sub

        'Private Sub scaleY_Scroll(sender As Object, e As EventArgs) Handles scaleY.Scroll
        '    Dim prevscl As Double = _scale.Y
        '    _scale.Y = CSng(10 ^ (-3 + (10 - scaleY.Value / 100) ^ 0.85))
        '    If _scale.Y < 0.003 Then _scale.Y = 0.003
        '    TbWBot.Text = Math.Round(ccfy(cscy(canvas.Height)), 4).ToString()
        '    TbWTop.Text = Math.Round(ccfy(cscy(0)), 4).ToString()
        '    TbWLft.Text = Math.Round(ccfx(cscx(0)), 4).ToString()
        '    TbWRht.Text = Math.Round(ccfx(cscx(canvas.Width)), 4).ToString()
        '    preview = True
        '    redrawCt = 2
        'End Sub

        'Private Sub scaleX_Scroll(sender As Object, e As EventArgs) Handles scaleX.Scroll
        '    Dim prevscl As Double = _scale.X
        '    _scale.X = CSng(10 ^ (-3 + (10 - scaleX.Value / 100) ^ 0.85))
        '    If _scale.X < 0.003 Then _scale.X = 0.003
        '    TbWBot.Text = Math.Round(ccfy(cscy(canvas.Height)), 4).ToString()
        '    TbWTop.Text = Math.Round(ccfy(cscy(0)), 4).ToString()
        '    TbWLft.Text = Math.Round(ccfx(cscx(0)), 4).ToString()
        '    TbWRht.Text = Math.Round(ccfx(cscx(canvas.Width)), 4).ToString()
        '    preview = True
        '    redrawCt = 2
        'End Sub

        Private Sub BtnScale_MouseUp(sender As Object, e As MouseEventArgs) Handles BtnScale.MouseUp
            If e.Button = MouseButtons.Right AndAlso Not (_scale.X = 1 And _scale.Y = 1 And _loc.X = 0 And _loc.Y = 0) Then
                _scale.X = 1
                _scale.Y = 1
                'scaleX.Value = 1000 - Math.Abs(CInt((Math.Log10(_scale.X) + 3) ^ 1.17647058824 * 100))
                'scaleY.Value = 1000 - Math.Abs(CInt((Math.Log10(_scale.Y) + 3) ^ 1.17647058824 * 100))
                _loc = New Coord(0, 0)
            ElseIf e.Button = MouseButtons.Middle Then
                _scale.X = 1
                _scale.Y = 1
                _loc = New Coord(-CSng(Canvas.Width / 2), -CSng(Canvas.Height / 2))
            End If
        End Sub

        'Private Sub ScaleChange() Handles scaleY.MouseUp, scaleX.MouseUp, scaleY.KeyUp, scaleX.KeyUp
        'End Sub
        Dim traceSupport As FunctionType() = {FunctionType.Cartesian, FunctionType.Inverse, FunctionType.Parametric,
        FunctionType.Polar, FunctionType.Differential}
        Private Sub UpdateFunc()
            If _functiontype(_curfn) = FunctionType.Differential Then
                NpdTVal.Text = "(0, 0)"
                BtnTNext.Enabled = False
            Else
                NpdTVal.Text = "0"
                If _functiontype(_curfn) = FunctionType.Parametric Then
                    BtnTNext.Enabled = False
                Else
                    BtnTNext.Enabled = True
                End If
            End If
            Tb.Text = _functions(_curfn)
            LbFx.Text = NameFromFuncId(_curfn) & " = "
            Tb.SelectionStart = Tb.Text.Length
            If traceSupport.Contains(_functiontype(_curfn)) Then
                BtnTrace.Show()
                If _traceOn Then Trace()
            Else
                BtnTrace.Hide()
                If _traceOn Then BtnTrace_Click(BtnTrace, New EventArgs())
            End If
            UpdateFnLabelColor()
            SetPnlSelect(_functiontype(_curfn))
            PnlFnType.Hide()
            NpdTVal.Select(0, NpdTVal.Text.Length)
        End Sub
        Private Sub ResetFunc()
            If _functiontype(_curfn) = FunctionType.Differential Then
                NpdTVal.Text = "(0, 0)"
                BtnTNext.Enabled = False
            Else
                NpdTVal.Text = "0"
                If _functiontype(_curfn) = FunctionType.Parametric Then
                    BtnTNext.Enabled = False
                Else
                    BtnTNext.Enabled = True
                End If
            End If
            LbFx.Text = NameFromFuncId(_curfn) & " = "
            Dim showcaselst As String()
            Dim ft As FunctionType = _functiontype(_curfn)
            ' lists of 'interesting' functions
            Select Case ft
                Case FunctionType.Cartesian, FunctionType.Inverse
                    showcaselst = {"x", "-x", "x^2", "-x^2", "0.2*x^3+0.5*x^2-2*x-2", "abs(x)", "-sqrt(x)",
                    "2*sinr(0.5*x)", "tanr(x)", "floor(x)", "cscr(x)", "sqrt(25-x^2)", "x^4", "x mod 4", "x^3",
                    "10*x", "-10*x", "-0.6*x", "1/x", "2/x", "3/x", "10/x^2", "1/x^2", "secr(x)", "ceil(x)",
                    "cotr(x)", "round(x)", "-floor(x)", "(x^2-1)/x", "(x^3-2)/x^2", "-x^4", "-5*asinr(0.2x)",
                    "-acosr(0.2x)", "1/x^3", "1/x^4", "1/floor(x)", "-sqrt(25-x^2)", "1/(0.5*x^2)", "x*sinr(x)",
                    "sqrt(x)", "-sinr(x)", "-cosr(x)", "-tanr(x)", "atanr(x)", "5*asinr(0.2*x)", "acosr(0.1*x)",
                    "sinh(x)", "sqrt(-x)", "-sqrt(-x)", "(x^2+x+1)/x", "-2/(x^2-3x-1)", "-xsinr(x)",
                    "-x*cosr(x)", "e^x", "e^(-x)", "0.5e^(0.5x)", "ln(x)", "log(x,3)", "log2(x)", "log10(x)",
                    "-atanr(x)", "-round(x)", "0.5*x^2", "x mod 3", "-x mod 2", "-x^2+2", "x mod 1", "x^2 -2",
                    "0.1*x^3", "-0.1*x^3", "-2/x", "-3/x", "-4/x", "sinr(x)", "x*cosr(x)", "90x", "-90x",
                    "(x^3+1)/x", "(x^2+1)/x^2", "(x^3+x^2-x-2)/(x^2+x)", "(x^2+x+2)/(x^2-x+2)", "x!", "-x!",
                    "(-x)!", "0.5x!", "-0.5x!", "sgn(x)", "x << 1", "x >> 1", "x*cotr(x)", "x*tanr(x)",
                    "-1/x^2", "-xtanr(x)", "x^2sinr(x)"}
                    If _functiontype(_curfn) = FunctionType.Inverse Then
                        For i As Integer = 0 To showcaselst.Length - 1
                            showcaselst(i) = showcaselst(i).Replace("x", "y")
                        Next
                    End If
                Case FunctionType.Parametric
                    showcaselst = {"<6sinr(t)^3, 3cosr(t)^3> [0<t<2pi]", "<3sinr(t)^3, 6cosr(t)^3> [0<t<2pi]",
                    "<5sinr(t)^3, 5cosr(t)^3> [0<t<2pi]", "<6*sinr(7πt), 5cosr(5πt)> [0<t<2]",
                    "<5sinr(7πt), 5cosr(5πt)> [0<t<2]", "<5*cosr(t), 5sinr(t)> [0<t<2π]",
                    "<6cosr(t), 3sinr(t)> [0<t<2π]", "<3*cosr(t), 6sinr(t)> [0<t<2π]",
                    "<4cosr(t), 4sinr(t)> [0<t<2π]", "<6*sinr(20πt), 15cosr(10πt)> [0<t<2π]",
                    "<10cosr(10πt), 10sinr(20πt)> [0<t<2π]",
                    "<8sinr(t)^3, (13cosr(t)-5cosr(2t)-3cosr(3t)-cosr(4t))/2> [0<t<2π]",
                    "<t, -t> [-1000<t<1000]", "<t,t> [-1000<t<1000]", "<15cosr(10πt), 6sinr(20πt)> [0<t<0.1]",
                    "<15cosr(10πt), 6sinr(20πt)> [0<t<0.2]",
                    "<8sinr(t)^3, (13cosr(t)-5cosr(2t)-3cosr(3t)-cosr(4t))/2> [0<t<2π]"}
                Case FunctionType.Polar
                    showcaselst = {"5-5sinr(θ) [0<θ<2π]", "5-5sinr(θ) [0<θ<2π]",
                    "5-8*sinr(θ) [0<θ<2π]", "5-9*sinr(θ) [0<θ<2π]", "5-9*cosr(θ) [0<θ<2π]",
                    "0.1*θ [0<θ<100π]", "0.2θ [0<θ<100π]", "5 [0<θ<2π]", "5*sinr(2*θ) [0<θ<2π]",
                    "5*sinr(3*θ) [0<θ<2π]", "5cosr(4θ) [0<θ<2π]", "5*cosr(6*θ) [0<θ<2π]",
                    "sqrt(100*cosr(2*θ)) [0<θ<2*π]", "sqrt(100*sinr(2*θ)) [0<θ<2*π]", "0.6*sqrt(θ) [0<θ<100*π]",
                    "10^(cosr(-π*θ)) [0<θ<98*π]", "8^(cosr(-π*θ)) [0<θ<14*π]", "1/(sinr(θ)-cosr(θ)) [0<θ<2*π]",
                    "2/(sinr(θ)-cosr(θ)) [0<θ<2*π]"}
                Case FunctionType.Differential
                    showcaselst = {"x/y", "-x/y", "y/x", "xy", "-y/x", "x+y", "x-y", "x-xy", "x", "y", "e^(-y)",
                    "2x/y", "e^(-x)"}
                Case FunctionType.OriginRay
                    showcaselst = {"0", "π", "π/2", "3*π/2", "π/3", "2*π/3", "4*π/3", "5*π/3", "π/4", "3*π/4",
                    "5*π/4", "7*π/4", "π/6", "5*π/6", "7*π/6", "11*π/6", "-π/2", "-π/4", "-π/6", "-5π/6", "-3*π/4",
                    "1", "-1"}
                Case Else
                    Return ' invalid function type
            End Select
            If traceSupport.Contains(_functiontype(_curfn)) Then
                If _traceOn Then Trace()
                BtnTrace.Show()
            Else
                BtnTrace.Hide()
                If _traceOn Then BtnTrace_Click(BtnTrace, New EventArgs())
            End If

            ' get a random showcase function
            Dim randomizer As New Random()
            Dim rand As Integer = randomizer.Next(showcaselst.Length)
            Tb.Text = showcaselst(rand)
            _functions(_curfn) = Tb.Text

            ' select for easier editing
            If Tb.Text.Contains(" [") Then
                Tb.SetSelection(0, Tb.Text.LastIndexOf("["c) - 1)
            Else
                Tb.SelectAll()
            End If

            NpdTVal.Select(0, NpdTVal.Text.Length)
            SetPnlSelect(_functiontype(_curfn))
            UpdateFnLabelColor()
        End Sub
        Private Sub Tb_KeyDown(sender As Object, e As KeyEventArgs) Handles Tb.KeyDown, Me.KeyDown
            If e.Control AndAlso e.KeyCode = Keys.A Then
                If e.KeyCode = Keys.A Then
                    Tb.SelectAll()
                End If
            End If
            If e.Alt Then
                If e.KeyCode = Keys.Up AndAlso _curfn > 0 Then
                    _curfn -= 1
                    EnablePrevNextFnBtns(_curfn > 0, True)
                    UpdateFunc()
                ElseIf e.KeyCode = Keys.Down AndAlso _curfn < _functions.Count - 1 Then
                    _curfn += 1
                    EnablePrevNextFnBtns(True, _curfn <> _functions.Count - 1)
                    UpdateFunc()
                ElseIf e.KeyCode = Keys.Oemplus OrElse e.KeyCode = Keys.A Then
                    BtnAdd.PerformClick()
                ElseIf e.KeyCode = Keys.T Then
                    BtnTrace.PerformClick()
                ElseIf e.KeyCode = Keys.O Then
                    LbFx_MouseDown(LbFx, New MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))
                ElseIf e.KeyCode = Keys.S OrElse e.KeyCode = Keys.W Then
                    BtnScale.PerformClick()
                ElseIf e.KeyCode = Keys.M And e.Control Then
                    e.SuppressKeyPress = True
                End If
            ElseIf PnlFnType.Visible Then
                If e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down Then
                    Dim num As Integer
                    If e.KeyCode = Keys.Up Then
                        num = _functiontype(_curfn) - 1
                    Else
                        num = _functiontype(_curfn) + 1
                    End If
                    If num <= 0 Then num = FunctionType.OriginRay
                    If num > FunctionType.OriginRay Then num = FunctionType.Cartesian
                    _functiontype(_curfn) = CType(num, FunctionType)
                    SetPnlSelect(num)
                    ResetFunc()
                Else
                    PnlFnType.Hide()
                End If
            End If
        End Sub

        Private Sub BtnPrevFn_Click(sender As Object, e As EventArgs) Handles BtnPrevFn.Click, BtnNextFn.Click
            If DirectCast(sender, Button).Name = "BtnNextFn" Then
                If _curfn < _functions.Count - 1 Then _curfn += 1
                EnablePrevNextFnBtns(True, _curfn <> _functions.Count - 1)
            Else
                If _curfn > 0 Then _curfn -= 1
                EnablePrevNextFnBtns(_curfn > 0, True)
            End If
            UpdateFunc()
        End Sub

        Private Sub split_Panel2_SizeChanged(sender As Object, e As EventArgs) Handles Split.Panel2.SizeChanged
            BtnPrevFn.Top = 0
            BtnPrevFn.Height = Split.Panel2.Height \ 2 + 2
            BtnNextFn.Top = Split.Panel2.Height \ 2
            BtnNextFn.Height = Split.Panel2.Height \ 2 + 2
            PnlFnType.Top = Split.SplitterDistance - PnlFnType.Height + 3
            PnlFnType.Left = BtnPrevFn.Width + Split.Panel2.Left
        End Sub

        Private Sub BtnFnTypeCancel_Click(sender As Object, e As EventArgs) Handles BtnFnDel.Click
            Tb.Focus()
            If MsgBox("Are you sure you want to delete this function?" & vbCrLf _
                & "All functions below will be shifted up one spot.", MsgBoxStyle.YesNo, "Delete Function") = MsgBoxResult.Yes Then
                Try
                    _functions.RemoveAt(_curfn)
                    _functiontype.RemoveAt(_curfn)
                    If _curfn >= _functions.Count Then
                        _curfn -= 1
                    End If
                    If _curfn = _functions.Count - 1 Then BtnNextFn.Enabled = False
                    If _curfn = 0 Then BtnPrevFn.Enabled = False
                    UpdateFunc()
                Catch 'ex As Exception
                    'MsgBox(ex.ToString())
                End Try
            End If
        End Sub
        Private Sub SetPnlSelect(id As Integer)
            For Each c As Control In PnlFnTypeSelector.Controls
                If TypeOf c Is Panel AndAlso c.Tag IsNot Nothing Then
                    Try
                        If Integer.Parse(c.Tag.ToString()) = id Then
                            c.BackColor = Color.FromArgb(160, 70, 10)
                            c.Cursor = Cursors.Arrow
                        Else
                            c.BackColor = Color.FromArgb(60, 60, 60)
                            c.Cursor = Cursors.Hand
                        End If
                    Catch
                    End Try
                End If
            Next
        End Sub
        Private Sub LbOpt_MouseDown(sender As Object, e As MouseEventArgs) Handles PnlOptPolar.MouseDown,
        PnlOptParametric.MouseDown, PnlOptDifferential.MouseDown, PnlOptCartesian.MouseDown,
        LbOptPolarR.MouseDown, LbOptPolarL.MouseDown, LbOptParametricR.MouseDown, LbOptParametricL.MouseDown,
        LbOptDifferentialR.MouseDown, LbOptDifferentialL.MouseDown, LbOptCartesianR.MouseDown,
        LbOptCartesianL.MouseDown,
        PnlOptOriginRay.MouseDown, LbOptOriginRayL.MouseDown, LbOptOriginRayR.MouseDown, PnlOptInverse.MouseDown,
        LbOptInverseL.MouseDown, LbOptInverseR.MouseDown

            Dim id As Integer = Integer.Parse(DirectCast(sender, Control).Tag.ToString())
            If _functiontype(_curfn) = DirectCast(id, FunctionType) Then Exit Sub
            _functiontype(_curfn) = DirectCast(id, FunctionType)
            SetPnlSelect(id)
            ResetFunc()
            PnlFnType.Hide()

        End Sub

        Private Sub UpdateFnLabelColor()
            Dim c As Color = ColorFromFuncId(_curfn)
            LbFx.ForeColor = Color.FromArgb((c.R + 255) \ 2, (c.G + 255) \ 2, (c.B + 255) \ 2)
        End Sub

        Private Sub LbFx_MouseEnter(sender As Object, e As EventArgs) Handles LbFx.MouseEnter
            LbFx.ForeColor = ColorFromFuncId(_curfn)
        End Sub

        Private Sub LbFx_MouseLeave(sender As Object, e As EventArgs) Handles LbFx.MouseLeave
            UpdateFnLabelColor()
        End Sub

        Private Sub BtnWCancel_Click(sender As Object, e As EventArgs) Handles BtnWCancel.Click, BtnWClose.Click
            BtnScale.PerformClick()
        End Sub

        Private Sub BtnWOK_Click(sender As Object, e As EventArgs) Handles BtnWOK.Click
            Try
                Dim r As Double = Eval(TbWRht.Text)
                Dim l As Double = Eval(TbWLft.Text)
                Dim t As Double = Eval(TbWTop.Text)
                Dim b As Double = Eval(TbWBot.Text)
                If r <= l + 0.01 OrElse t <= b Then
                    MsgBox("Invalid range: maximum values must EXCEED minimum values.", MsgBoxStyle.Exclamation, "Invalid range")
                    Return
                Else
                    'Dim offsetx As Double = (_loc.X + WID / 2) Mod (hstep * SCREENFACT / _scale.X)
                    Dim sx As Double = CSng((r - l) * (CDbl(SCREENFACT) / Canvas.Width))
                    Dim sy As Double = CSng((t - b) * (CDbl(SCREENFACT) / Canvas.Height))
                    Dim nsx As Double = SelectScale(sx)
                    Dim nsy As Double = SelectScale(sy)
                    If nsx < MINSCALE OrElse nsy < MINSCALE Then
                        MsgBox("Range is below the lower limit. The axis scale must be no smaller than " & MINSCALE & ".", MsgBoxStyle.Exclamation, "Range too small")
                        Return
                    ElseIf nsx > MAXSCALE OrElse nsy > MAXSCALE Then
                        MsgBox("Range is above the upper limit. The axis scale must be no larger than " & MAXSCALE & ".", MsgBoxStyle.Exclamation, "Range too large")
                        Return
                    End If
                    _scale.X = sx
                    _scale.Y = sy

                    'cfcx: Return CSng(x * (SCREENFACT / _scale.X) + _center.X + WID / 2)
                    _loc.X = -CSng((l + (r - l) / 2) * (SCREENFACT / _scale.X))
                    _loc.Y = -CSng((b + (t - b) / 2) * (SCREENFACT / _scale.Y))
                    BtnScale.PerformClick()
                End If
            Catch 'ex As Exception
                MsgBox("Invalid expression format specified! Please make sure you didn't mistype anything.", MsgBoxStyle.Exclamation, "Invalid fFormat")
            End Try
        End Sub

        Private Sub TbWLft_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TbWTop.KeyPress, TbWRht.KeyPress, TbWLft.KeyPress, TbWBot.KeyPress
            If e.KeyChar = Convert.ToChar(Keys.Enter) Then
                BtnWOK.PerformClick()
                e.Handled = True
            End If
        End Sub

        Private Sub TbWTop_KeyDown(sender As Object, e As KeyEventArgs) Handles TbWTop.KeyDown, TbWRht.KeyDown, TbWLft.KeyDown, TbWBot.KeyDown ', scaleY.KeyDown, scaleX.KeyDown
            If e.KeyCode = Keys.Escape OrElse (e.KeyCode = Keys.W AndAlso e.Alt) OrElse (e.KeyCode = Keys.S AndAlso e.Alt) Then
                BtnWClose.PerformClick()
            ElseIf e.KeyCode = Keys.Up OrElse e.KeyCode = Keys.Down
                Try
                    Dim cTb As TextBox = DirectCast(sender, TextBox)
                    cTb.Text = (Math.Floor(Eval(cTb.Text) * 10) / 10 +
                    If(e.KeyCode = Keys.Up, 0.1, -0.1)).ToString
                    e.SuppressKeyPress = True
                Catch
                End Try
            End If
        End Sub

        Dim preview As Boolean = False

        Dim Pnlppt As Point
        Dim Pnldragging As Boolean
        Private Sub PnlWindow_MouseDown(sender As Object, e As MouseEventArgs) Handles PnlWindow.MouseDown,
        PnlWHeader.MouseDown, LbWindow.MouseDown, LbWTop.MouseDown, LbWRht.MouseDown, LbWLft.MouseDown, LbWBot.MouseDown, LbWLogo.MouseDown

            Pnldragging = True
            Pnlppt = e.Location
        End Sub
        Private Sub PnlWindow_MouseMove(sender As Object, e As MouseEventArgs) Handles PnlWindow.MouseMove,
        PnlWHeader.MouseMove, LbWindow.MouseMove, LbWTop.MouseMove, LbWRht.MouseMove, LbWLft.MouseMove, LbWBot.MouseMove, LbWLogo.MouseMove
            If Pnldragging Then
                PnlWindow.Left = e.X - Pnlppt.X + PnlWindow.Left
                PnlWindow.Top = e.Y - Pnlppt.Y + PnlWindow.Top
            End If
        End Sub

        Private Sub PnlWindow_MouseUp(sender As Object, e As MouseEventArgs) Handles PnlWindow.MouseUp,
        PnlWHeader.MouseUp, LbWindow.MouseUp, LbWTop.MouseUp, LbWRht.MouseUp, LbWLft.MouseUp, LbWBot.MouseUp, LbWLogo.MouseUp
            Pnldragging = False
        End Sub

        Private Sub Tb_TextChanged(sender As Object, e As EventArgs) Handles Tb.TextChanged
            _drct = 3
            tmrDelayRedraw.Start()
        End Sub
        Dim _drct As Integer = 0
        Private Sub tmrDelayRedraw_Tick(sender As Object, e As EventArgs) Handles tmrDelayRedraw.Tick
            If _drct <= 0 Then
                tmrDelayRedraw.Stop()
                UpdateGraph()
                If Not DrawWorker.IsBusy Then DrawWorker.RunWorkerAsync()
            Else
                _drct -= 1
            End If
        End Sub

        Private Sub DrawWorker_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs) Handles DrawWorker.DoWork
            While Not _ended
                Try
                    If preview Then
                        Redraw(True)
                        DrawWorker.ReportProgress(0)
                        Thread.Sleep(60)
                    Else
                        Redraw(False)
                        DrawWorker.ReportProgress(0)
                        Exit While
                    End If
                Catch 'ex As Exception
                End Try
            End While
        End Sub

        Private Sub DrawWorker_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles DrawWorker.ProgressChanged
            Dim tmp As Bitmap = _buffer
            _buffer = _tmpbuffer
            _tmpbuffer = tmp
            Canvas.Invalidate()
            'If e.ProgressPercentage <> 0 Then
            '    If Not DrawWorker.IsBusy Then
            '        DrawWorker.RunWorkerAsync()
            '    End If
            'End If
        End Sub

        Private Sub TmrHighQuality_Tick(sender As Object, e As EventArgs) Handles TmrHighQuality.Tick
            TmrHighQuality.Stop()
            preview = False
        End Sub
    End Class
End Namespace
