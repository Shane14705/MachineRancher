using MachineRancher;
using Microsoft.Extensions.Configuration;


MachineMonitor monitor = new MachineMonitor();
await monitor.Monitor();