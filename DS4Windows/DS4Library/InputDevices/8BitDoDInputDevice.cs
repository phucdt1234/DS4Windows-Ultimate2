/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DS4Windows.InputDevices
{
    /// <summary>
    /// 8BitDo Ultimate 2 operating in DInput (DirectInput) mode over USB or BT.
    ///
    /// The controller exposes a standard HID gamepad top-level collection
    /// (UsagePage 0x01 / Usage 0x05) with the following report 0x01 layout,
    /// verified from the device's HID descriptor and live reports:
    ///
    ///   [0] Report ID (0x01)
    ///   [1] bits 0-3: Hat switch (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8/15=neutral)
    ///       bits 4-7: unidentified (no events observed on the tested pad)
    ///   [2] Left stick X  (usage X,  0..255, 0x80 center)
    ///   [3] Left stick Y  (usage Y,  0..255, 0x80 center, 0 = up)
    ///   [4] Right stick X (usage Z,  0..255, 0x80 center)
    ///   [5] Right stick Y (usage Rz, 0..255, 0x80 center, 0 = up)
    ///   [6] R2 analog (Accelerator, usage 0xC4 Simulation page, 0..255)
    ///   [7] L2 analog (Brake, usage 0xC5 Simulation page, 0..255)
    ///   [8] A, B, PR, X, Y, PL, LB(L1), RB(R1)
    ///   [9] L2 click, R2 click, Minus(Share), Plus(Options), Home(PS), L3, R3,
    ///       bit 7: Star (pad firmware turbo modifier; not mapped to output)
    ///   [10] L4(SideL), R4(SideR)
    ///   [15..26] IMU: accel XYZ + gyro XYZ as 6x int16 LE (see DecodeImu)
    ///
    /// Button placement was verified by pressing every physical control and
    /// reading the raw report; see DecodeInputReport for the bit map. No
    /// lightbar/rumble writes are attempted (DInput mode exposes no known
    /// output format); battery level is not reported by the pad.
    /// </summary>
    public class EightBitDoDInputDevice : DS4Device
    {
        private const int INPUT_REPORT_LEN = 34;
        private const int OUTPUT_REPORT_LEN = 5;

        private new const int WARN_INTERVAL_USB = 30;
        private new const int WARN_INTERVAL_BT = 40;

        private const string BLUETOOTH_HID_GUID = "{00001124-0000-1000-8000-00805F9B34FB}";

        // IMU: 6x int16 little-endian starting at report byte 15 (verified from
        // live captures: accel Z reads ~4091 with the pad lying flat => 4096 LSB/g;
        // gyro axes rest within a few counts of zero).
        //   [15..16] accel X  [17..18] accel Y  [19..20] accel Z
        //   [21..22] gyro X   [23..24] gyro Y   [25..26] gyro Z
        private const float IMU_ACCEL_LSB_PER_G = 4096.0f;

        // The pad reports at ~1000 Hz but the DS4Windows mapping/output stack
        // was designed around 250 Hz. Fire the Report event every Nth report
        // (N=2 -> 500 Hz) so mapping, the processing pipeline and the ViGEm
        // submission are not overloaded; worst-case added latency is N ms.
        private const int REPORT_PROCESS_DIVIDER = 2;

        private bool connectionOpened = false;
        private EightBitDoDInputControllerOptions nativeOptionsStore;

        private byte[] inputReportBuffer;

        public override event ReportHandler<EventArgs> Report = null;
        public override event EventHandler<EventArgs> Removal = null;

        public override event EventHandler BatteryChanged;
        public override event EventHandler ChargingChanged;

        public EightBitDoDInputDevice(HidDevice hidDevice,
            string disName, VidPidFeatureSet featureSet = VidPidFeatureSet.DefaultDS4) :
            base(hidDevice, disName, featureSet)
        {
            runCalib = false;
            synced = true;

            warnInterval = WARN_INTERVAL_USB;

            DeviceSlotNumberChanged += (sender, e) => { };
            Removal += EightBitDoDInputDevice_Removal;
        }

        private void EightBitDoDInputDevice_Removal(object sender, EventArgs e)
        {
            connectionOpened = false;
        }

        public static ConnectionType DetermineConnectionType(HidDevice hDevice)
        {
            ConnectionType result;
            if (hDevice.DevicePath.ToUpper().Contains(BLUETOOTH_HID_GUID))
            {
                result = ConnectionType.BT;
            }
            else
            {
                result = ConnectionType.USB;
            }

            return result;
        }

        public override void PostInit()
        {
            deviceType = InputDeviceType.EightBitDoDInput;
            gyroMouseSensSettings = new GyroMouseSens();
            conType = DetermineConnectionType(hDevice);
            optionsStore = nativeOptionsStore = new EightBitDoDInputControllerOptions(deviceType);
            // String serial read; HidDevice falls back to a device-path derived
            // serial when the pad does not expose a serial number string.
            Mac = hDevice.ReadSerial(SerialReportID);

            warnInterval = conType == ConnectionType.BT ? WARN_INTERVAL_BT : WARN_INTERVAL_USB;

            inputReportBuffer = new byte[hDevice.Capabilities.InputReportByteLength];
        }

        public override void StartUpdate()
        {
            this.inputReportErrorCount = 0;

            if (!connectionOpened)
            {
                connectionOpened = true;
            }

            if (ds4Input == null)
            {
                ds4Input = new Thread(ReadInput);
                ds4Input.IsBackground = true;
                ds4Input.Priority = ThreadPriority.AboveNormal;
                ds4Input.Name = "8BitDo DInput Reader Thread";
                ds4Input.Start();
            }
        }

        protected override void StopOutputUpdate()
        {
        }

        /// <summary>
        /// Decodes a raw DInput report 0x01 into a DS4State. Pure mapping of
        /// bytes to controls so the layout can be unit tested without hardware.
        ///
        /// Button layout verified against the physical pad (button test round):
        ///   byte 8: A, B, PR, X, Y, PL, LB(L1), RB(R1)
        ///   byte 9: L2 click, R2 click, Minus(Share), Plus(Options),
        ///           Home(PS), L3, R3, [unidentified]
        ///   byte 10: L4(SideL), R4(SideR), [unidentified x2]
        ///   byte 6/7: R2/L2 analog (Accelerator is the first of the pair)
        /// </summary>
        public static void DecodeInputReport(byte[] report, DS4State state)
        {
            // Analog sticks (0..255, 0x80 center, Y: 0 = up like DS4)
            state.LX = report[2];
            state.LY = report[3];
            state.RX = report[4];
            state.RY = report[5];

            // Analog triggers: byte 6 is the Accelerator (R2), byte 7 the Brake (L2)
            state.R2 = report[6];
            state.R2Raw = state.R2;
            state.L2 = report[7];
            state.L2Raw = state.L2;

            // Hat switch (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8/15=neutral)
            byte hat = (byte)(report[1] & 0x0F);
            state.DpadUp = hat == 0 || hat == 1 || hat == 7;
            state.DpadRight = hat >= 1 && hat <= 3;
            state.DpadDown = hat >= 3 && hat <= 5;
            state.DpadLeft = hat >= 5 && hat <= 7;

            // Byte 8: face buttons and shoulder inputs
            byte buttons = report[8];
            state.Cross = (buttons & 0x01) != 0;      // A
            state.Circle = (buttons & 0x02) != 0;     // B
            state.BRP = (buttons & 0x04) != 0;        // PR back paddle
            state.Square = (buttons & 0x08) != 0;     // X
            state.Triangle = (buttons & 0x10) != 0;   // Y
            state.BLP = (buttons & 0x20) != 0;        // PL back paddle
            state.L1 = (buttons & 0x40) != 0;         // LB
            state.R1 = (buttons & 0x80) != 0;         // RB

            // Byte 9: trigger clicks and system buttons
            buttons = report[9];
            bool l2Click = (buttons & 0x01) != 0;     // L2 digital click
            bool r2Click = (buttons & 0x02) != 0;     // R2 digital click
            state.L2Btn = l2Click || state.L2 > 0;
            state.R2Btn = r2Click || state.R2 > 0;
            state.Share = (buttons & 0x04) != 0;      // Minus
            state.Options = (buttons & 0x08) != 0;    // Plus
            state.PS = (buttons & 0x10) != 0;         // Home
            state.L3 = (buttons & 0x20) != 0;         // L3
            state.R3 = (buttons & 0x40) != 0;         // R3
            // bit 7 (0x80): Star - pad firmware turbo modifier, not mapped

            // Byte 10: extra shoulder buttons
            buttons = report[10];
            state.SideL = (buttons & 0x01) != 0;      // L4
            state.SideR = (buttons & 0x02) != 0;      // R4
            // bits 2-3 (0x30): unidentified (Phase 3)

            // Byte 1 high nibble: unidentified, emits nothing on the tested pad
        }

        /// <summary>
        /// Decodes the vendor IMU block: 6x int16 little-endian at bytes 15..26
        /// (accel XYZ then gyro XYZ). Pure function for unit testing.
        /// </summary>
        public static void DecodeImu(byte[] report, out short accX, out short accY, out short accZ,
            out short gyrX, out short gyrY, out short gyrZ)
        {
            accX = (short)(report[15] | (report[16] << 8));
            accY = (short)(report[17] | (report[18] << 8));
            accZ = (short)(report[19] | (report[20] << 8));
            gyrX = (short)(report[21] | (report[22] << 8));
            gyrY = (short)(report[23] | (report[24] << 8));
            gyrZ = (short)(report[25] | (report[26] << 8));
        }

        private unsafe void ReadInput()
        {
            unchecked
            {
                Debouncer = SetupDebouncer();
                firstActive = DateTime.UtcNow;
                NativeMethods.HidD_SetNumInputBuffers(hDevice.SafeReadHandle.DangerousGetHandle(), 3);
                Queue<long> latencyQueue = new Queue<long>(21);
                int tempLatencyCount = 0;
                long oldtime = 0;
                string currerror = string.Empty;
                long curtime = 0;
                long testelapsed = 0;
                timeoutEvent = false;
                ds4InactiveFrame = true;
                idleInput = true;
                double elapsedDeltaTime = 0.0;
                byte tempByte = 0;
                long latencySum = 0;

                long previousCheckTime = 0;
                long deltaCheckElapsed;
                double lastCheckElapsed;
                double lastCheckTimeElapsed;

                sixAxis.ResetContinuousCalibration();
                standbySw.Start();

                while (!exitInputThread)
                {
                    oldCharging = charging;
                    currerror = string.Empty;

                    readWaitEv.Set();

                    HidDevice.ReadStatus res = hDevice.ReadFile(inputReportBuffer);
                    if (res == HidDevice.ReadStatus.Success)
                    {
                        if (inputReportBuffer[0] != 0x01)
                        {
                            inputReportErrorCount++;
                            if (inputReportErrorCount > 10)
                            {
                                exitInputThread = true;
                                isDisconnecting = true;
                                Removal?.Invoke(this, EventArgs.Empty);
                            }

                            continue;
                        }
                    }
                    else
                    {
                        exitInputThread = true;
                        isDisconnecting = true;
                        Removal?.Invoke(this, EventArgs.Empty);
                        continue;
                    }

                    inputReportErrorCount = 0;
                    curtime = Stopwatch.GetTimestamp();
                    testelapsed = curtime - oldtime;
                    lastTimeElapsedDouble = testelapsed * (1.0 / Stopwatch.Frequency) * 1000.0;
                    lastTimeElapsed = (long)lastTimeElapsedDouble;
                    elapsedDeltaTime = lastTimeElapsedDouble * .001;

                    deltaCheckElapsed = curtime - previousCheckTime;
                    lastCheckElapsed = deltaCheckElapsed * (1.0 / Stopwatch.Frequency) * 1000.0;
                    lastCheckTimeElapsed = lastCheckElapsed * 0.001;
                    previousCheckTime = curtime;

                    // NOTE: no minimum-delta gate here. The DS4 poll gate in the
                    // base class (lastCheckTimeElapsed <= 0.005) would swallow every
                    // report of a 1000 Hz pad; process each report as it arrives.

                    oldtime = curtime;

                    if (tempLatencyCount >= 20)
                    {
                        latencySum -= latencyQueue.Dequeue();
                        tempLatencyCount--;
                    }

                    latencySum += this.lastTimeElapsed;
                    latencyQueue.Enqueue(this.lastTimeElapsed);
                    tempLatencyCount++;

                    Latency = latencySum / (double)tempLatencyCount;

                    utcNow = DateTime.UtcNow;
                    cState.PacketCounter = pState.PacketCounter + 1;
                    cState.FrameCounter = (byte)(cState.PacketCounter % 128);
                    cState.ReportTimeStamp = utcNow;

                    cState.elapsedTime = elapsedDeltaTime;
                    cState.totalMicroSec = pState.totalMicroSec + (uint)(elapsedDeltaTime * 1000000);

                    // Pad does not expose a battery reading in DInput mode
                    battery = 99;
                    cState.Battery = 99;

                    DecodeInputReport(inputReportBuffer, cState);

                    DecodeImu(inputReportBuffer, out short accX, out short accY, out short accZ,
                        out short gyrX, out short gyrY, out short gyrZ);

                    // Convert to DS4 sensor units: gyro 16 LSB/dps matches the DS4
                    // convention 1:1; accel 4096 LSB/g -> 8192 LSB/g (x2).
                    // Axis permutation verified by live tests (user reports):
                    //   pad yaw axis   = report gyro Z (bytes 25-26)
                    //   pad pitch axis = report gyro Y (bytes 23-24)
                    //   pad roll axis  = report gyro X (bytes 21-22)
                    int aX = accX * 2, aY = accY * 2, aZ = accZ * 2;
                    int yawRaw = gyrZ, pitchRaw = -gyrY, rollRaw = gyrX; // pitch inverted: user report
                    sixAxis.PrepareNonDS4SixAxis(ref yawRaw, ref pitchRaw, ref rollRaw, ref aX, ref aY, ref aZ);

                    // SixAxis.populate expects DS4 raw triples (X->-yaw, Y->+pitch,
                    // Z->-roll) and handles all unit conversions internally.
                    cState.Motion.populate(yawRaw, pitchRaw, rollRaw, aX, aY, aZ,
                        elapsedDeltaTime, pState.Motion);

                    if (sixAxis.HasSixAccelMovedSubscribers)
                    {
                        SixAxisEventArgs args = new SixAxisEventArgs(cState.ReportTimeStamp, cState.Motion);
                        sixAxis.FireSixAxisEvent(args);
                    }

                    if (conType == ConnectionType.USB)
                    {
                        if (idleTimeout == 0)
                        {
                            lastActive = utcNow;
                        }
                        else
                        {
                            idleInput = isDS4Idle();
                            if (!idleInput)
                            {
                                lastActive = utcNow;
                            }
                        }
                    }
                    else
                    {
                        if (idleTimeout == 0 || isRemoved)
                        {
                            lastActive = utcNow;
                        }
                        else
                        {
                            idleInput = isDS4Idle();
                            if (!idleInput)
                            {
                                lastActive = utcNow;
                            }
                        }
                    }

                    if (fireReport && cState.PacketCounter % REPORT_PROCESS_DIVIDER == 0)
                    {
                        Report?.Invoke(this, EventArgs.Empty);
                    }

                    WriteReport();

                    if (!string.IsNullOrEmpty(currerror))
                        error = currerror;
                    else if (!string.IsNullOrEmpty(error))
                        error = string.Empty;

                    pState.Motion.copy(cState.Motion);
                    cState.CopyTo(pState);

                    if (hasInputEvts)
                    {
                        lock (eventQueueLock)
                        {
                            Action tempAct = null;
                            for (int actInd = 0, actLen = eventQueue.Count; actInd < actLen; actInd++)
                            {
                                tempAct = eventQueue.Dequeue();
                                tempAct.Invoke();
                            }

                            hasInputEvts = false;
                        }
                    }
                }
            }

            timeoutExecuted = true;
        }

        public void WriteReport()
        {
            MergeStates();

            // DInput mode exposes no rumble/lightbar output report; haptics
            // requested through DS4Windows are merged but cannot be forwarded.
        }

        public override bool IsAlive()
        {
            return !isDisconnecting && connectionOpened;
        }

        public override bool DisconnectWireless(bool callRemoval = false)
        {
            connectionOpened = false;
            return true;
        }

        public override bool DisconnectBT(bool callRemoval = false)
        {
            connectionOpened = false;
            return true;
        }

        public override bool DisconnectDongle(bool remove = false)
        {
            connectionOpened = false;
            return true;
        }

        public override void LoadStoreSettings()
        {
            if (nativeOptionsStore != null)
            {
            }
        }
    }

    public class EightBitDoDInputControllerOptions : ControllerOptionsStore
    {
        public const string XML_ELEMENT_NAME = "EightBitDoDInputSupportSettings";

        public EightBitDoDInputControllerOptions(InputDeviceType deviceType) :
            base(deviceType)
        {
        }
    }
}
