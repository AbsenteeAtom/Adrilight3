using adrilight.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace adrilight.Tests
{
    [TestClass]
    public class NightLightDetectionTests
    {
        // Creates a 19-byte array with the specified value at the ON/OFF indicator byte (index 18)
        private static byte[] MakeData(byte byte18)
        {
            var data = new byte[19];
            data[18] = byte18;
            return data;
        }

        [TestMethod]
        public void ParseRegistryData_Byte18_0x15_ReturnsOn()
        {
            // 0x15: ON indicator observed on some Windows builds
            Assert.AreEqual(NightLightState.On, NightLightDetection.ParseRegistryData(MakeData(0x15)));
        }

        [TestMethod]
        public void ParseRegistryData_Byte18_0x12_ReturnsOn()
        {
            // 0x12: ON indicator observed on other Windows builds (same Bond field, different base value)
            Assert.AreEqual(NightLightState.On, NightLightDetection.ParseRegistryData(MakeData(0x12)));
        }

        [TestMethod]
        public void ParseRegistryData_Byte18_0x13_ReturnsOff()
        {
            Assert.AreEqual(NightLightState.Off, NightLightDetection.ParseRegistryData(MakeData(0x13)));
        }

        [TestMethod]
        public void ParseRegistryData_Byte18_0x10_ReturnsOff()
        {
            Assert.AreEqual(NightLightState.Off, NightLightDetection.ParseRegistryData(MakeData(0x10)));
        }

        [TestMethod]
        public void ParseRegistryData_NullData_ReturnsUnknown()
        {
            Assert.AreEqual(NightLightState.Unknown, NightLightDetection.ParseRegistryData(null));
        }

        [TestMethod]
        public void ParseRegistryData_UnknownByte_ReturnsOff()
        {
            // Any byte other than the known ON values (0x15, 0x12) is treated as OFF
            Assert.AreEqual(NightLightState.Off, NightLightDetection.ParseRegistryData(MakeData(0xFF)));
        }

        [TestMethod]
        public void ParseRegistryData_DataTooShort_ReturnsUnknown()
        {
            Assert.AreEqual(NightLightState.Unknown, NightLightDetection.ParseRegistryData(new byte[10]));
        }
    }
}
