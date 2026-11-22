using System;
using System.Diagnostics;

namespace LinkScope
{
    internal static class GameSirBattery
    {
        internal const string Nova2LiteUsb = "3537:100F";
        private const int ReportLength = 65;
        private const int BatteryOffset = 19;

        // Device-info query documented by AJAY0406/Gamesir-Nova-2-Lite-Battery-Detection.
        internal static void ReadNova2Lite(string path, Device controller)
        {
            using (var handle = Receiver.Open(path, 0xc0000000))
            {
                if (handle.IsInvalid) return;

                var command = new byte[ReportLength];
                command[1] = 0x01;
                command[2] = 0x01;
                if (!Receiver.Transfer(handle, command, true, 150)) return;

                var reply = new byte[ReportLength];
                var timer = Stopwatch.StartNew();
                int remaining;
                while ((remaining = 500 - (int)timer.ElapsedMilliseconds) > 0)
                {
                    // Leave a rejected value if a short reply doesn't contain the battery field.
                    reply[BatteryOffset] = 0xfe;
                    if (!Receiver.Transfer(handle, reply, false, remaining)) return;
                    if (reply[0] != 0 || reply[1] != 0x01) continue;

                    int value = reply[BatteryOffset];
                    if (value > 100 && value != 0xff) continue;
                    if (value <= 100)
                    {
                        controller.Battery = value;
                        controller.Source = "GameSir 电量";
                    }
                    else
                    {
                        controller.Level = "充电中";
                        controller.Source = "GameSir · 充电中";
                    }
                    return;
                }
            }
        }
    }
}
