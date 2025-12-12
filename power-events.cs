#:package Microsoft.Win32.SystemEvents@10.0.1

using Microsoft.Win32;

Console.WriteLine("Power Events Logger Started");

// Subscribe to PowerModeChanged event
SystemEvents.PowerModeChanged += OnPowerModeChanged;

Console.WriteLine("Listening for power events. Press Ctrl+C to exit.");

// Start a message loop to process system events
using (var waitHandle = new ManualResetEvent(false))
{
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        waitHandle.Set();
    };

    waitHandle.WaitOne();
}

// Unsubscribe from the event before exiting
SystemEvents.PowerModeChanged -= OnPowerModeChanged;

static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
{
    // Log the power mode change to the console
    Console.WriteLine($"{DateTime.Now} - Power mode changed: {e.Mode}");
}
