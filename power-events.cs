#:property TargetFramework=net10.0-windows
#:package System.Diagnostics.EventLog@10.0.1

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

Console.WriteLine("Listening for power events in Event Log...");

using (var eventLog = new EventLog("System"))
{
    eventLog.EntryWritten += OnEntryWritten;
    eventLog.EnableRaisingEvents = true;

    Console.WriteLine("Press Enter to exit.");
    Console.ReadLine();
}

static async void OnEntryWritten(object sender, EntryWrittenEventArgs e)
{
    // Filter for Kernel-Power events only
    if (e.Entry.Source is not "Microsoft-Windows-Kernel-Power")
    {
        return;
    }

    Console.WriteLine("");
    Console.WriteLine("----------------------------------------");
    Console.WriteLine($"New event log entry: {e.Entry.InstanceId}");
    Console.WriteLine($"Category: {e.Entry.Category}");
    Console.WriteLine($"Category number: {e.Entry.CategoryNumber}");
    Console.WriteLine($"Entry type: {e.Entry.EntryType}");
    Console.WriteLine($"Source: {e.Entry.Source}");
    Console.WriteLine($"Message: {e.Entry.Message}");
    Console.WriteLine($"Time Generated: {e.Entry.TimeGenerated}");
    Console.WriteLine($"Time Written: {e.Entry.TimeWritten}");
    Console.WriteLine($"Index: {e.Entry.Index}");
    Console.WriteLine($"Machine Name: {e.Entry.MachineName}");
    Console.WriteLine($"User Name: {e.Entry.UserName}");
    if (e.Entry.Data != null && e.Entry.Data.Length > 0)
    {
        Console.WriteLine($"Data: {BitConverter.ToString(e.Entry.Data)}");
    }
    if (e.Entry.ReplacementStrings != null && e.Entry.ReplacementStrings.Length > 0)
    {
        Console.WriteLine("Replacement Strings:");
        foreach (var str in e.Entry.ReplacementStrings)
        {
            Console.WriteLine($"  - {str}");
        }
    }

    // Ignore other events for now
    if (e.Entry.InstanceId != 506 && e.Entry.InstanceId != 507)
    {
        return;
    }

    // call http endpoint to log power event
    using var httpClient = new HttpClient();

    var state = e.Entry.InstanceId switch {
        506 => "Standby",
        507 => "Awake",
        _ => "Unknown"
    };

    var powerEventData = new PowerEventData
    {
        State = state,
        TimeGenerated = e.Entry.TimeGenerated
    };
    try
    {
        var response = await httpClient.PostAsJsonAsync(
            "http://localhost:5000/power-events",
            powerEventData,
            SourceGenerationContext.Default.PowerEventData
        );
        Console.WriteLine($"Logged to server: {response.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: Failed to log to server. Error message: {ex.Message}");
    }
}

class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PowerEventData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

// ============= Description of power event Instance IDs ================ 

// 506 - The system is entering Modern Standby (S0 Low Power Idle) sleep.
//       Replacement Strings:
//       [0] Reason 12 
//       [1] LidOpenState false 
//       [2] ExternalMonitorConnectedState true 
//       [3] ScenarioInstanceId 98 
//       [4] BatteryRemainingCapacityOnEnter 92270 
//       [5] BatteryFullChargeCapacityOnEnter 92270 
//       [6] ScenarioInstanceIdV2 98 
//       [7] BootId 42 

// 507 - The system is exiting Modern Standby (S0 Low Power Idle) sleep.
//       Replacement Strings:
//       [0] EnergyDrain 0
//       [1] ActiveResidencyInUs 345577906 
//       [2] NonDripsTimeActivatedInUs 0 
//       [3] FirstDripsEntryInUs 0 
//       [4] DripsResidencyInUs 0 
//       [5] DurationInUs 345577906 
//       [6] DripsTransitions 0 
//       [7] FullChargeCapacityRatio 96 
//       [8] AudioPlaying false 
//       [9] Reason 31 - Input Keyboard, 32 - Input Mouse
//       [10] AudioPlaybackInUs 0 
//       [11] NonActivatedCpuInUs 0 
//       [12] PowerStateAc true 
//       [13] HwDripsResidencyInUs 0 
//       [14] ExitLatencyInUs 185674 
//       [15] DisconnectedStandby false 
//       [16] AoAcCompliantNic true 
//       [17] NonAttributedCpuInUs 0 
//       [18] ModernSleepEnabledActionsBitmask 7 
//       [19] ModernSleepAppliedActionsBitmask 0 
//       [20] LidOpenState false 
//       [21] ExternalMonitorConnectedState true 
//       [22] ScenarioInstanceId 100 
//       [23] IsCsSessionInProgressOnExit false 
//       [24] BatteryRemainingCapacityOnExit 92270 
//       [25] BatteryFullChargeCapacityOnExit 92270 
//       [26] ScenarioInstanceIdV2 98 
//       [27] BootId 42 
//       [28] InputSuppressionActionCount 0 
//       [29] NonResiliencyTimeInUs 345577906 
//       [30] ResiliencyDripsTimeInUs 0 
//       [31] ResiliencyHwDripsTimeInUs 0 
//       [32] GdiOnTime 0 
//       [33] DwmSyncFlushTime 0 
//       [34] MonitorPowerOnTime 184528 
//       [35] SleepEntered false 
//       [36] ScreenOffEnergyCapacityAtStart 92270 
//       [37] ScreenOffEnergyCapacityAtEnd 92270 
//       [38] ScreenOffDurationInUs 345576530 
//       [39] SleepEnergyCapacityAtStart 0 
//       [40] SleepEnergyCapacityAtEnd 0 
//       [41] SleepDurationInUs 0 
//       [42] ScreenOffFullEnergyCapacityAtStart 92270 
//       [43] ScreenOffFullEnergyCapacityAtEnd 92270 
//       [44] SleepFullEnergyCapacityAtStart 0 
//       [45] SleepFullEnergyCapacityAtEnd 0 
//       [46] PowerSchemeInfo 2 
//       [47] PowerButtonSuppressionActionCount 0 
//       [48] ScreenOffSwDripsResidencyInUs 0 
//       [49] ScreenOffHwDripsResidencyInUs 0 
//       [50] SleepSwDripsResidencyInUs 0 
//       [51] SleepHwDripsResidencyInUs 0 

