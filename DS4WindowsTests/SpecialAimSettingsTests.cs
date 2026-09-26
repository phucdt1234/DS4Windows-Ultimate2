using System.Xml;
using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF.DS4Control;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests
{
    [TestClass]
    public class SpecialAimSettingsTests
    {
        private static BackingStore MakeCustomStore()
        {
            BackingStore store = new BackingStore();
            SpecialAimSettings sa = store.specialAimSettings[0];
            sa.enabled = true;
            sa.leftStick.enabled = true;
            sa.leftStick.deadzoneInner = 0.1;
            sa.leftStick.deadzoneOuter = 0.9;
            sa.leftStick.curveEnabled = true;
            sa.leftStick.curveType = 2; // Bezier
            sa.leftStick.curvePower = 2.5;
            sa.leftStick.curveP1X = 0.4; sa.leftStick.curveP1Y = 0.2;
            sa.leftStick.curveP2X = 0.6; sa.leftStick.curveP2Y = 0.8;
            sa.leftStick.filterEnabled = true;
            sa.leftStick.filterAlpha = 0.35;
            sa.leftStick.jitterEnabled = true;
            sa.leftStick.jitterRadius = 0.02;
            sa.leftStick.jitterFrequency = 60;
            sa.leftStick.jitterThreshold = 0.08;

            sa.rightStick.enabled = true;
            sa.rightStick.deadzoneInner = 0.05;
            sa.rightStick.filterEnabled = true;
            sa.rightStick.filterAlpha = 0.6;

            sa.recoil.enabled = true;
            sa.recoil.compensationX = -0.1;
            sa.recoil.compensationY = -0.5;
            sa.recoil.pullStrength = 0.8;
            sa.recoil.pullRate = 3.5;
            sa.recoil.triggerThreshold = 0.4;
            sa.recoil.triggerSource = 2;
            return store;
        }

        private static string SerializeStore(BackingStore store)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
            using Utf8StringWriter strWriter = new Utf8StringWriter();
            using XmlWriter xmlWriter = XmlWriter.Create(strWriter,
                new XmlWriterSettings() { Encoding = System.Text.Encoding.UTF8, Indent = true, OmitXmlDeclaration = true });
            ProfileDTO dto = new ProfileDTO();
            dto.DeviceIndex = 0;
            dto.MapFrom(store);
            serializer.Serialize(xmlWriter, dto, new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty }));
            xmlWriter.Flush();
            xmlWriter.Close();
            return strWriter.ToString();
        }

        [TestMethod]
        public void SpecialAimSettings_RoundTripsThroughProfileXml()
        {
            BackingStore original = MakeCustomStore();
            string xml = SerializeStore(original);

            Assert.IsTrue(xml.Contains("<SpecialAimSettings>"), "serialized XML must contain the SpecialAimSettings group");

            XmlSerializer serializer = new XmlSerializer(typeof(ProfileDTO), ProfileDTO.GetAttributeOverrides());
            using StringReader sr = new StringReader(xml);
            BackingStore loaded = new BackingStore();
            ProfileDTO dto = serializer.Deserialize(sr) as ProfileDTO;
            dto.DeviceIndex = 0;
            dto.MapTo(loaded);

            SpecialAimSettings sa = loaded.specialAimSettings[0];
            Assert.IsTrue(sa.enabled);
            Assert.IsTrue(sa.leftStick.enabled);
            Assert.AreEqual(0.1, sa.leftStick.deadzoneInner, 1e-9);
            Assert.AreEqual(0.9, sa.leftStick.deadzoneOuter, 1e-9);
            Assert.IsTrue(sa.leftStick.curveEnabled);
            Assert.AreEqual(2, sa.leftStick.curveType);
            Assert.AreEqual(2.5, sa.leftStick.curvePower, 1e-9);
            Assert.AreEqual(0.4, sa.leftStick.curveP1X, 1e-9);
            Assert.AreEqual(0.2, sa.leftStick.curveP1Y, 1e-9);
            Assert.AreEqual(0.6, sa.leftStick.curveP2X, 1e-9);
            Assert.AreEqual(0.8, sa.leftStick.curveP2Y, 1e-9);
            Assert.IsTrue(sa.leftStick.filterEnabled);
            Assert.AreEqual(0.35, sa.leftStick.filterAlpha, 1e-9);
            Assert.IsTrue(sa.leftStick.jitterEnabled);
            Assert.AreEqual(0.02, sa.leftStick.jitterRadius, 1e-9);
            Assert.AreEqual(60, sa.leftStick.jitterFrequency, 1e-9);
            Assert.AreEqual(0.08, sa.leftStick.jitterThreshold, 1e-9);

            Assert.IsTrue(sa.rightStick.enabled);
            Assert.AreEqual(0.05, sa.rightStick.deadzoneInner, 1e-9);
            Assert.AreEqual(0.6, sa.rightStick.filterAlpha, 1e-9);

            Assert.IsTrue(sa.recoil.enabled);
            Assert.AreEqual(-0.1, sa.recoil.compensationX, 1e-9);
            Assert.AreEqual(-0.5, sa.recoil.compensationY, 1e-9);
            Assert.AreEqual(0.8, sa.recoil.pullStrength, 1e-9);
            Assert.AreEqual(3.5, sa.recoil.pullRate, 1e-9);
            Assert.AreEqual(0.4, sa.recoil.triggerThreshold, 1e-9);
            Assert.AreEqual(2, sa.recoil.triggerSource);
        }

        [TestMethod]
        public void SpecialAimSettings_DisabledByDefault()
        {
            BackingStore store = new BackingStore();
            for (int i = 0; i < store.specialAimSettings.Length; i++)
            {
                Assert.IsFalse(store.specialAimSettings[i].enabled, $"slot {i} must default to disabled");
                Assert.IsFalse(store.specialAimSettings[i].leftStick.enabled);
                Assert.IsFalse(store.specialAimSettings[i].rightStick.enabled);
                Assert.IsFalse(store.specialAimSettings[i].recoil.enabled);
            }
        }
    }
}
