namespace Logger.Entities
{
    public class SensorReading
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public float Pressure { get; set; }
        public int AirQuality { get; set; }
        public int LightLevel { get; set; }
        public string ReadingDate { get; set; } = string.Empty;
        public string ReadingTime { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? WifiStatus { get; set; }
        public string? NtpSync { get; set; }
    }
}
