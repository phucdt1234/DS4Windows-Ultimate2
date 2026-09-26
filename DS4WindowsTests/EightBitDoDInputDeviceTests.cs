using DS4Windows;
using DS4Windows.InputDevices;

namespace DS4WindowsTests
{
    [TestClass]
    public partial class EightBitDoDInputDeviceTests
    {
        // Neutral report captured from the controller: sticks centered,
        // triggers released, hat switch neutral (0x0F), no buttons held.
        private static byte[] NeutralReport() => new byte[]
        {
            0x01, 0x0F, 0x83, 0x7F, 0x81, 0x7F, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x5E, 0x42, 0xFE, 0x2A, 0x00, 0xF2,
            0x0F, 0x02, 0x00, 0x09, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        // Report builder using the layout verified against the physical pad:
        //   byte 8: A(0x01) B(0x02) PR(0x04) X(0x08) Y(0x10) PL(0x20) LB/L1(0x40) RB/R1(0x80)
        //   byte 9: L2click(0x01) R2click(0x02) Minus/Share(0x04) Plus/Options(0x08)
        //           Home/PS(0x10) L3(0x20) R3(0x40)
        //   byte 10: L4/SideL(0x01) R4/SideR(0x02)
        //   byte 6 = R2 analog, byte 7 = L2 analog
        private static byte[] ReportWith(byte byte8 = 0, byte byte9 = 0, byte byte10 = 0,
            byte lx = 0x80, byte ly = 0x80, byte rx = 0x80, byte ry = 0x80,
            byte l2 = 0, byte r2 = 0, byte hat = 0x0F)
        {
            var report = new byte[34];
            report[0] = 0x01;
            report[1] = (byte)(hat & 0x0F);
            report[2] = lx;
            report[3] = ly;
            report[4] = rx;
            report[5] = ry;
            report[6] = r2;
            report[7] = l2;
            report[8] = byte8;
            report[9] = byte9;
            report[10] = byte10;
            return report;
        }

        [TestMethod]
        public void NeutralReport_DecodesToRestingState()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(NeutralReport(), state);

            Assert.AreEqual(0x83, state.LX);
            Assert.AreEqual(0x7F, state.LY);
            Assert.AreEqual(0x81, state.RX);
            Assert.AreEqual(0x7F, state.RY);
            Assert.AreEqual(0, state.L2);
            Assert.AreEqual(0, state.R2);
            Assert.IsFalse(state.L2Btn);
            Assert.IsFalse(state.R2Btn);

            Assert.IsFalse(state.DpadUp);
            Assert.IsFalse(state.DpadDown);
            Assert.IsFalse(state.DpadLeft);
            Assert.IsFalse(state.DpadRight);

            Assert.IsFalse(state.Cross);
            Assert.IsFalse(state.Circle);
            Assert.IsFalse(state.Square);
            Assert.IsFalse(state.Triangle);
            Assert.IsFalse(state.L1);
            Assert.IsFalse(state.R1);
            Assert.IsFalse(state.L3);
            Assert.IsFalse(state.R3);
            Assert.IsFalse(state.Share);
            Assert.IsFalse(state.Options);
            Assert.IsFalse(state.PS);
            Assert.IsFalse(state.BLP);
            Assert.IsFalse(state.BRP);
            Assert.IsFalse(state.SideL);
            Assert.IsFalse(state.SideR);
        }

