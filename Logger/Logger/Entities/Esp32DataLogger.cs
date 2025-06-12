using System.IO.Ports;
using System.Text.Json;
using Logger.Data;

namespace Logger.Entities
{
    public class Esp32DataLogger : IDisposable
    {
        private SerialPort? _serialPort;
        private const int DefaultBaudRate = 115200;
        private bool _connected = false;
        private AppDbContext _dbContext;
        private Thread? _pollingThread;

        public Esp32DataLogger()
        {
            _dbContext = new AppDbContext();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            _dbContext.Database.EnsureCreated();
            Console.WriteLine("База данных инициализирована");
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
                    return;
                }
            }

            Console.WriteLine("\n=== Устройство не найдено ===");
        }

        private bool TryConnectToEsp32(string portName)
        {
            Console.WriteLine($"Проверка порта {portName}...");

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

                Thread.Sleep(2000);

                _serialPort.WriteLine("""{"command":"get_data"}""");

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
                Console.WriteLine($"Ошибка подключения: {ex.Message}");
            }
            finally
            {
                _serialPort?.Dispose();
            }

            return false;
        }

        private static bool IsValidSensorData(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("temperature", out _) &&
                       doc.RootElement.TryGetProperty("humidity", out _);
            }
            catch
            {
                return false;
            }
        }

        private void StartDataListening()
        {
            _serialPort!.DataReceived += (sender, e) =>
            {
                try
                {
                    string jsonData = _serialPort.ReadLine();
                    if (!string.IsNullOrWhiteSpace(jsonData) && jsonData.StartsWith('{'))
                    {
                        Console.WriteLine($"Получены данные: {jsonData}");
                        ProcessSensorData(jsonData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка чтения: {ex.Message}");
                }
            };

            _pollingThread = new Thread(() =>
            {
                while (_connected)
                {
                    try
                    {
                        _serialPort.WriteLine("""{"command":"get_data"}""");
                        Thread.Sleep(5000);
                    }
                    catch
                    {
                        _connected = false;
                    }
                }
            })
            { IsBackground = true };

            _pollingThread.Start();
        }

        private void ProcessSensorData(string jsonData)
        {
            try
            {
                var sensorData = JsonSerializer.Deserialize<SensorData>(jsonData);
                if (sensorData != null)
                {
                    SaveSensorData(sensorData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки данных: {ex.Message}");
            }
        }

        private void SaveSensorData(SensorData data)
        {
            try
            {
                var reading = new SensorReading
                {
                    Temperature = data.Temperature,
                    Humidity = data.Humidity,
                    Pressure = data.Pressure,
                    AirQuality = data.AirQuality,
                    LightLevel = data.LightLevel,
                    ReadingDate = data.Date,
                    ReadingTime = data.Time,
                    IpAddress = data.IpAddress ?? string.Empty,
                    WifiStatus = data.WifiStatus ?? string.Empty,
                    NtpSync = data.NtpSync ?? string.Empty
                };

                _dbContext.SensorReadings.Add(reading);
                _dbContext.SaveChanges();
                Console.WriteLine("Данные сохранены в базу");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _connected = false;

            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                Console.WriteLine("\n=== Соединение закрыто ===");
            }

            _pollingThread?.Join(500);
            _dbContext.Dispose();
            Console.WriteLine("Отключение от базы данных");
        }

        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }

    public class SensorData
    {
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public float Pressure { get; set; }
        public int AirQuality { get; set; }
        public int LightLevel { get; set; }
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public string? IpAddress { get; set; }
        public string? WifiStatus { get; set; }
        public string? NtpSync { get; set; }
    }
}
