using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EjectionSeats.Physics;

namespace EjectionSeats
{
    public partial class MainWindow : Window
    {
        private List<TrajectoryPoint> trajectory = new List<TrajectoryPoint>();
        private double maxX, maxY, minY;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnSimulateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Считывание параметров
                double alt = double.Parse(txtHeight.Text);
                double speed = double.Parse(txtSpeed.Text);
                double pitch = double.Parse(txtPitch.Text);
                double bank = double.Parse(txtBank.Text);
                double mass = double.Parse(txtMass.Text);
                double area = double.Parse(txtArea.Text);
                double cx = double.Parse(txtCx.Text);
                double impulse = double.Parse(txtImpulse.Text);
                double burnTime = double.Parse(txtBurnTime.Text);
                double railAngle = double.Parse(txtRailAngle.Text);
                double chuteDelay = double.Parse(txtChuteDelay.Text);
                double chuteArea = double.Parse(txtChuteArea.Text);

                // Проверка на физическую адекватность
                if (alt < 0) throw new Exception("Высота не может быть отрицательной");
                if (speed < 0) throw new Exception("Скорость не может быть отрицательной");
                if (mass <= 0) throw new Exception("Масса должна быть положительной");

                // Запуск моделирования
                var model = new EjectionModel(alt, speed, pitch, bank, mass, area, cx,
                                               impulse, burnTime, railAngle, chuteDelay, chuteArea);
                model.RunSimulation(maxTime: 30.0, dt: 0.02);
                trajectory = model.Points;

                if (trajectory.Count == 0)
                    throw new Exception("Моделирование не дало результатов");

                // Поиск границ для масштабирования
                maxX = 0; maxY = 0; minY = double.MaxValue;
                foreach (var p in trajectory)
                {
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Y < minY && p.Y > 0) minY = p.Y;
                }
                if (minY == double.MaxValue) minY = 0;
                if (maxX < 10) maxX = 10;
                if (maxY < 10) maxY = 10;

                DrawTrajectory();

