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
        public class StateVector
        {
            public double X, Y, Z;
            public double Vx, Vy, Vz;
            public double Psi, Theta, Gamma;
            public double OmegaX, OmegaY, OmegaZ;
            public double Time, Pressure;
            public bool IsDeployed;
        }

        private List<StateVector> trajectory;
        private List<Point3D> dangerTrajectory;
        private double g = 9.81;
        private double zoomLevel = 1.0, panX = 0, panY = 0;
        private bool isPanning;
        private Point lastMousePos;

        public MainWindow()
        {
            InitializeComponent();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            Canvas3D.MouseWheel += Canvas3D_MouseWheel;
            Canvas3D.MouseLeftButtonDown += Canvas3D_MouseLeftButtonDown;
            Canvas3D.MouseLeftButtonUp += Canvas3D_MouseLeftButtonUp;
            Canvas3D.MouseMove += Canvas3D_MouseMove;
            Canvas3D.MouseRightButtonDown += Canvas3D_MouseRightButtonDown;
        }

        private double ParseDouble(string s) =>
            string.IsNullOrWhiteSpace(s) ? 0 : double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryParseInputs(out var p)) return;
                CalculateTrajectory(p);
                zoomLevel = 1.0; panX = panY = 0;
                DrawTrajectory(p.Vc);
                DrawOxyPlots();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private struct InputParams
        {
            public double Vc, H, pitchDeg, rollDeg, yawDeg;
            public double M, chiDeg, Jx, Jy, Jz;
            public double S_cm, L_cm, W0, T1, Mz, R_gas;
            public double T_rd, t_rd, e_rd;
            public double Cx, Cy0, Cz0, S_mid, mz_coef, Lk;
            public double Scc, Cxcc, Rcc;
            public double Xotk, Yotk, Zotk;
            public double t_deploy, S_main, Cx_main, M_pilot, t_deploy_ramp;
            public double simTime;
        }

        private bool TryParseInputs(out InputParams p)
        {
            p = default;
            try
            {
                p.Vc = ParseDouble(txtVc.Text);
                p.H = ParseDouble(txtH.Text);
                p.pitchDeg = ParseDouble(txtPitch.Text);
                p.rollDeg = ParseDouble(txtRoll.Text);
                p.yawDeg = ParseDouble(txtYaw.Text);
                p.M = ParseDouble(txtMass.Text);
                p.chiDeg = ParseDouble(txtChi.Text);
                p.Jx = ParseDouble(txtJx.Text);
                p.Jy = ParseDouble(txtJy.Text);
                p.Jz = ParseDouble(txtJz.Text);
                p.S_cm = ParseDouble(txtS_cm.Text);
                p.L_cm = ParseDouble(txtL_cm.Text);
                p.W0 = ParseDouble(txtW0.Text);
                p.T1 = ParseDouble(txtT1.Text);
                p.Mz = ParseDouble(txtMz.Text);
                p.R_gas = ParseDouble(txtR.Text);
                p.T_rd = ParseDouble(txtThrust.Text);
                p.t_rd = ParseDouble(txtTimeRD.Text);
                p.e_rd = ParseDouble(txtEcc.Text);
                p.Cx = ParseDouble(txtCx.Text);
                p.Cy0 = ParseDouble(txtCy0.Text);
                p.Cz0 = ParseDouble(txtCz0.Text);
                p.S_mid = ParseDouble(txtS.Text);
                p.mz_coef = ParseDouble(txtMzCoef.Text);
                p.Lk = ParseDouble(txtLk.Text);
                p.Scc = ParseDouble(txtScc.Text);
                p.Cxcc = ParseDouble(txtCxcc.Text);
                p.Rcc = ParseDouble(txtRcc.Text);
                p.Xotk = ParseDouble(txtXotk.Text);
                p.Yotk = ParseDouble(txtYotk.Text);
                p.Zotk = ParseDouble(txtZotk.Text);
                p.t_deploy = ParseDouble(txtTDeploy.Text);
                p.S_main = ParseDouble(txtSmain.Text);
                p.Cx_main = ParseDouble(txtCxmain.Text);
                p.M_pilot = ParseDouble(txtMpilot.Text);
                p.t_deploy_ramp = ParseDouble(txtTDeployRamp.Text);
                p.simTime = ParseDouble(txtSimTime.Text);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка ввода: " + ex.Message);
                return false;
            }
        }

        private void CalculateTrajectory(InputParams p)
        {
            double chi = p.chiDeg * Math.PI / 180.0;
            double pitch = p.pitchDeg * Math.PI / 180.0;
            double roll = p.rollDeg * Math.PI / 180.0;
            double yaw = p.yawDeg * Math.PI / 180.0;

            // Начальная скорость кресла (раздел 2.1.6 пособия)
            double Vcm = 15.0; // скорость от СМ, можно вынести в поле
            double Vx0 = p.Vc - Vcm * Math.Sin(chi + pitch);
            double Vy0 = Vcm * Math.Cos(chi + pitch);
            double Vz0 = 0;

            StateVector current = new StateVector
            {
                X = 0,
                Y = p.H,
                Z = 0,
                Vx = Vx0,
                Vy = Vy0,
                Vz = Vz0,
                Psi = chi + pitch,
                Theta = roll,
                Gamma = yaw,
                OmegaX = 0,
                OmegaY = 0,
                OmegaZ = 0,
                Time = 0,
                Pressure = 0,
                IsDeployed = false
            };

            trajectory = new List<StateVector> { current };
            dangerTrajectory = new List<Point3D>();
            double dt = 0.01, maxOverload = 0;
            bool deployed = false;
            double landingSpeed = 0;
            double totalTime = p.simTime;   // вместо 60.0

            for (double t = 0; t < totalTime; t += dt)
            {
                double rho = GetAirDensity(current.Y);
                double V_abs = Math.Sqrt(current.Vx * current.Vx + current.Vy * current.Vy + current.Vz * current.Vz);
                double V_abs_clamp = Math.Max(V_abs, 0.01);
                double Mach = V_abs / GetSoundSpeed(current.Y);
                double K = (Mach <= 0.6) ? 1.0 : (1.0 + 0.5 * (Mach - 0.6));

                if (!deployed && t >= p.t_deploy && current.Y > 0)
                {
                    deployed = true;
                    current.IsDeployed = true;
                }

                double M_eff = current.IsDeployed ? p.M_pilot : p.M;
                double F_rd = 0;
                double dragForce = 0;
                double aeroForceX = 0, aeroForceY = 0, aeroForceZ = 0;
                double Fcc = 0;   // объявлена здесь

                if (!current.IsDeployed)
                {
                    // Реактивный ускоритель
                    F_rd = (t <= p.t_rd) ? p.T_rd : 0;

                    // Лобовое сопротивление кресла
                    double Cx_eff = p.Cx * K;
                    dragForce = 0.5 * rho * Cx_eff * p.S_mid * V_abs * V_abs;
                    double invV = 1.0 / V_abs_clamp;
                    aeroForceX = -dragForce * current.Vx * invV;
                    aeroForceY = -dragForce * current.Vy * invV;
                    aeroForceZ = -dragForce * current.Vz * invV;

                    // Стабилизирующие парашюты (два купола)
                    Fcc = 0.5 * rho * p.Scc * p.Cxcc * V_abs * V_abs;
                    aeroForceX -= 2 * Fcc * current.Vx * invV;
                    aeroForceY -= 2 * Fcc * current.Vy * invV;
                    aeroForceZ -= 2 * Fcc * current.Vz * invV;
                }
                else
                {
                    // Фаза основного парашюта
                    double timeSinceDeploy = t - p.t_deploy;
                    double fraction = Math.Min(1.0, timeSinceDeploy / p.t_deploy_ramp);
                    double effArea = fraction * p.S_main;                     // всегда ≥ 0
                    dragForce = 0.5 * rho * p.Cx_main * effArea * V_abs * V_abs;
                    double invV = 1.0 / V_abs_clamp;
                    aeroForceX = -dragForce * current.Vx * invV;
                    aeroForceY = -dragForce * current.Vy * invV;
                    aeroForceZ = -dragForce * current.Vz * invV;
                }

                // Суммарные силы
                double Fx = F_rd * Math.Cos(current.Psi) + aeroForceX;
                double Fy = F_rd * Math.Sin(current.Psi) + aeroForceY - M_eff * g;
                double Fz = aeroForceZ;

                double ax = Fx / M_eff;
                double ay = Fy / M_eff;
                double az = Fz / M_eff;

                // Моменты (упрощённо)
                double epsX = 0, epsY = 0, epsZ = 0;
                if (!current.IsDeployed)
                {
                    double alpha = current.Psi - Math.Atan2(current.Vy, current.Vx);
                    double Mx_aero = p.mz_coef * p.Lk * p.S_mid * 0.5 * rho * V_abs * V_abs * (current.Gamma - current.Theta);
                    double Mz_aero = p.mz_coef * p.Lk * p.S_mid * 0.5 * rho * V_abs * V_abs * alpha;
                    double M_stab = Fcc * p.Rcc * Math.Sign(alpha);
                    epsX = (Mx_aero + M_stab) / p.Jx;
                    epsZ = (Mz_aero + M_stab) / p.Jz;
                }

                double a_total = Math.Sqrt(ax * ax + (ay + g) * (ay + g) + az * az);
                double n = a_total / g;
                if (n > maxOverload) maxOverload = n;

                // Интегрирование
                StateVector next = new StateVector
                {
                    Time = current.Time + dt,
                    Vx = current.Vx + ax * dt,
                    Vy = current.Vy + ay * dt,
                    Vz = current.Vz + az * dt,
                    OmegaX = current.OmegaX + epsX * dt,
                    OmegaY = current.OmegaY + epsY * dt,
                    OmegaZ = current.OmegaZ + epsZ * dt,
                    IsDeployed = current.IsDeployed
                };

                double Vx_avg = (current.Vx + next.Vx) / 2.0;
                double Vy_avg = (current.Vy + next.Vy) / 2.0;
                double Vz_avg = (current.Vz + next.Vz) / 2.0;
                next.X = current.X + Vx_avg * dt;
                next.Y = current.Y + Vy_avg * dt;
                next.Z = current.Z + Vz_avg * dt;

                double OmegaX_avg = (current.OmegaX + next.OmegaX) / 2.0;
                double OmegaY_avg = (current.OmegaY + next.OmegaY) / 2.0;
                double OmegaZ_avg = (current.OmegaZ + next.OmegaZ) / 2.0;
                next.Psi = current.Psi + OmegaZ_avg * dt;
                next.Theta = current.Theta + OmegaX_avg * dt;
                next.Gamma = current.Gamma + OmegaY_avg * dt;

                if (next.Y <= 0)
                {
                    landingSpeed = Math.Sqrt(next.Vx * next.Vx + next.Vy * next.Vy + next.Vz * next.Vz);
                    next.Y = 0;
                    trajectory.Add(next);
                    current = next;
                    break;
                }

                trajectory.Add(next);
                current = next;

                if (!current.IsDeployed)
                {
                    dangerTrajectory.Add(new Point3D(p.Xotk + p.Vc * current.Time, p.Yotk, p.Zotk));
                }

                // Диагностика в реальном времени (обновляем не каждый шаг, а раз в 20 шагов)
                if (current.IsDeployed && Math.Abs(current.Time % 0.2) < dt)
                {
                    txtStatus.Text = $"Парашют: t={current.Time:F1}с, Vx={current.Vx:F1}, Vy={current.Vy:F1}";
                }
            }

            double maxHeight = trajectory.Max(s => s.Y) - p.H;
            double minDist = double.MaxValue;
            for (int i = 0; i < Math.Min(trajectory.Count, dangerTrajectory.Count); i++)
            {
                var kk = trajectory[i];
                var otk = dangerTrajectory[i];
                double dist = Math.Sqrt((kk.X - otk.X) * (kk.X - otk.X) +
                                        (kk.Y - otk.Y) * (kk.Y - otk.Y) +
                                        (kk.Z - otk.Z) * (kk.Z - otk.Z));
                if (dist < minDist) minDist = dist;
            }

            txtStatus.Text = $"Полёт: {current.Time:F2} с, приземление: {landingSpeed:F1} м/с";
            txtMaxOverload.Text = $"Макс. перегрузка: {maxOverload:F2} ед.";
            txtMaxHeight.Text = $"Подъём: {maxHeight:F2} м (абс. {p.H + maxHeight:F0} м)";
            txtMinDistance.Text = $"Мин. расстояние до киля: {minDist:F2} м";
        }

        private double GetSoundSpeed(double h)
        {
            double T = (h <= 11000) ? 288.15 - 0.0065 * h : 216.65;
            return Math.Sqrt(1.4 * 287.05 * T);
        }

        private double GetAirDensity(double h)
        {
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

        // ================== ВИЗУАЛИЗАЦИЯ ==================
        private void DrawTrajectory(double Vc)
        {
            Canvas3D.Children.Clear();
            if (trajectory == null || trajectory.Count < 2) return;

            double width = Canvas3D.ActualWidth, height = Canvas3D.ActualHeight;
            if (width < 10) width = 600; if (height < 10) height = 400;
            double H0 = trajectory[0].Y;

            var allPoints = trajectory.Select(s => new Point(Vc * s.Time - s.X, s.Y - H0)).ToList();
            double minX = allPoints.Min(p => p.X) - 5;
            double maxX = allPoints.Max(p => p.X) + 5;
            double minY = Math.Min(-5, allPoints.Min(p => p.Y)) - 5;
            double maxY = allPoints.Max(p => p.Y) + 10;
            double margin = 50;

            Point Transform(Point pt)
            {
                double normX = (pt.X - minX) / (maxX - minX) * zoomLevel + panX;
                double normY = (pt.Y - minY) / (maxY - minY) * zoomLevel + panY;
                return new Point(margin + normX * (width - 2 * margin),
                                 height - margin - normY * (height - 2 * margin));
            }

            Canvas3D.Children.Add(new Rectangle { Width = width, Height = height, Fill = Brushes.White });
            DrawGrid(minX, maxX, minY, maxY, width, height, margin, Transform, H0);
            DrawAirplane(Transform(new Point(0, 0)), H0);

            var pointsBefore = trajectory.Where(s => !s.IsDeployed).Select(s => new Point(Vc * s.Time - s.X, s.Y - H0)).ToList();
            var pointsAfter = trajectory.Where(s => s.IsDeployed).Select(s => new Point(Vc * s.Time - s.X, s.Y - H0)).ToList();

            if (pointsBefore.Count > 1)
            {
                Polyline line1 = new Polyline { Stroke = Brushes.Red, StrokeThickness = 3 };
                foreach (var pt in pointsBefore) line1.Points.Add(Transform(pt));
                Canvas3D.Children.Add(line1);
            }
            if (pointsAfter.Count > 1)
            {
                Polyline line2 = new Polyline { Stroke = Brushes.Green, StrokeThickness = 3 };
                foreach (var pt in pointsAfter) line2.Points.Add(Transform(pt));
                Canvas3D.Children.Add(line2);
            }

            DrawMarker(Transform(allPoints.First()), Colors.Green, 8, "Старт");
            DrawMarker(Transform(allPoints.Last()), Colors.Blue, 8, "Приземление");

            if (dangerTrajectory != null && dangerTrajectory.Count > 0)
            {
                var d0 = dangerTrajectory[0];
                Point dangerRel = new Point(Vc * d0.X - d0.X, d0.Y - H0);
                Ellipse de = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Red };
                Point dc = Transform(dangerRel);
                Canvas.SetLeft(de, dc.X - 4); Canvas.SetTop(de, dc.Y - 4);
                Canvas3D.Children.Add(de);
            }

            var titleX = new TextBlock { Text = "← Продольное перемещение (м) →", FontWeight = System.Windows.FontWeights.Bold, FontSize = 12 };
            Canvas.SetLeft(titleX, width / 2 - 100); Canvas.SetTop(titleX, height - 20);
            Canvas3D.Children.Add(titleX);

            var titleY = new TextBlock { Text = "↑ Абсолютная высота (м) ↓", FontWeight = System.Windows.FontWeights.Bold, FontSize = 12, RenderTransform = new RotateTransform(-90) };
            Canvas.SetLeft(titleY, 5); Canvas.SetTop(titleY, height / 2 - 70);
            Canvas3D.Children.Add(titleY);

            var scaleInfo = new TextBlock { Text = $"Масштаб: {zoomLevel:F1}x", FontSize = 9, Foreground = Brushes.Gray };
            Canvas.SetLeft(scaleInfo, 10); Canvas.SetTop(scaleInfo, 10);
            Canvas3D.Children.Add(scaleInfo);

            var heightInfo = new TextBlock { Text = $"Начальная высота: {H0:F0} м", FontSize = 11, Foreground = Brushes.DarkBlue };
            Canvas.SetLeft(heightInfo, width - 180); Canvas.SetTop(heightInfo, 10);
            Canvas3D.Children.Add(heightInfo);

            DrawLegend(width);
        }

        private void DrawGrid(double minX, double maxX, double minY, double maxY,
                              double w, double h, double margin,
                              Func<Point, Point> transform, double H0)
        {
            double step = 10;
            for (double x = Math.Ceiling(minX / step) * step; x <= maxX; x += step)
            {
                var p1 = transform(new Point(x, minY));
                var p2 = transform(new Point(x, maxY));
                Canvas3D.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.LightGray, StrokeThickness = 0.5 });
                var label = new TextBlock { Text = $"{x:F0}", FontSize = 9, Foreground = Brushes.Gray };
                Canvas.SetLeft(label, p1.X - 15); Canvas.SetTop(label, h - margin + 5);
                Canvas3D.Children.Add(label);
            }
            for (double y = Math.Ceiling(minY / step) * step; y <= maxY; y += step)
            {
                var p1 = transform(new Point(minX, y));
                var p2 = transform(new Point(maxX, y));
                Canvas3D.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.LightGray, StrokeThickness = 0.5 });
                var label = new TextBlock { Text = $"{y + H0:F0}", FontSize = 9, Foreground = Brushes.Gray };
                Canvas.SetLeft(label, 5); Canvas.SetTop(label, p1.Y - 8);
                Canvas3D.Children.Add(label);
            }

            Point origin = transform(new Point(0, 0));
            Canvas3D.Children.Add(new Line { X1 = margin, Y1 = origin.Y, X2 = w - margin, Y2 = origin.Y, Stroke = Brushes.Black, StrokeThickness = 2 });
            Canvas3D.Children.Add(new Line { X1 = origin.X, Y1 = margin, X2 = origin.X, Y2 = h - margin, Stroke = Brushes.Black, StrokeThickness = 2 });

            var originLabel = new TextBlock { Text = $"{H0:F0}", FontSize = 9, FontWeight = System.Windows.FontWeights.Bold };
            Canvas.SetLeft(originLabel, origin.X - 20); Canvas.SetTop(originLabel, origin.Y + 5);
            Canvas3D.Children.Add(originLabel);
        }

        private void DrawAirplane(Point origin, double H0)
        {
            Rectangle fuselage = new Rectangle { Width = 40, Height = 8, Fill = Brushes.DarkBlue, Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(fuselage, origin.X - 20); Canvas.SetTop(fuselage, origin.Y - 4);
            Canvas3D.Children.Add(fuselage);

            Polygon fin = new Polygon
            {
                Points = new PointCollection { new Point(origin.X - 15, origin.Y - 4), new Point(origin.X - 25, origin.Y - 25), new Point(origin.X - 10, origin.Y - 25), new Point(origin.X - 5, origin.Y - 4) },
                Fill = Brushes.LightBlue,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas3D.Children.Add(fin);

            Line leftWing = new Line { X1 = origin.X - 5, Y1 = origin.Y, X2 = origin.X - 30, Y2 = origin.Y + 15, Stroke = Brushes.DarkBlue, StrokeThickness = 3 };
            Line rightWing = new Line { X1 = origin.X + 5, Y1 = origin.Y, X2 = origin.X + 30, Y2 = origin.Y + 15, Stroke = Brushes.DarkBlue, StrokeThickness = 3 };
            Canvas3D.Children.Add(leftWing); Canvas3D.Children.Add(rightWing);

            Ellipse danger = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Red, Stroke = Brushes.DarkRed, StrokeThickness = 1 };
            Canvas.SetLeft(danger, origin.X - 18); Canvas.SetTop(danger, origin.Y - 29);
            Canvas3D.Children.Add(danger);
        }

        private void DrawMarker(Point center, Color color, double size, string label)
        {
            Ellipse marker = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(marker, center.X - size / 2); Canvas.SetTop(marker, center.Y - size / 2);
            Canvas3D.Children.Add(marker);

            TextBlock tb = new TextBlock { Text = label, FontSize = 10, FontWeight = System.Windows.FontWeights.Bold, Foreground = new SolidColorBrush(color), Background = Brushes.White };
            Canvas.SetLeft(tb, center.X + 10); Canvas.SetTop(tb, center.Y - 10);
            Canvas3D.Children.Add(tb);
        }

        private void DrawLegend(double width)
        {
            double x = width - 150, y = 40;
            Rectangle bg = new Rectangle { Width = 130, Height = 85, Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 1 };
            Canvas.SetLeft(bg, x); Canvas.SetTop(bg, y);
            Canvas3D.Children.Add(bg);

            Line redLine = new Line { X1 = x + 10, Y1 = y + 15, X2 = x + 30, Y2 = y + 15, Stroke = Brushes.Red, StrokeThickness = 3 };
            Canvas3D.Children.Add(redLine);
            TextBlock redText = new TextBlock { Text = "Кресло", FontSize = 9 };
            Canvas.SetLeft(redText, x + 35); Canvas.SetTop(redText, y + 10);
            Canvas3D.Children.Add(redText);

            Line greenLine = new Line { X1 = x + 10, Y1 = y + 35, X2 = x + 30, Y2 = y + 35, Stroke = Brushes.Green, StrokeThickness = 3 };
            Canvas3D.Children.Add(greenLine);
            TextBlock greenText = new TextBlock { Text = "Парашют", FontSize = 9 };
            Canvas.SetLeft(greenText, x + 35); Canvas.SetTop(greenText, y + 30);
            Canvas3D.Children.Add(greenText);

            Ellipse danger = new Ellipse { Width = 8, Height = 8, Fill = Brushes.Red };
            Canvas.SetLeft(danger, x + 20); Canvas.SetTop(danger, y + 55);
            Canvas3D.Children.Add(danger);
            TextBlock dangerText = new TextBlock { Text = "Киль", FontSize = 9 };
            Canvas.SetLeft(dangerText, x + 35); Canvas.SetTop(dangerText, y + 50);
            Canvas3D.Children.Add(dangerText);
        }

        private void DrawOxyPlots()
        {
            if (trajectory == null) return;

            var plotV = new PlotModel { Title = "Скорость" };
            var seriesV = new LineSeries { Color = OxyColor.FromRgb(0, 0, 255), StrokeThickness = 2 };
            foreach (var s in trajectory) seriesV.Points.Add(new DataPoint(s.Time, Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy + s.Vz * s.Vz)));
            plotV.Series.Add(seriesV);
            plotV.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Время (с)" });
            plotV.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Скорость (м/с)" });
            PlotVelocity.Model = plotV;

            var plotN = new PlotModel { Title = "Перегрузка" };
            var seriesN = new LineSeries { Color = OxyColor.FromRgb(255, 0, 0), StrokeThickness = 2 };
            for (int i = 1; i < trajectory.Count; i++)
            {
                var prev = trajectory[i - 1]; var curr = trajectory[i];
                double dt_local = curr.Time - prev.Time;
                double ax = (curr.Vx - prev.Vx) / dt_local, ay = (curr.Vy - prev.Vy) / dt_local, az = (curr.Vz - prev.Vz) / dt_local;
                double a = Math.Sqrt(ax * ax + (ay + g) * (ay + g) + az * az);
                seriesN.Points.Add(new DataPoint(curr.Time, a / g));
            }
            plotN.Series.Add(seriesN);
            plotN.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Время (с)" });
            plotN.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Перегрузка (ед.)" });
            PlotOverload.Model = plotN;
        }

        private void Canvas3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            zoomLevel = Math.Max(0.5, Math.Min(3.0, zoomLevel + (e.Delta > 0 ? 0.1 : -0.1)));
            DrawTrajectory(ParseDouble(txtVc.Text));
        }
        private void Canvas3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isPanning = true; lastMousePos = e.GetPosition(Canvas3D); Canvas3D.CaptureMouse();
        }
        private void Canvas3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isPanning = false; Canvas3D.ReleaseMouseCapture();
        }
        private void Canvas3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning && trajectory != null)
            {
                Point curr = e.GetPosition(Canvas3D);
                double dx = (curr.X - lastMousePos.X) / Canvas3D.ActualWidth;
                double dy = (curr.Y - lastMousePos.Y) / Canvas3D.ActualHeight;
                panX += dx; panY -= dy;
                lastMousePos = curr;
                DrawTrajectory(ParseDouble(txtVc.Text));
            }
        }
        private void Canvas3D_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            zoomLevel = 1.0; panX = panY = 0;
            DrawTrajectory(ParseDouble(txtVc.Text));
        }
    }

    public class Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}