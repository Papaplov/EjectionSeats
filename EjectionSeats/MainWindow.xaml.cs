using System;
using System.Collections.Generic;
using System.Linq;
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
        private double maxX, maxY, minY, minX;
        private double burnTimeValue = 0.22;

        // Параметры масштабирования
        private double scale = 1.0;  // Единый масштаб для X и Y
        private double offsetX = 0;
        private double offsetY = 0;

        // Размеры области отображения
        private double marginLeft = 70;
        private double marginRight = 40;
        private double marginTop = 40;
        private double marginBottom = 60;
        private double plotW, plotH;

        public MainWindow()
        {
            InitializeComponent();
            this.SizeChanged += (s, e) => { if (trajectory.Count > 0) DrawTrajectory(); };
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
                burnTimeValue = double.Parse(txtBurnTime.Text);
                double railAngle = double.Parse(txtRailAngle.Text);
                double chuteDelay = double.Parse(txtChuteDelay.Text);
                double chuteArea = double.Parse(txtChuteArea.Text);

                // Проверка
                if (alt < 0) throw new Exception("Высота не может быть отрицательной");
                if (speed < 0) throw new Exception("Скорость не может быть отрицательной");
                if (mass <= 0) throw new Exception("Масса должна быть положительной");

                // Запуск моделирования
                var model = new EjectionModel(alt, speed, pitch, bank, mass, area, cx,
                                               impulse, burnTimeValue, railAngle, chuteDelay, chuteArea);
                model.RunSimulation(maxTime: 40.0, dt: 0.02);
                trajectory = model.Points;

                if (trajectory.Count == 0)
                    throw new Exception("Моделирование не дало результатов");

                // Поиск границ
                maxX = trajectory.Max(p => p.X);
                minX = trajectory.Min(p => p.X);
                maxY = trajectory.Max(p => p.Y);
                minY = trajectory.Min(p => p.Y);

                // Добавляем запас по краям 5%
                double paddingX = maxX * 0.05;
                double paddingY = maxY * 0.05;
                minX = Math.Max(0, minX - paddingX);
                maxX = maxX + paddingX;
                minY = Math.Max(0, minY - paddingY);
                maxY = maxY + paddingY;

                if (maxX < 10) maxX = 10;
                if (maxY < 10) maxY = 10;

                // Сбрасываем масштаб
                scale = 1.0;
                offsetX = 0;
                offsetY = 0;

                DrawTrajectory();

                txtStatus.Text = $"Расчёт завершён. Дальность: {trajectory[trajectory.Count - 1].X:F0} м, " +
                                 $"Время полёта: {trajectory[trajectory.Count - 1].Time:F1} с, " +
                                 $"Макс. высота: {maxY - paddingY:F0} м\n" +
                                 $"Масштаб: 1 см на экране = {(maxY / plotH):F1} м (нажмите кнопки +/- для изометрического масштаба)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
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

            plotW = w - marginLeft - marginRight;
            plotH = h - marginTop - marginBottom;

            // Вычисляем масштаб для изометрического отображения
            double rangeX = maxX - minX;
            double rangeY = maxY - minY;
            if (rangeX <= 0) rangeX = 1;
            if (rangeY <= 0) rangeY = 1;

            // Масштаб по X и Y в пикселях на метр
            double scaleXPixelsPerMeter = plotW / rangeX;
            double scaleYPixelsPerMeter = plotH / rangeY;

            // Используем минимальный масштаб, чтобы оба измерения поместились
            // Умножаем на scale (который может менять пользователь кнопками)
            double pixelsPerMeter = Math.Min(scaleXPixelsPerMeter, scaleYPixelsPerMeter) * scale;

            // Вычисляем видимые диапазоны с учётом единого масштаба
            double visibleRangeX = plotW / pixelsPerMeter;
            double visibleRangeY = plotH / pixelsPerMeter;

            // Центрируем траекторию
            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            double viewMinX = centerX - visibleRangeX / 2 + offsetX * rangeX;
            double viewMaxX = viewMinX + visibleRangeX;
            double viewMinY = centerY - visibleRangeY / 2 + offsetY * rangeY;
            double viewMaxY = viewMinY + visibleRangeY;

            // Функция преобразования координат
            Func<double, double, Point> map = (xWorld, yWorld) =>
            {
                double px = marginLeft + (xWorld - viewMinX) * pixelsPerMeter;
                double py = marginTop + plotH - (yWorld - viewMinY) * pixelsPerMeter;
                return new Point(px, py);
            };

            // Рисуем сетку
            DrawGridAndAxes(marginLeft, marginTop, plotW, plotH, viewMinX, viewMaxX, viewMinY, viewMaxY, pixelsPerMeter);

            // Рисуем траекторию
            DrawColoredTrajectory(map);

            // Рисуем ключевые точки
            DrawKeyPoints(map);

            // Рисуем легенду
            DrawLegend(marginLeft, marginTop);

            // Добавляем кнопки масштабирования
            AddZoomControls();
        }

        private void DrawGridAndAxes(double left, double top, double plotW, double plotH,
                                      double viewMinX, double viewMaxX, double viewMinY, double viewMaxY,
                                      double pixelsPerMeter)
        {
            // Оси
            Line xAxis = new Line { X1 = left, Y1 = top + plotH, X2 = left + plotW, Y2 = top + plotH, Stroke = Brushes.Black, StrokeThickness = 2 };
            Line yAxis = new Line { X1 = left, Y1 = top, X2 = left, Y2 = top + plotH, Stroke = Brushes.Black, StrokeThickness = 2 };
            canvasTrajectory.Children.Add(xAxis);
            canvasTrajectory.Children.Add(yAxis);

            // Подписи осей
            TextBlock xLabel = new TextBlock { Text = "Дальность (м)", FontSize = 11, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(xLabel, left + plotW / 2 - 40);
            Canvas.SetTop(xLabel, top + plotH + 25);
            canvasTrajectory.Children.Add(xLabel);

            TextBlock yLabel = new TextBlock { Text = "Высота (м)", FontSize = 11, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(yLabel, left - 50);
            Canvas.SetTop(yLabel, top + plotH / 2 - 10);
            canvasTrajectory.Children.Add(yLabel);

            // Сетка по X с шагом 100, 200, 500 м
            double stepX = GetNiceStep(Math.Abs(viewMaxX - viewMinX));
            double startX = Math.Floor(viewMinX / stepX) * stepX;
            for (double xVal = startX; xVal <= viewMaxX; xVal += stepX)
            {
                if (xVal < viewMinX - stepX / 2) continue;
                if (xVal > viewMaxX + stepX / 2) break;

                double xPix = left + (xVal - viewMinX) * pixelsPerMeter;
                if (xPix < left || xPix > left + plotW) continue;

                Line gridLine = new Line
                {
                    X1 = xPix,
                    Y1 = top,
                    X2 = xPix,
                    Y2 = top + plotH,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5
                };
                gridLine.StrokeDashArray = new DoubleCollection { 3, 3 };
                canvasTrajectory.Children.Add(gridLine);

                // Подпись только если есть место
                if (xPix > left + 20 && xPix < left + plotW - 20)
                {
                    TextBlock lbl = new TextBlock { Text = $"{xVal:F0}", FontSize = 8, Foreground = Brushes.DarkSlateGray };
                    Canvas.SetLeft(lbl, xPix - 12);
                    Canvas.SetTop(lbl, top + plotH + 3);
                    canvasTrajectory.Children.Add(lbl);
                }
            }

            // Сетка по Y с тем же шагом (изометрически)
            double stepY = stepX; // Одинаковый шаг для X и Y
            double startY = Math.Floor(viewMinY / stepY) * stepY;
            for (double yVal = startY; yVal <= viewMaxY; yVal += stepY)
            {
                if (yVal < viewMinY - stepY / 2) continue;
                if (yVal > viewMaxY + stepY / 2) break;

                double yPix = top + plotH - (yVal - viewMinY) * pixelsPerMeter;
                if (yPix < top || yPix > top + plotH) continue;

                Line gridLine = new Line
                {
                    X1 = left,
                    Y1 = yPix,
                    X2 = left + plotW,
                    Y2 = yPix,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5
                };
                gridLine.StrokeDashArray = new DoubleCollection { 3, 3 };
                canvasTrajectory.Children.Add(gridLine);

                // Подпись только если есть место
                if (yPix > top + 15 && yPix < top + plotH - 10)
                {
                    TextBlock lbl = new TextBlock { Text = $"{yVal:F0}", FontSize = 8, Foreground = Brushes.DarkSlateGray };
                    Canvas.SetLeft(lbl, left - 35);
                    Canvas.SetTop(lbl, yPix - 6);
                    canvasTrajectory.Children.Add(lbl);
                }
            }

            // Добавляем информацию о масштабе в угол
            TextBlock scaleInfo = new TextBlock
            {
                Text = $"Масштаб: 1 см ↔ {(100 / pixelsPerMeter):F1} м",
                FontSize = 8,
                Foreground = Brushes.Gray,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Padding = new Thickness(3)
            };
            Canvas.SetRight(scaleInfo, 10);
            Canvas.SetBottom(scaleInfo, 5);
            canvasTrajectory.Children.Add(scaleInfo);
        }

        private double GetNiceStep(double range)
        {
            // Выбираем красивый шаг сетки: 10, 20, 50, 100, 200, 500, 1000...
            double rawStep = range / 8;
            double exponent = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double fraction = rawStep / exponent;

            if (fraction < 1.5) return 1 * exponent;
            if (fraction < 3.5) return 2 * exponent;
            if (fraction < 7.5) return 5 * exponent;
            return 10 * exponent;
        }

        private void DrawColoredTrajectory(Func<double, double, Point> map)
        {
            if (trajectory.Count < 2) return;

            // Разбиваем на сегменты
            List<TrajectoryPoint> rocketPhase = new List<TrajectoryPoint>();
            List<TrajectoryPoint> freePhase = new List<TrajectoryPoint>();
            List<TrajectoryPoint> chutePhase = new List<TrajectoryPoint>();

            bool rocketFinished = false;
            bool chuteDeployed = false;

            foreach (var p in trajectory)
            {
                if (!rocketFinished && p.Time <= burnTimeValue)
                {
                    rocketPhase.Add(p);
                }
                else if (!rocketFinished)
                {
                    rocketFinished = true;
                }

                if (rocketFinished && !chuteDeployed)
                {
                    if (!p.ParachuteDeployed)
                        freePhase.Add(p);
                    else
                    {
                        chuteDeployed = true;
                        if (freePhase.Count > 0 && freePhase.Last() != p)
                            freePhase.Add(p);
                        chutePhase.Add(p);
                    }
                }
                else if (chuteDeployed)
                {
                    chutePhase.Add(p);
                }
            }

            // Красный - работа двигателя
            if (rocketPhase.Count > 1)
            {
                Polyline line = new Polyline { Stroke = Brushes.Red, StrokeThickness = 3 };
                foreach (var p in rocketPhase)
                    line.Points.Add(map(p.X, p.Y));
                canvasTrajectory.Children.Add(line);
            }

            // Синий - свободный полёт
            if (freePhase.Count > 1)
            {
                Polyline line = new Polyline { Stroke = Brushes.DodgerBlue, StrokeThickness = 2.5 };
                foreach (var p in freePhase)
                    line.Points.Add(map(p.X, p.Y));
                canvasTrajectory.Children.Add(line);
            }

            // Зелёный - парашют
            if (chutePhase.Count > 1)
            {
                Polyline line = new Polyline { Stroke = Brushes.ForestGreen, StrokeThickness = 2.5 };
                foreach (var p in chutePhase)
                    line.Points.Add(map(p.X, p.Y));
                canvasTrajectory.Children.Add(line);
            }
        }

        private void DrawKeyPoints(Func<double, double, Point> map)
        {
            if (trajectory.Count == 0) return;

            // Старт
            Point startPt = map(0, trajectory[0].Y);
            Ellipse startMarker = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Red, Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(startMarker, startPt.X - 5);
            Canvas.SetTop(startMarker, startPt.Y - 5);
            canvasTrajectory.Children.Add(startMarker);

            TextBlock startLabel = new TextBlock { Text = "Катапультирование", FontSize = 8, Foreground = Brushes.Red, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(startLabel, startPt.X + 8);
            Canvas.SetTop(startLabel, startPt.Y - 8);
            canvasTrajectory.Children.Add(startLabel);

            // Раскрытие парашюта
            var chutePoint = trajectory.FirstOrDefault(p => p.ParachuteDeployed);
            if (chutePoint != null)
            {
                Point chutePt = map(chutePoint.X, chutePoint.Y);
                Polygon rhombus = new Polygon();
                rhombus.Points.Add(new Point(chutePt.X, chutePt.Y - 6));
                rhombus.Points.Add(new Point(chutePt.X + 6, chutePt.Y));
                rhombus.Points.Add(new Point(chutePt.X, chutePt.Y + 6));
                rhombus.Points.Add(new Point(chutePt.X - 6, chutePt.Y));
                rhombus.Fill = Brushes.Gold;
                rhombus.Stroke = Brushes.Black;
                rhombus.StrokeThickness = 1;
                canvasTrajectory.Children.Add(rhombus);

                TextBlock chuteLabel = new TextBlock { Text = $"Раскрытие\n{chutePoint.Time:F1} с", FontSize = 7, Foreground = Brushes.DarkGoldenrod };
                Canvas.SetLeft(chuteLabel, chutePt.X + 8);
                Canvas.SetTop(chuteLabel, chutePt.Y - 10);
                canvasTrajectory.Children.Add(chuteLabel);
            }

            // Приземление
            var lastPoint = trajectory.Last();
            Point endPt = map(lastPoint.X, 0);
            Rectangle endMarker = new Rectangle { Width = 8, Height = 8, Fill = Brushes.Black };
            Canvas.SetLeft(endMarker, endPt.X - 4);
            Canvas.SetTop(endMarker, endPt.Y - 4);
            canvasTrajectory.Children.Add(endMarker);

            TextBlock endLabel = new TextBlock { Text = $"Приземление\n{lastPoint.X:F0} м", FontSize = 8, Foreground = Brushes.Black, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(endLabel, endPt.X + 8);
            Canvas.SetTop(endLabel, endPt.Y - 10);
            canvasTrajectory.Children.Add(endLabel);
        }

        private void DrawLegend(double left, double top)
        {
            Border legendBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8),
                Margin = new Thickness(10)
            };

            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "Легенда", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });

            StackPanel redItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            redItem.Children.Add(new Rectangle { Width = 20, Height = 3, Fill = Brushes.Red, Margin = new Thickness(0, 0, 5, 0) });
            redItem.Children.Add(new TextBlock { Text = "Работа двигателя" });
            panel.Children.Add(redItem);

            StackPanel blueItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            blueItem.Children.Add(new Rectangle { Width = 20, Height = 3, Fill = Brushes.DodgerBlue, Margin = new Thickness(0, 0, 5, 0) });
            blueItem.Children.Add(new TextBlock { Text = "Свободный полёт" });
            panel.Children.Add(blueItem);

            StackPanel greenItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            greenItem.Children.Add(new Rectangle { Width = 20, Height = 3, Fill = Brushes.ForestGreen, Margin = new Thickness(0, 0, 5, 0) });
            greenItem.Children.Add(new TextBlock { Text = "Снижение на парашюте" });
            panel.Children.Add(greenItem);

            legendBorder.Child = panel;
            Canvas.SetLeft(legendBorder, left);
            Canvas.SetTop(legendBorder, top);
            canvasTrajectory.Children.Add(legendBorder);
        }

        private void AddZoomControls()
        {
            Border panelBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5)
            };

            StackPanel buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            Button zoomInBtn = new Button { Content = "➕", Width = 30, Height = 30, FontSize = 16, Margin = new Thickness(2), ToolTip = "Приблизить (сохраняя пропорции)" };
            Button zoomOutBtn = new Button { Content = "➖", Width = 30, Height = 30, FontSize = 16, Margin = new Thickness(2), ToolTip = "Отдалить" };
            Button resetBtn = new Button { Content = "⟳", Width = 30, Height = 30, FontSize = 16, Margin = new Thickness(2), ToolTip = "Сбросить масштаб и центрировать" };

            zoomInBtn.Click += (s, e) => { scale *= 1.25; DrawTrajectory(); };
            zoomOutBtn.Click += (s, e) => { scale /= 1.25; DrawTrajectory(); };
            resetBtn.Click += (s, e) => { scale = 1.0; offsetX = 0; offsetY = 0; DrawTrajectory(); };

            buttonPanel.Children.Add(zoomInBtn);
            buttonPanel.Children.Add(zoomOutBtn);
            buttonPanel.Children.Add(resetBtn);

            panelBorder.Child = buttonPanel;

            Canvas.SetRight(panelBorder, 10);
            Canvas.SetTop(panelBorder, 10);
            canvasTrajectory.Children.Add(panelBorder);
        }
    }
}