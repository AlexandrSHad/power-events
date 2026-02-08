# Phase 1: Windows Service

**File: `windows-service/power-events-service.cs`**

## 1.1 Add LibreHardwareMonitorLib Package

Add package directive after existing `Hardware.Info` line:

```csharp
#:package LibreHardwareMonitorLib@0.9.5
```

Add using directive:

```csharp
using LibreHardwareMonitor.Hardware;
```

## 1.2 Fix CPU Averaging Bug

**Current (bug):** Only reads first CPU object.

```csharp
var cpuPercent = hardwareInfo.CpuList[0].PercentProcessorTime;
```

**Fixed:** Average across all CPUs.

```csharp
var cpuPercent = hardwareInfo.CpuList.Average(cpu => (double)cpu.PercentProcessorTime);
```

## 1.3 Expand SystemMetricsData

Add nullable fields for temperature and battery. Nullable so JSON emits `null` when sensors are unavailable, and ESP32 handles gracefully.

```csharp
internal sealed class SystemMetricsData
{
    public required double CpuPercent { get; set; }
    public required double RamPercent { get; set; }
    public double? CpuTempCelsius { get; set; }
    public double? GpuTempCelsius { get; set; }
    public double? BatteryPercent { get; set; }
    public bool? BatteryCharging { get; set; }
    public required DateTime Timestamp { get; set; }
}
```

## 1.4 Add UpdateVisitor Class

Required by LibreHardwareMonitorLib to traverse and update hardware sensors.

```csharp
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
```

## 1.5 Initialize LibreHardwareMonitor Computer

In `SystemMetricsBackgroundService.ExecuteAsync`, before the `while` loop:

```csharp
var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true
};
computer.Open();
```

Close in a `finally` block:

```csharp
finally
{
    computer.Close();
}
```

## 1.6 Read Temperature Data

Inside the polling loop, after `RefreshMemoryStatus()`:

```csharp
computer.Accept(new UpdateVisitor());
double? cpuTemp = null;
double? gpuTemp = null;

foreach (var hw in computer.Hardware)
{
    if (hw.HardwareType == HardwareType.Cpu)
    {
        // Prefer "Package" sensor, fallback to first temp sensor
        var packageSensor = hw.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature &&
            s.Value.HasValue &&
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));

        var tempSensor = packageSensor ?? hw.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature && s.Value.HasValue);

        if (tempSensor != null)
            cpuTemp = Math.Round(tempSensor.Value!.Value, 1);
    }

    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
    {
        // Prefer "Core" sensor, fallback to first temp sensor
        var coreSensor = hw.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature &&
            s.Value.HasValue &&
            s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));

        var tempSensor = coreSensor ?? hw.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature && s.Value.HasValue);

        if (tempSensor != null)
            gpuTemp = Math.Round(tempSensor.Value!.Value, 1);
    }
}
```

## 1.7 Read Battery Data

Inside the polling loop:

```csharp
hardwareInfo.RefreshBatteryList();
double? batteryPercent = null;
bool? batteryCharging = null;

if (hardwareInfo.BatteryList.Count > 0)
{
    var battery = hardwareInfo.BatteryList[0];
    batteryPercent = battery.EstimatedChargeRemaining;
    batteryCharging = battery.BatteryStatusDescription?.Contains("Charging",
        StringComparison.OrdinalIgnoreCase) ?? false;
}
```

## 1.8 Update Metrics Construction and Logging

```csharp
var metricsData = new SystemMetricsData
{
    CpuPercent = cpuPercent,
    RamPercent = memoryPercent,
    CpuTempCelsius = cpuTemp,
    GpuTempCelsius = gpuTemp,
    BatteryPercent = batteryPercent,
    BatteryCharging = batteryCharging,
    Timestamp = DateTime.Now
};

logger.LogInformation(
    "Metrics: CPU {CpuPercent}%, RAM {RamPercent}%, CPU Temp {CpuTemp}C, GPU Temp {GpuTemp}C, Battery {BatteryPercent}% ({BatteryStatus})",
    cpuPercent, memoryPercent, cpuTemp, gpuTemp, batteryPercent,
    batteryCharging == true ? "Charging" : "Discharging");
```

## Verification

1. Deploy with `deploy.cmd`
2. `mosquitto_sub -t system-metrics` - verify JSON has all new fields
3. Confirm CPU% looks reasonable (averaged across all processors)
4. Confirm temperatures in 30-90°C range
5. Confirm battery fields populated or null
