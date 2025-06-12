using System;
using System.IO.Ports;
using System.Threading;
using System.Data.SQLite;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

class Esp32DataLogger
{
    private SerialPort _serialPort;
    private const int DefaultBaudRate = 115200;
    private bool _connected = false;
    private string _dbPath = "sensor_data.db";
    private SQLiteConnection _dbConnection;

    public void InitializeDatabase()
    {
        bool dbExists = File.Exists(_dbPath);
        _dbConnection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
        _dbConnection.Open();

        // Проверяем структуру таблицы и при необходимости пересоздаем
        CheckDatabaseStructure();

        Console.WriteLine(dbExists ?
            "Подключено к существующей базе данных" :
            "Создана новая база данных");
    }

    private void CheckDatabaseStructure()
    {
        try
        {
            // Проверяем существование таблицы
            string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='sensor_readings'";
            using (var cmd = new SQLiteCommand(checkTableSql, _dbConnection))
            {
                if (cmd.ExecuteScalar() == null)
                {
                    CreateDatabaseTables();
                    return;
                }
            }

            // Проверяем наличие всех необходимых колонок
            string checkColumnsSql = "PRAGMA table_info(sensor_readings)";
            using (var cmd = new SQLiteCommand(checkColumnsSql, _dbConnection))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    bool hasDate = false, hasTime = false, hasIp = false;
                    while (reader.Read())
                    {
                        string column = reader["name"].ToString();
                        if (column == "reading_date") hasDate = true;
                        if (column == "reading_time") hasTime = true;
                        if (column == "ip_address") hasIp = true;
                    }

                    if (!hasDate || !hasTime || !hasIp)
                    {
                        Console.WriteLine("Обновление структуры базы данных...");
                        RecreateDatabaseTables();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка проверки структуры БД: {ex.Message}");
            RecreateDatabaseTables();
        }
    }

    private void CreateDatabaseTables()
    {
        string sql = @"
        DROP TABLE IF EXISTS sensor_readings;
        
        CREATE TABLE sensor_readings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
            temperature REAL NOT NULL,
            humidity REAL NOT NULL,
            pressure REAL NOT NULL,
            air_quality INTEGER NOT NULL,
            light_level INTEGER NOT NULL,
            reading_date TEXT NOT NULL,
            reading_time TEXT NOT NULL,
            ip_address TEXT,
            wifi_status TEXT,
            ntp_sync TEXT
        );";

        using (var command = new SQLiteCommand(sql, _dbConnection))
        {
            command.ExecuteNonQuery();
        }
    }

    private void RecreateDatabaseTables()
    {
        try
        {
            // Создаем временную копию старых данных, если они есть
            BackupExistingData();

            // Пересоздаем таблицы
            CreateDatabaseTables();

            Console.WriteLine("Структура базы данных успешно обновлена");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обновления БД: {ex.Message}");
        }
    }

