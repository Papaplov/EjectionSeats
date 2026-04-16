using System;
using System.Collections.Generic;

namespace EjectionSeats.Physics
{
    public class TrajectoryPoint
    {
        public double Time { get; set; }
        public double X { get; set; }      // горизонтальная дальность (м)
        public double Y { get; set; }      // высота (м)
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Ax { get; set; }
        public double Ay { get; set; }
        public bool ParachuteDeployed { get; set; }
        public double Altitude => Y;
    }

    public class EjectionModel
    {
        private const double g = 9.80665;
        private const double Rho0 = 1.225;   // плотность воздуха у земли (кг/м³)

        // Входные параметры
        private double initialAltitude;
        private double initialSpeed;
        private double pitchAngleRad;
        private double bankAngleRad;
        private double mass;
        private double area;
        private double cx;
        private double rocketImpulse;
        private double burnTime;
        private double railAngleRad;
        private double chuteDelay;
        private double chuteArea;

        private double rocketForce;
        private double vx0, vy0;
        private double x, y, vx, vy;
        private double time;
        private bool rocketActive;
        private bool parachuteDeployed;
        private double chuteCx = 1.2;   // коэффициент сопротивления парашюта

        public List<TrajectoryPoint> Points { get; private set; }

        public EjectionModel(double alt, double spd, double pitchDeg, double bankDeg,
                             double m, double a, double cxVal,
                             double impulse, double burnT, double railDeg,
                             double delay, double chuteA)
        {
            initialAltitude = alt;
            initialSpeed = spd;
            pitchAngleRad = pitchDeg * Math.PI / 180.0;
            bankAngleRad = bankDeg * Math.PI / 180.0;
            mass = m;
            area = a;
            cx = cxVal;
            rocketImpulse = impulse;
            burnTime = burnT;
            railAngleRad = railDeg * Math.PI / 180.0;
            chuteDelay = delay;
            chuteArea = chuteA;

            // Сила тяги ракетного двигателя (постоянная в течение времени работы)
            rocketForce = rocketImpulse / burnTime;

            Points = new List<TrajectoryPoint>();
        }

        private double GetAirDensity(double altitude)
        {
            // Стандартная атмосфера (упрощённая барометрическая формула)
            if (altitude < 11000)
                return Rho0 * Math.Pow(1 - altitude / 44330.0, 4.256);
            else
                return Rho0 * 0.297 * Math.Exp((11000 - altitude) / 6340);
        }

        public void RunSimulation(double maxTime = 30.0, double dt = 0.02)
        {
            // Начальные условия: скорость кресла равна скорости самолёта
            double vxPlane = initialSpeed * Math.Cos(pitchAngleRad) * Math.Cos(bankAngleRad);
            double vyPlane = initialSpeed * Math.Sin(pitchAngleRad);

            // Добавляем начальную скорость от катапульты вдоль направляющих
            double ejectSpeed = Math.Sqrt(2 * rocketForce * 1.2 / mass); // примерная начальная скорость
            vx0 = vxPlane + ejectSpeed * Math.Sin(railAngleRad);
            vy0 = vyPlane + ejectSpeed * Math.Cos(railAngleRad);

            x = 0;
            y = initialAltitude;
            vx = vx0;
            vy = vy0;
            time = 0;
            rocketActive = true;
            parachuteDeployed = false;

            Points.Clear();
            Points.Add(CreateCurrentPoint());

            while (time < maxTime && y > 0)
            {
                double dtStep = dt;
                double rho = GetAirDensity(y);
                double speed = Math.Sqrt(vx * vx + vy * vy);

                // Аэродинамическое сопротивление
                double currentArea = area;
                double currentCx = cx;
                if (parachuteDeployed)
                {
                    currentArea = chuteArea;
                    currentCx = chuteCx;
                }
                double dragForce = 0.5 * rho * speed * speed * currentArea * currentCx;
                double axDrag = -dragForce * vx / (speed + 1e-8) / mass;
                double ayDrag = -dragForce * vy / (speed + 1e-8) / mass;

                // Тяга ракеты (только на активном участке и по направляющим)
                double axRocket = 0, ayRocket = 0;
                if (rocketActive && time <= burnTime)
                {
                    double rocketAcc = rocketForce / mass;
                    axRocket = rocketAcc * Math.Sin(railAngleRad);
                    ayRocket = rocketAcc * Math.Cos(railAngleRad);
                }
                else
                {
                    rocketActive = false;
                }

                // Раскрытие парашюта по задержке
                if (!parachuteDeployed && time >= chuteDelay && !rocketActive)
                {
                    parachuteDeployed = true;
                }

                double ax = axDrag + axRocket;
                double ay = ayDrag - g + ayRocket;

                vx += ax * dtStep;
                vy += ay * dtStep;
                x += vx * dtStep;
                y += vy * dtStep;
                time += dtStep;

                if (y < 0) y = 0;

                Points.Add(CreateCurrentPoint());

                // Остановка если почти упали
                if (y <= 0.1 && vy <= 0) break;
            }
        }

        private TrajectoryPoint CreateCurrentPoint()
        {
            return new TrajectoryPoint
            {
                Time = time,
                X = x,
                Y = y,
                Vx = vx,
                Vy = vy,
                Ax = (vx - (Points.Count > 0 ? Points[Points.Count - 1].Vx : vx)) / 0.02,
                Ay = (vy - (Points.Count > 0 ? Points[Points.Count - 1].Vy : vy)) / 0.02,
                ParachuteDeployed = parachuteDeployed
            };
        }
    }
}