                txtStatus.Text = $"Расчёт завершён. Дальность: {trajectory[trajectory.Count - 1].X:F0} м, " +
                                 $"Время полёта: {trajectory[trajectory.Count - 1].Time:F1} с, " +
                                 $"Макс. высота: {maxY:F0} м";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка ввода или расчёта: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Ошибка";
            }
        }

        private void DrawTrajectory()
        {
            canvasTrajectory.Children.Clear();
            if (trajectory.Count < 2) return;

            double w = canvasTrajectory.ActualWidth;
            double h = canvasTrajectory.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Отступы для осей
            double marginLeft = 50;
            double marginRight = 30;
            double marginTop = 30;
            double marginBottom = 40;
            double plotW = w - marginLeft - marginRight;
            double plotH = h - marginTop - marginBottom;

            // Функция преобразования координат (X — дальность, Y — высота)
            Func<double, double, Point> map = (xWorld, yWorld) =>
            {
                double px = marginLeft + (xWorld / maxX) * plotW;
                double py = marginTop + plotH - (yWorld / maxY) * plotH;
                return new Point(px, py);
            };

            // Отрисовка сетки и осей
            DrawGridAndAxes(w, h, marginLeft, marginRight, marginTop, marginBottom, plotW, plotH);

            // Отрисовка траектории
            Polyline polyline = new Polyline();
            polyline.StrokeThickness = 2;
            polyline.Stroke = Brushes.Blue;

            bool parachuteDrawn = false;
            Polyline chutePolyline = null;

            for (int i = 0; i < trajectory.Count; i++)
            {
                var p = trajectory[i];
                Point pt = map(p.X, p.Y);

                if (!p.ParachuteDeployed)
                {
                    if (!parachuteDrawn)
                    {
                        polyline.Points.Add(pt);
                    }
                    else
                    {
                        // Начало новой линии после парашюта не нужно, так как зелёная линия уже рисуется отдельно
                    }
                }
                else
                {
                    if (!parachuteDrawn)
                    {
                        // Переключаемся на зелёную линию
                        parachuteDrawn = true;
                        chutePolyline = new Polyline();
                        chutePolyline.StrokeThickness = 2;
                        chutePolyline.Stroke = Brushes.Green;
                        // Добавляем последнюю точку синей линии как начало зелёной
                        if (i > 0)
                            chutePolyline.Points.Add(map(trajectory[i - 1].X, trajectory[i - 1].Y));
                        chutePolyline.Points.Add(pt);
                    }
                    else if (chutePolyline != null)
                    {
                        chutePolyline.Points.Add(pt);
                    }
                }
            }

            canvasTrajectory.Children.Add(polyline);
            if (chutePolyline != null)
                canvasTrajectory.Children.Add(chutePolyline);

            // Отметка точки катапультирования
            Point startPt = map(0, trajectory[0].Y);
            Ellipse startMarker = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Red };
            Canvas.SetLeft(startMarker, startPt.X - 4);
            Canvas.SetTop(startMarker, startPt.Y - 4);
            canvasTrajectory.Children.Add(startMarker);

            // Отметка точки приземления
            var lastPoint = trajectory[trajectory.Count - 1];
            Point endPt = map(lastPoint.X, 0);
            Ellipse endMarker = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Black };
            Canvas.SetLeft(endMarker, endPt.X - 4);
            Canvas.SetTop(endMarker, endPt.Y - 4);
            canvasTrajectory.Children.Add(endMarker);
        }

        private void DrawGridAndAxes(double w, double h, double left, double right, double top, double bottom, double plotW, double plotH)
        {
            // Оси
            Line xAxis = new Line { X1 = left, Y1 = top + plotH, X2 = left + plotW, Y2 = top + plotH, Stroke = Brushes.Black, StrokeThickness = 1 };
            Line yAxis = new Line { X1 = left, Y1 = top, X2 = left, Y2 = top + plotH, Stroke = Brushes.Black, StrokeThickness = 1 };
            canvasTrajectory.Children.Add(xAxis);
            canvasTrajectory.Children.Add(yAxis);

            // Подписи осей
            TextBlock xLabel = new TextBlock { Text = "Дальность (м)", FontSize = 10 };
            Canvas.SetLeft(xLabel, left + plotW - 40);
            Canvas.SetTop(xLabel, top + plotH + 5);
            canvasTrajectory.Children.Add(xLabel);

            TextBlock yLabel = new TextBlock { Text = "Высота (м)", FontSize = 10 };
            Canvas.SetLeft(yLabel, left - 35);
            Canvas.SetTop(yLabel, top);
            canvasTrajectory.Children.Add(yLabel);

            // Сетка по X
            int gridXCount = 5;
            for (int i = 0; i <= gridXCount; i++)
            {
                double xVal = maxX * i / gridXCount;
                double xPix = left + (xVal / maxX) * plotW;
                Line gridLine = new Line { X1 = xPix, Y1 = top, X2 = xPix, Y2 = top + plotH, Stroke = Brushes.LightGray, StrokeThickness = 0.5 };
                canvasTrajectory.Children.Add(gridLine);

                TextBlock lbl = new TextBlock { Text = $"{xVal:F0}", FontSize = 8 };
                Canvas.SetLeft(lbl, xPix - 10);
                Canvas.SetTop(lbl, top + plotH + 2);
                canvasTrajectory.Children.Add(lbl);
            }

            // Сетка по Y
            int gridYCount = 5;
            for (int i = 0; i <= gridYCount; i++)
            {
                double yVal = maxY * i / gridYCount;
                double yPix = top + plotH - (yVal / maxY) * plotH;
                Line gridLine = new Line { X1 = left, Y1 = yPix, X2 = left + plotW, Y2 = yPix, Stroke = Brushes.LightGray, StrokeThickness = 0.5 };
                canvasTrajectory.Children.Add(gridLine);

                TextBlock lbl = new TextBlock { Text = $"{yVal:F0}", FontSize = 8 };
                Canvas.SetLeft(lbl, left - 25);
                Canvas.SetTop(lbl, yPix - 6);
                canvasTrajectory.Children.Add(lbl);
            }
        }
    }
}