    private void BackupExistingData()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                string backupPath = $"backup_{DateTime.Now:yyyyMMddHHmmss}.db";
                File.Copy(_dbPath, backupPath);
                Console.WriteLine($"Создана резервная копия: {backupPath}");
            }
        }
        catch { /* Игнорируем ошибки резервного копирования */ }
    }

    public void FindAndConnect()
    {
        Console.WriteLine("=== Поиск ESP32 Sensor Gateway ===");

        string[] ports = SerialPort.GetPortNames();
        if (ports.Length == 0)
        {
            Console.WriteLine("COM-порты не обнаружены.");
            return;
        }

        Console.WriteLine($"Найдены порты: {string.Join(", ", ports)}");

        foreach (string port in ports)
        {
            if (TryConnectToEsp32(port))
            {
                _connected = true;
                Console.WriteLine($"\n=== Успешное подключение к ESP32 на {port} ===");
                StartDataListening();
                break;
            }
        }
    }

    private bool TryConnectToEsp32(string portName)
    {
        Console.WriteLine($"\nПроверка порта {portName}...");

        try
        {
            _serialPort = new SerialPort(portName, DefaultBaudRate)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                NewLine = "\r\n",
                DtrEnable = true
            };

            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // Даем время ESP32 на инициализацию
            Thread.Sleep(2000);

            // Отправляем запрос данных
            _serialPort.WriteLine("{\"command\":\"get_data\"}");

            // Ожидаем ответ в течение 3 секунд
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < 3)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    string response = _serialPort.ReadLine();
                    Console.WriteLine($"Получен ответ: {response}");

                    if (IsValidSensorData(response))
                    {
                        return true;
                    }
                }
                Thread.Sleep(100);
            }

            _serialPort.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }

        return false;
    }

    private bool IsValidSensorData(string json)
    {
        try
        {
            var data = JObject.Parse(json);
            return data["temperature"] != null && data["humidity"] != null;
        }
        catch
        {
            return false;
        }
    }

    private void StartDataListening()
    {
        _serialPort.DataReceived += (sender, e) =>
        {
            try
            {
                while (_serialPort.BytesToRead > 0)
                {
                    string jsonData = _serialPort.ReadLine();
                    if (!string.IsNullOrWhiteSpace(jsonData) && jsonData.StartsWith("{"))
                    {
                        Console.WriteLine($"Получены данные: {jsonData}");
                        ProcessSensorData(jsonData);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка чтения: {ex.Message}");
            }
        };

        // Периодический запрос данных каждые 5 секунд
        new Thread(() =>
        {
            while (_connected)
            {
                try
                {
                    _serialPort.WriteLine("{\"command\":\"get_data\"}");
                    Thread.Sleep(5000);
                }
                catch
                {
                    _connected = false;
                }
            }
        })
        { IsBackground = true }.Start();
    }

    private void ProcessSensorData(string jsonData)
    {
        try
        {
            var sensorData = JsonConvert.DeserializeObject<SensorData>(jsonData);
            SaveSensorData(sensorData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка обработки данных: {ex.Message}");
        }
    }

    private void SaveSensorData(SensorData data)
    {
        string sql = @"
        INSERT INTO sensor_readings (
            temperature, humidity, pressure, air_quality, light_level,
            reading_date, reading_time, ip_address, wifi_status, ntp_sync
        ) VALUES (
            @temp, @hum, @press, @air, @light,
            @date, @time, @ip, @wifi, @ntp
        )";

        using (var command = new SQLiteCommand(sql, _dbConnection))
        {
            command.Parameters.AddWithValue("@temp", data.Temperature);
            command.Parameters.AddWithValue("@hum", data.Humidity);
            command.Parameters.AddWithValue("@press", data.Pressure);
            command.Parameters.AddWithValue("@air", data.AirQuality);
            command.Parameters.AddWithValue("@light", data.LightLevel);
            command.Parameters.AddWithValue("@date", data.Date);
            command.Parameters.AddWithValue("@time", data.Time);
            command.Parameters.AddWithValue("@ip", data.IpAddress);
            command.Parameters.AddWithValue("@wifi", data.WifiStatus);
            command.Parameters.AddWithValue("@ntp", data.NtpSync);

            command.ExecuteNonQuery();
            Console.WriteLine("Данные сохранены в базу");
        }
    }

    public void Disconnect()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _connected = false;
            _serialPort.Close();
            _serialPort.Dispose();
            Console.WriteLine("\n=== Соединение закрыто ===");
        }

        if (_dbConnection != null)
        {
            _dbConnection.Close();
            _dbConnection.Dispose();
            Console.WriteLine("Отключение от базы данных");
        }
    }

class SensorData
{
    [JsonProperty("temperature")]
    public float Temperature { get; set; }

    [JsonProperty("humidity")]
    public float Humidity { get; set; }

    [JsonProperty("pressure")]
    public float Pressure { get; set; }

    [JsonProperty("airQuality")]
    public int AirQuality { get; set; }

    [JsonProperty("lightLevel")]
    public int LightLevel { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("ipAddress")]
    public string IpAddress { get; set; }

    [JsonProperty("wifiStatus")]
    public string WifiStatus { get; set; }

    [JsonProperty("ntpSync")]
    public string NtpSync { get; set; }
}


    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Создаем иконку в трее
            var notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = "ESP32 Data Logger",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Открыть лог", null, OpenLog);
            contextMenu.Items.Add("Настройки", null, OpenSettings);
            contextMenu.Items.Add("Выход", null, ExitApp);
            notifyIcon.ContextMenuStrip = contextMenu;

            // Двойной клик по иконке
            notifyIcon.DoubleClick += (s, e) => OpenLog(s, e);

            // Запускаем логгер
            var logger = new Esp32DataLogger();
            logger.InitializeDatabase();
            logger.FindAndConnect();

            Application.Run();

            // При выходе
            logger.Disconnect();
            notifyIcon.Dispose();
        }

        static void OpenLog(object sender, EventArgs e)
        {
            // Здесь код для открытия лога
            System.Diagnostics.Process.Start("notepad.exe", "sensor_data.db");
        }

        static void OpenSettings(object sender, EventArgs e)
        {
            MessageBox.Show("Здесь будут настройки", "Настройки");
        }

        static void ExitApp(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}