        [TestMethod]
        public void FaceButtons_DecodeFromByte8()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x01), state);
            Assert.IsTrue(state.Cross);
            Assert.IsFalse(state.Circle);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x02), state);
            Assert.IsTrue(state.Circle);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x08), state);
            Assert.IsTrue(state.Square);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x10), state);
            Assert.IsTrue(state.Triangle);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x1B), state);
            Assert.IsTrue(state.Cross && state.Circle && state.Square && state.Triangle);
        }

        [TestMethod]
        public void ShoulderButtons_L1R1FromByte8HighBits()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x40), state);
            Assert.IsTrue(state.L1);
            Assert.IsFalse(state.R1);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x80), state);
            Assert.IsTrue(state.R1);
            Assert.IsFalse(state.L1);
        }

        [TestMethod]
        public void BackPaddles_MapToBLPBRP()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x20), state);
            Assert.IsTrue(state.BLP);
            Assert.IsFalse(state.BRP);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte8: 0x04), state);
            Assert.IsTrue(state.BRP);
            Assert.IsFalse(state.BLP);
        }

        [TestMethod]
        public void ExtraShoulders_MapToSideLSideR()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte10: 0x01), state);
            Assert.IsTrue(state.SideL);
            Assert.IsFalse(state.SideR);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte10: 0x02), state);
            Assert.IsTrue(state.SideR);
            Assert.IsFalse(state.SideL);
        }

        [TestMethod]
        public void HatSwitch_DecodesAllEightDirections()
        {
            void Check(byte hat, bool up, bool right, bool down, bool left)
            {
                DS4State state = new DS4State();
                EightBitDoDInputDevice.DecodeInputReport(ReportWith(hat: hat), state);
                Assert.AreEqual(up, state.DpadUp, $"hat {hat}: Up");
                Assert.AreEqual(right, state.DpadRight, $"hat {hat}: Right");
                Assert.AreEqual(down, state.DpadDown, $"hat {hat}: Down");
                Assert.AreEqual(left, state.DpadLeft, $"hat {hat}: Left");
            }

            Check(0, true, false, false, false);   // N
            Check(1, true, true, false, false);    // NE
            Check(2, false, true, false, false);   // E
            Check(3, false, true, true, false);    // SE
            Check(4, false, false, true, false);   // S
            Check(5, false, false, true, true);    // SW
            Check(6, false, false, false, true);   // W
            Check(7, true, false, false, true);    // NW
            Check(8, false, false, false, false);  // neutral
            Check(15, false, false, false, false); // neutral
        }

        [TestMethod]
        public void SystemButtons_DecodeFromByte9()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte9: 0x1C), state);
            Assert.IsTrue(state.Share);    // Minus
            Assert.IsTrue(state.Options);  // Plus
            Assert.IsTrue(state.PS);       // Home
            Assert.IsFalse(state.L3);
            Assert.IsFalse(state.R3);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte9: 0x60), state);
            Assert.IsTrue(state.L3);
            Assert.IsTrue(state.R3);
        }

        [TestMethod]
        public void TriggerClicks_SetDigitalButtonState()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte9: 0x01), state);
            Assert.IsTrue(state.L2Btn);
            Assert.IsFalse(state.R2Btn);
            Assert.AreEqual(0, state.L2); // analog untouched by the click bit

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(byte9: 0x02), state);
            Assert.IsTrue(state.R2Btn);
            Assert.IsFalse(state.L2Btn);
        }

        [TestMethod]
        public void AnalogTriggers_Byte7IsL2AndByte6IsR2()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(l2: 128, r2: 255), state);
            Assert.AreEqual(128, state.L2);
            Assert.AreEqual(128, state.L2Raw);
            Assert.IsTrue(state.L2Btn); // analog value alone activates the digital button
            Assert.AreEqual(255, state.R2);
            Assert.AreEqual(255, state.R2Raw);
            Assert.IsTrue(state.R2Btn);

            state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(r2: 64), state);
            Assert.AreEqual(64, state.R2);
            Assert.AreEqual(0, state.L2);
            Assert.IsFalse(state.L2Btn);
        }

        [TestMethod]
        public void Sticks_PassThroughRawRange()
        {
            DS4State state = new DS4State();
            EightBitDoDInputDevice.DecodeInputReport(ReportWith(lx: 0, ly: 255, rx: 0, ry: 255), state);
            Assert.AreEqual(0, state.LX);    // left stick pushed left
            Assert.AreEqual(255, state.LY);  // left stick pushed down
            Assert.AreEqual(0, state.RX);    // right stick pushed left
            Assert.AreEqual(255, state.RY);  // right stick pushed down
        }
    }
}

namespace DS4WindowsTests
{
    public partial class EightBitDoDInputDeviceTests
    {
        // Raw sample captured while the pad was rotated (user capture round 2)
        private static byte[] ImuMovingSample() => new byte[]
        {
            0x01, 0x0F, 0x83, 0x7F, 0x81, 0x7F, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x4D, 0xCA, 0xF6, 0x4B, 0x02, 0x8C,
            0x0E, 0xC8, 0xFE, 0x16, 0x02, 0xCE, 0xFF, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        // Pad lying flat, face up: accel X ~ -460, Y ~ +43, Z ~ +4091 (1g at 4096 LSB/g)
        private static byte[] ImuFlatSample() => new byte[]
        {
            0x01, 0x0F, 0x83, 0x7F, 0x81, 0x7F, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x4D, 0x3F, 0xFE, 0x33, 0x00, 0xFF,
            0x0F, 0x01, 0x00, 0x06, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        [TestMethod]
        public void DecodeImu_ExtractsLittleEndianAxes()
        {
            EightBitDoDInputDevice.DecodeImu(ImuFlatSample(),
                out short accX, out short accY, out short accZ, out short gyrX, out short gyrY, out short gyrZ);

            Assert.AreEqual(unchecked((short)0xFE3F) /* -449 */, accX);
            Assert.AreEqual((short)0x0033 /* 51 */, accY);
            Assert.AreEqual((short)0x0FFF /* 4095 */, accZ);
            Assert.AreEqual((short)0x0001, gyrX);
            Assert.AreEqual((short)0x0006, gyrY);
            Assert.AreEqual((short)0x0004, gyrZ);

            EightBitDoDInputDevice.DecodeImu(ImuMovingSample(),
                out accX, out accY, out accZ, out gyrX, out gyrY, out gyrZ);

            Assert.AreEqual(unchecked((short)0xF6CA) /* -2358 */, accX);
            Assert.AreEqual((short)0x024B /* 587 */, accY);
            Assert.AreEqual((short)0x0E8C /* 3724 */, accZ);
            Assert.AreEqual(unchecked((short)0xFEC8) /* -312 */, gyrX);
            Assert.AreEqual((short)0x0216 /* 534 */, gyrY);
            Assert.AreEqual(unchecked((short)0xFFCE) /* -50 */, gyrZ);
        }
    }
}
