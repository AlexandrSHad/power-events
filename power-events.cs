#:property TargetFramework=net10.0-windows
#:package System.Management@10.0.1

using System.Management;

Console.WriteLine("Power Events Logger Started");

// Subscribe to WMI events for sleep and wake
var watcher = new ManagementEventWatcher(
    new WqlEventQuery("SELECT * FROM Win32_PowerManagementEvent"));

watcher.EventArrived += OnPowerEvent;
watcher.Start();

Console.WriteLine("Listening for power events. Press Ctrl+C to exit.");

// Keep the application running
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("Exiting...");
    e.Cancel = true;
    watcher.Stop();
    watcher.Dispose();
};

// Block the main thread
while (true)
{
    System.Threading.Thread.Sleep(1000);
}

static void OnPowerEvent(object sender, EventArrivedEventArgs e)
{
    var eventType = (int)e.NewEvent.Properties["EventType"].Value;

    Console.WriteLine(e);
    Console.WriteLine(e.NewEvent);
    foreach (var property in e.NewEvent.Properties)
    {
        Console.WriteLine($"Property: {property.Name} = {property.Value}");
    }

    // Log the power event based on the event type
    switch (eventType)
    {
        case 4:
            Console.WriteLine($"{DateTime.Now} - System is entering sleep.");
            break;
        case 7:
            Console.WriteLine($"{DateTime.Now} - System has resumed from sleep.");
            break;
        default:
            Console.WriteLine($"{DateTime.Now} - Power event occurred: {eventType}");
            break;
    }
}