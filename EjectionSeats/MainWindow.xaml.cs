using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace EjectionSeats
{
    public partial class MainWindow : Window
    {
        // Полный вектор состояния
        public class StateVector
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Vx { get; set; }
            public double Vy { get; set; }
            public double Vz { get; set; }
            public double Theta { get; set; }
            public double Psi { get; set; }
            public double Omega { get; set; }
            public double Time { get; set; }
            public double Pressure { get; set; }
        }

        private List<StateVector> trajectory;
        private double g = 9.81;

        // Переменные для масштабирования
        private double zoomLevel = 1.0;
        private double panX = 0;
        private double panY = 0;
        private bool isPanning = false;
        private Point lastMousePos;

        public MainWindow()
        {
            InitializeComponent();

            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");

            // Подписываемся на события мыши для Canvas
            Canvas3D.MouseWheel += Canvas3D_MouseWheel;
            Canvas3D.MouseLeftButtonDown += Canvas3D_MouseLeftButtonDown;
            Canvas3D.MouseLeftButtonUp += Canvas3D_MouseLeftButtonUp;
            Canvas3D.MouseMove += Canvas3D_MouseMove;
            Canvas3D.MouseRightButtonDown += Canvas3D_MouseRightButtonDown;
        }

        private double ParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Replace(',', '.');
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryParseInputs(out double Vc, out double H, out double pitchDeg, out double M, out double chiDeg,
                                    out double Jz, out double S_cm, out double L_cm, out double W0, out double T1,
                                    out double Mz, out double R_gas, out double T_rd, out double t_rd, out double e_rd,
                                    out double Cx, out double S_mid, out double mz_coef, out double Lk))
                    return;

                CalculateTrajectory(Vc, H, pitchDeg, M, chiDeg, Jz, S_cm, L_cm, W0, T1,
                                   Mz, R_gas, T_rd, t_rd, e_rd, Cx, S_mid, mz_coef, Lk);

                // Сбрасываем зум и панорамирование
                zoomLevel = 1.0;
                panX = 0;
                panY = 0;

                DrawTrajectory(Vc);
                DrawOxyPlots();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при расчете: {ex.Message}", "Ошибка",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CalculateTrajectory(double Vc, double H, double pitchDeg, double M, double chiDeg,
                                         double Jz, double S_cm, double L_cm, double W0, double T1,
                                         double Mz, double R_gas, double T_rd, double t_rd, double e_rd,
                                         double Cx, double S_mid, double mz_coef, double Lk)
        {
            double chi = chiDeg * Math.PI / 180.0;
            double pitch = pitchDeg * Math.PI / 180.0;

            StateVector current = new StateVector()
            {
                X = 0,
                Y = H,
                Z = 0,
                Vx = Vc,
                Vy = 0,
                Vz = 0,
                Theta = 0,
                Psi = chi + pitch,
                Omega = 0,
                Time = 0,
                Pressure = 0
            };

            trajectory = new List<StateVector> { current };

            double dt = 0.01;
            double totalTime = 5.0;
            double rho = GetAirDensity(H);
            double maxOverload = 0;

            for (double t = 0; t < totalTime; t += dt)
            {
                rho = GetAirDensity(current.Y);
                double V_abs = Math.Sqrt(current.Vx * current.Vx + current.Vy * current.Vy);

                // Сила СМ (Стреляющий Механизм) - формулы 7.2, 7.15
                double F_cm = 0;
                double L_cm_used = Math.Abs(current.X);

                if (L_cm_used < L_cm)
                {
                    double numerator = R_gas * Mz * T1 - 0.1 * M * V_abs * V_abs;
                    double denominator = W0 + S_cm * L_cm_used;
                    current.Pressure = (denominator > 0 && numerator > 0) ? numerator / denominator : 0;
                    F_cm = current.Pressure * S_cm;
                }
                else
                {
                    current.Pressure = 0;
                    F_cm = 0;
                }

                double F_cm_x = F_cm * Math.Cos(current.Psi);
                double F_cm_y = F_cm * Math.Sin(current.Psi);

                // Сила РД (Ракетный Двигатель) - формулы 7.32, 7.33
                double F_rd = (t <= t_rd) ? T_rd : 0;
                double T_x = F_rd * Math.Cos(current.Psi);
                double T_y = F_rd * Math.Sin(current.Psi);
                double M_rd = F_rd * e_rd;

                // Аэродинамика - формулы 7.30, 7.31
                double Alpha = current.Psi - current.Theta;
                double F_a = 0.5 * Cx * S_mid * rho * V_abs * V_abs;
                double F_ax = -F_a * Math.Cos(current.Theta);
                double F_ay = -F_a * Math.Sin(current.Theta);
                double M_a = mz_coef * Lk * S_mid * 0.5 * rho * V_abs * V_abs * Math.Sign(Alpha);

                // Сумма сил
                double F_sum_x = F_ax + T_x + F_cm_x;
                double F_sum_y = F_ay + T_y + F_cm_y - M * g;

                double a_x = F_sum_x / M;
                double a_y = F_sum_y / M;

                double M_sum = M_a + M_rd;
                double Eps_z = M_sum / Jz;

                // Перегрузка - формула 7.5
                double a_total = Math.Sqrt(a_x * a_x + (a_y + g) * (a_y + g));
                double n_current = a_total / g;
                if (n_current > maxOverload) maxOverload = n_current;

                // Интегрирование Эйлера - формулы 7.55, 7.56, 7.57
                StateVector next = new StateVector
                {
                    Time = current.Time + dt,
                    Vx = current.Vx + a_x * dt,
                    Vy = current.Vy + a_y * dt,
                    Vz = 0
                };

                double Vx_avg = (current.Vx + next.Vx) / 2.0;
                double Vy_avg = (current.Vy + next.Vy) / 2.0;

                next.X = current.X + Vx_avg * dt;
                next.Y = current.Y + Vy_avg * dt;
                next.Z = 0;

                next.Omega = current.Omega + Eps_z * dt;
                double Omega_avg = (current.Omega + next.Omega) / 2.0;
                next.Psi = current.Psi + Omega_avg * dt;
                next.Theta = Math.Atan2(next.Vy, next.Vx);
                next.Pressure = current.Pressure;

                if (next.Y < 0) break;

                trajectory.Add(next);
                current = next;
            }

            double maxHeight = trajectory.Max(s => s.Y) - H;
            double absoluteMaxHeight = trajectory.Max(s => s.Y);

            txtStatus.Text = $"Время полета: {current.Time:F2} с";
            txtMaxOverload.Text = $"Макс. перегрузка: {maxOverload:F2} ед.";
            txtMaxHeight.Text = $"Подъем: {maxHeight:F2} м (абс. {absoluteMaxHeight:F0} м)";
        }

        private void DrawTrajectory(double Vc)
        {
            Canvas3D.Children.Clear();
            if (trajectory == null || trajectory.Count < 2) return;

            double width = Canvas3D.ActualWidth;
            double height = Canvas3D.ActualHeight;
            if (width < 10) width = 600;
            if (height < 10) height = 400;

            double H0 = trajectory[0].Y;

            // Пересчет в самолетную СК: X1 = Vc*t - X, Y1 = Y - H0 (формула 7.37)
            var relativePoints = trajectory.Select(p => new Point(
                Vc * p.Time - p.X,
                p.Y - H0
            )).ToList();

            // Границы данных
            double dataMinX = relativePoints.Min(p => p.X);
            double dataMaxX = relativePoints.Max(p => p.X);
            double dataMinY = Math.Min(-5, relativePoints.Min(p => p.Y));
            double dataMaxY = relativePoints.Max(p => p.Y) + 5;

            // Добавляем отступы
            double margin = 50;
            double dataRangeX = dataMaxX - dataMinX;
            double dataRangeY = dataMaxY - dataMinY;

            if (dataRangeX < 1) dataRangeX = 10;
            if (dataRangeY < 1) dataRangeY = 10;

            // Функция преобразования координат с учетом зума и панорамирования
            Point TransformPoint(Point pt)
            {
                // Нормализация
                double normX = (pt.X - dataMinX) / dataRangeX;
                double normY = (pt.Y - dataMinY) / dataRangeY;

                // Применение зума и панорамирования
                normX = normX * zoomLevel + panX;
                normY = normY * zoomLevel + panY;

                // Преобразование в координаты Canvas
                double canvasX = margin + normX * (width - 2 * margin);
                double canvasY = height - margin - normY * (height - 2 * margin);

                return new Point(canvasX, canvasY);
            }

            // Белый фон
            Rectangle background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = Brushes.White
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            Canvas3D.Children.Add(background);

            // Рисуем сетку
            DrawGrid(dataMinX, dataMaxX, dataMinY, dataMaxY, width, height, margin, TransformPoint);

            // Рисуем самолет в точке (0,0) относительных координат
            Point planePos = TransformPoint(new Point(0, 0));
            DrawAirplane(planePos, H0);

            // Траектория
            Polyline trajectoryLine = new Polyline
            {
                Stroke = Brushes.Red,
                StrokeThickness = 3
            };

            foreach (var pt in relativePoints)
            {
                trajectoryLine.Points.Add(TransformPoint(pt));
            }
            Canvas3D.Children.Add(trajectoryLine);

            // Старт и конец
            DrawMarker(TransformPoint(relativePoints[0]), Colors.Green, 8, "Старт");
            DrawMarker(TransformPoint(relativePoints.Last()), Colors.Blue, 8, "Конец");

            // Маркеры времени
            for (int i = 0; i < trajectory.Count; i += (int)(0.5 / 0.01))
            {
                if (i < trajectory.Count)
                {
                    Point pt = TransformPoint(relativePoints[i]);
                    Ellipse marker = new Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Fill = Brushes.Orange,
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(marker, pt.X - 2.5);
                    Canvas.SetTop(marker, pt.Y - 2.5);
                    Canvas3D.Children.Add(marker);
                }
            }

            // Подписи осей с единицами измерения
            TextBlock titleX = new TextBlock
            {
                Text = "← Продольное перемещение X (м) →",
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.Bold
            };
            Canvas.SetLeft(titleX, width / 2 - 120);
            Canvas.SetTop(titleX, height - 20);
            Canvas3D.Children.Add(titleX);

            TextBlock titleY = new TextBlock
            {
                Text = "↑ Относительная высота Y (м) ↓",
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.Bold,
                RenderTransform = new RotateTransform(-90)
            };
            Canvas.SetLeft(titleY, 5);
            Canvas.SetTop(titleY, height / 2 - 80);
            Canvas3D.Children.Add(titleY);

            // Информация о масштабе и смещении
            TextBlock scaleInfo = new TextBlock
            {
                Text = $"Масштаб: {zoomLevel:F1}x | Смещение: ({panX:F2}, {panY:F2})",
                FontSize = 9,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(scaleInfo, 10);
            Canvas.SetTop(scaleInfo, 10);
            Canvas3D.Children.Add(scaleInfo);

            // Информация о начальной высоте
            TextBlock infoH = new TextBlock
            {
                Text = $"Начальная высота: {H0:F0} м",
                FontSize = 11,
                Foreground = Brushes.DarkBlue
            };
            Canvas.SetLeft(infoH, width - 180);
            Canvas.SetTop(infoH, 10);
            Canvas3D.Children.Add(infoH);

            // Легенда
            DrawLegend(width);
        }

        private void DrawGrid(double minX, double maxX, double minY, double maxY,
                              double width, double height, double margin,
                              Func<Point, Point> transform)
        {
            double xStep = 20;
            double yStep = 10;

            // Вертикальные линии сетки и подписи по X
            for (double x = Math.Ceiling(minX / xStep) * xStep; x <= maxX; x += xStep)
            {
                Point p1 = transform(new Point(x, minY));
                Point p2 = transform(new Point(x, maxY));

                // Линия сетки
                Line line = new Line
                {
                    X1 = p1.X,
                    Y1 = p1.Y,
                    X2 = p2.X,
                    Y2 = p2.Y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5
                };
                Canvas3D.Children.Add(line);

                // Подпись значения X
                TextBlock tb = new TextBlock
                {
                    Text = $"{x:F0}",
                    FontSize = 9,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(tb, p1.X - 15);
                Canvas.SetTop(tb, height - margin + 5);
                Canvas3D.Children.Add(tb);
            }

            // Горизонтальные линии сетки и подписи по Y
            for (double y = Math.Ceiling(minY / yStep) * yStep; y <= maxY; y += yStep)
            {
                Point p1 = transform(new Point(minX, y));
                Point p2 = transform(new Point(maxX, y));

                // Линия сетки
                Line line = new Line
                {
                    X1 = p1.X,
                    Y1 = p1.Y,
                    X2 = p2.X,
                    Y2 = p2.Y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5
                };
                Canvas3D.Children.Add(line);

                // Подпись значения Y
                TextBlock tb = new TextBlock
                {
                    Text = $"{y:F0}",
                    FontSize = 9,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(tb, 5);
                Canvas.SetTop(tb, p1.Y - 8);
                Canvas3D.Children.Add(tb);
            }

            // Оси координат
            Point origin = transform(new Point(0, 0));

            // Ось X
            Line xAxis = new Line
            {
                X1 = margin,
                Y1 = origin.Y,
                X2 = width - margin,
                Y2 = origin.Y,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            Canvas3D.Children.Add(xAxis);

            // Стрелка на оси X
            Polygon xArrow = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(width - margin, origin.Y),
                    new Point(width - margin - 8, origin.Y - 4),
                    new Point(width - margin - 8, origin.Y + 4)
                },
                Fill = Brushes.Black
            };
            Canvas3D.Children.Add(xArrow);

            // Ось Y
            Line yAxis = new Line
            {
                X1 = origin.X,
                Y1 = margin,
                X2 = origin.X,
                Y2 = height - margin,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };
            Canvas3D.Children.Add(yAxis);

            // Стрелка на оси Y
            Polygon yArrow = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(origin.X, margin),
                    new Point(origin.X - 4, margin + 8),
                    new Point(origin.X + 4, margin + 8)
                },
                Fill = Brushes.Black
            };
            Canvas3D.Children.Add(yArrow);

            // Подпись "0" в начале координат
            TextBlock zeroLabel = new TextBlock
            {
                Text = "0",
                FontSize = 9,
                Foreground = Brushes.Black,
                FontWeight = System.Windows.FontWeights.Bold
            };
            Canvas.SetLeft(zeroLabel, origin.X - 15);
            Canvas.SetTop(zeroLabel, origin.Y + 5);
            Canvas3D.Children.Add(zeroLabel);

            // Подпись уровня самолета
            TextBlock levelLabel = new TextBlock
            {
                Text = "Уровень самолета",
                FontSize = 9,
                Foreground = Brushes.DarkGreen,
                FontStyle = System.Windows.FontStyles.Italic
            };
            Canvas.SetLeft(levelLabel, origin.X + 10);
            Canvas.SetTop(levelLabel, origin.Y - 20);
            Canvas3D.Children.Add(levelLabel);
        }

        private void DrawAirplane(Point origin, double H0)
        {
            // Фюзеляж
            Rectangle fuselage = new Rectangle
            {
                Width = 40,
                Height = 8,
                Fill = Brushes.DarkBlue,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas.SetLeft(fuselage, origin.X - 20);
            Canvas.SetTop(fuselage, origin.Y - 4);
            Canvas3D.Children.Add(fuselage);

            // Киль
            Polygon fin = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(origin.X - 15, origin.Y - 4),
                    new Point(origin.X - 25, origin.Y - 25),
                    new Point(origin.X - 10, origin.Y - 25),
                    new Point(origin.X - 5, origin.Y - 4)
                },
                Fill = Brushes.LightBlue,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas3D.Children.Add(fin);

            // Крылья
            Line leftWing = new Line
            {
                X1 = origin.X - 5,
                Y1 = origin.Y,
                X2 = origin.X - 30,
                Y2 = origin.Y + 15,
                Stroke = Brushes.DarkBlue,
                StrokeThickness = 3
            };
            Canvas3D.Children.Add(leftWing);

            Line rightWing = new Line
            {
                X1 = origin.X + 5,
                Y1 = origin.Y,
                X2 = origin.X + 30,
                Y2 = origin.Y + 15,
                Stroke = Brushes.DarkBlue,
                StrokeThickness = 3
            };
            Canvas3D.Children.Add(rightWing);

            // Опасная точка киля
            Ellipse dangerPoint = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Red,
                Stroke = Brushes.DarkRed,
                StrokeThickness = 1
            };
            Canvas.SetLeft(dangerPoint, origin.X - 18);
            Canvas.SetTop(dangerPoint, origin.Y - 29);
            Canvas3D.Children.Add(dangerPoint);

            TextBlock dangerLabel = new TextBlock
            {
                Text = "Опасная точка киля",
                FontSize = 9,
                Foreground = Brushes.Red,
                FontWeight = System.Windows.FontWeights.Bold
            };
            Canvas.SetLeft(dangerLabel, origin.X - 70);
            Canvas.SetTop(dangerLabel, origin.Y - 50);
            Canvas3D.Children.Add(dangerLabel);
        }

        private void DrawMarker(Point center, Color color, double size, string label)
        {
            Ellipse marker = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(marker, center.X - size / 2);
            Canvas.SetTop(marker, center.Y - size / 2);
            Canvas3D.Children.Add(marker);

            TextBlock tb = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                Background = Brushes.White
            };
            Canvas.SetLeft(tb, center.X + 10);
            Canvas.SetTop(tb, center.Y - 10);
            Canvas3D.Children.Add(tb);
        }

        private void DrawLegend(double width)
        {
            double x = width - 170;
            double y = 50;

            Rectangle bg = new Rectangle
            {
                Width = 150,
                Height = 80,
                Fill = Brushes.White,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                Opacity = 0.9
            };
            Canvas.SetLeft(bg, x);
            Canvas.SetTop(bg, y);
            Canvas3D.Children.Add(bg);

            TextBlock title = new TextBlock
            {
                Text = "Легенда",
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 11
            };
            Canvas.SetLeft(title, x + 5);
            Canvas.SetTop(title, y + 5);
            Canvas3D.Children.Add(title);

            // Красная линия
            Line redLine = new Line
            {
                X1 = x + 10,
                Y1 = y + 25,
                X2 = x + 35,
                Y2 = y + 25,
                Stroke = Brushes.Red,
                StrokeThickness = 3
            };
            Canvas3D.Children.Add(redLine);

            TextBlock redLabel = new TextBlock
            {
                Text = "Траектория",
                FontSize = 9
            };
            Canvas.SetLeft(redLabel, x + 40);
            Canvas.SetTop(redLabel, y + 18);
            Canvas3D.Children.Add(redLabel);

            // Самолет
            Rectangle plane = new Rectangle
            {
                Width = 12,
                Height = 4,
                Fill = Brushes.DarkBlue
            };
            Canvas.SetLeft(plane, x + 20);
            Canvas.SetTop(plane, y + 45);
            Canvas3D.Children.Add(plane);

            TextBlock planeLabel = new TextBlock
            {
                Text = "Самолет",
                FontSize = 9
            };
            Canvas.SetLeft(planeLabel, x + 40);
            Canvas.SetTop(planeLabel, y + 40);
            Canvas3D.Children.Add(planeLabel);

            // Опасная точка
            Ellipse danger = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(danger, x + 23);
            Canvas.SetTop(danger, y + 62);
            Canvas3D.Children.Add(danger);

            TextBlock dangerLabel = new TextBlock
            {
                Text = "Опасная точка",
                FontSize = 9
            };
            Canvas.SetLeft(dangerLabel, x + 40);
            Canvas.SetTop(dangerLabel, y + 58);
            Canvas3D.Children.Add(dangerLabel);
        }

        private void DrawOxyPlots()
        {
            // График скорости
            var plotV = new PlotModel { Title = "Скорость кресла" };
            var seriesV = new LineSeries { Color = OxyColor.FromRgb(0, 0, 255), StrokeThickness = 2 };
            foreach (var s in trajectory)
                seriesV.Points.Add(new DataPoint(s.Time, Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy)));
            plotV.Series.Add(seriesV);
            plotV.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Время (с)" });
            plotV.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Скорость (м/с)" });
            PlotVelocity.Model = plotV;

            // График перегрузки
            var plotN = new PlotModel { Title = "Перегрузка" };
            var seriesN = new LineSeries { Color = OxyColor.FromRgb(255, 0, 0), StrokeThickness = 2 };
            for (int i = 1; i < trajectory.Count; i++)
            {
                var p = trajectory[i - 1];
                var c = trajectory[i];
                double dt = c.Time - p.Time;
                double ax = (c.Vx - p.Vx) / dt;
                double ay = (c.Vy - p.Vy) / dt;
                double a = Math.Sqrt(ax * ax + (ay + g) * (ay + g));
                seriesN.Points.Add(new DataPoint(c.Time, a / g));
            }
            plotN.Series.Add(seriesN);

            // Линия предельной перегрузки (20 ед.)
            var limit = new LineSeries
            {
                Color = OxyColor.FromRgb(255, 165, 0),
                StrokeThickness = 1,
                LineStyle = LineStyle.Dash,
                Title = "Предел (20 ед.)"
            };
            limit.Points.Add(new DataPoint(0, 20));
            limit.Points.Add(new DataPoint(trajectory.Last().Time, 20));
            plotN.Series.Add(limit);

            plotN.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Время (с)" });
            plotN.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Перегрузка (ед.)" });
            PlotOverload.Model = plotN;
        }

        // Обработчики мыши для масштабирования и панорамирования
        private void Canvas3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (trajectory == null) return;

            double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
            zoomLevel = Math.Max(0.5, Math.Min(3.0, zoomLevel + zoomDelta));
            DrawTrajectory(double.Parse(txtVc.Text.Replace(',', '.'), CultureInfo.InvariantCulture));
        }

        private void Canvas3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isPanning = true;
            lastMousePos = e.GetPosition(Canvas3D);
            Canvas3D.CaptureMouse();
        }

        private void Canvas3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isPanning = false;
            Canvas3D.ReleaseMouseCapture();
        }

        private void Canvas3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning && trajectory != null)
            {
                Point currentPos = e.GetPosition(Canvas3D);
                double deltaX = (currentPos.X - lastMousePos.X) / Canvas3D.ActualWidth;
                double deltaY = (currentPos.Y - lastMousePos.Y) / Canvas3D.ActualHeight;

                panX += deltaX;
                panY -= deltaY;

                lastMousePos = currentPos;
                DrawTrajectory(double.Parse(txtVc.Text.Replace(',', '.'), CultureInfo.InvariantCulture));
            }
        }

        private void Canvas3D_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            zoomLevel = 1.0;
            panX = 0;
            panY = 0;
            if (trajectory != null)
                DrawTrajectory(double.Parse(txtVc.Text.Replace(',', '.'), CultureInfo.InvariantCulture));
        }

        private double GetAirDensity(double h)
        {
            // Стандартная атмосфера ГОСТ 4401-81
            if (h <= 11000)
            {
                double T = 288.15 - 0.0065 * h;
                double P = 101325 * Math.Pow(T / 288.15, 5.2561);
                return P / (287.05 * T);
            }
            else
            {
                double T = 216.65;
                double P = 22632 * Math.Exp(-(h - 11000) / 6341.62);
                return P / (287.05 * T);
            }
        }

        private bool TryParseInputs(out double Vc, out double H, out double pitchDeg, out double M, out double chiDeg,
                                    out double Jz, out double S_cm, out double L_cm, out double W0, out double T1,
                                    out double Mz, out double R_gas, out double T_rd, out double t_rd, out double e_rd,
                                    out double Cx, out double S_mid, out double mz_coef, out double Lk)
        {
            Vc = H = pitchDeg = M = chiDeg = Jz = S_cm = L_cm = W0 = T1 = Mz = R_gas = T_rd = t_rd = e_rd = Cx = S_mid = mz_coef = Lk = 0;
            try
            {
                Vc = ParseDouble(txtVc.Text);
                H = ParseDouble(txtH.Text);
                pitchDeg = ParseDouble(txtPitch.Text);
                M = ParseDouble(txtMass.Text);
                chiDeg = ParseDouble(txtChi.Text);
                Jz = ParseDouble(txtJz.Text);
                S_cm = ParseDouble(txtS_cm.Text);
                L_cm = ParseDouble(txtL_cm.Text);
                W0 = ParseDouble(txtW0.Text);
                T1 = ParseDouble(txtT1.Text);
                Mz = ParseDouble(txtMz.Text);
                R_gas = ParseDouble(txtR.Text);
                T_rd = ParseDouble(txtThrust.Text);
                t_rd = ParseDouble(txtTimeRD.Text);
                e_rd = ParseDouble(txtEcc.Text);
                Cx = ParseDouble(txtCx.Text);
                S_mid = ParseDouble(txtS.Text);
                mz_coef = ParseDouble(txtMzCoef.Text);
                Lk = ParseDouble(txtLk.Text);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка ввода числовых параметров:\n{ex.Message}\n\n" +
                               "Проверьте, что все числа введены корректно (используйте запятую как разделитель).",
                               "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}