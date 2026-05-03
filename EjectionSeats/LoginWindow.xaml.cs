using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Npgsql;

namespace EjectionSeats
{
    public partial class LoginWindow : Window
    {
        private const string connectionString =
            "Host=localhost;Port=5432;Database=EjectionSeatsDB;Username=postgres;Password=Passw0rd";

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                txtError.Text = "Введите логин и пароль.";
                return;
            }

            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(
                        @"SELECT u.password, u.id, r.code 
                  FROM users u 
                  JOIN user_roles r ON u.role_id = r.id 
                  WHERE u.login = @login AND u.is_active = TRUE", conn))
                    {
                        cmd.Parameters.AddWithValue("login", login);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string storedPassword = reader.GetString(0);
                                int userId = reader.GetInt32(1);
                                string roleCode = reader.GetString(2);

                                // Сравнение паролей напрямую (без хеширования)
                                if (password == storedPassword)
                                {
                                    // Успешный вход
                                    LogAuth(userId, true, null);
                                    MainWindow mainWindow = new MainWindow(roleCode, userId);
                                    mainWindow.Show();
                                    this.Close();
                                }
                                else
                                {
                                    LogAuth(userId, false, "Неверный пароль");
                                    txtError.Text = "Неверный пароль.";
                                }
                            }
                            else
                            {
                                txtError.Text = "Пользователь не найден.";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения к БД: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ComputeHash(string password, string salt)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password + salt);  // именно password + salt
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private void LogAuth(int userId, bool success, string failureReason)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(
                        "INSERT INTO auth_log (user_id, login_time, ip_address, success, failure_reason) " +
                        "VALUES (@userId, NOW(), '127.0.0.1', @success, @reason)", conn))
                    {
                        cmd.Parameters.AddWithValue("userId", userId);
                        cmd.Parameters.AddWithValue("success", success);
                        cmd.Parameters.AddWithValue("reason", (object)failureReason ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { /* не мешаем */ }
        }
    }
}