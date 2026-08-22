using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class PreferencesTests
    {
        private NoodleStyle _style;
        private float _thickness;
        private float _snap;

        [SetUp]
        public void SetUp()
        {
            _style = SaintsGraphPreferences.NoodleStyle;
            _thickness = SaintsGraphPreferences.NoodleThickness;
            _snap = SaintsGraphPreferences.GridSnap;
        }

        [TearDown]
        public void TearDown()
        {
            SaintsGraphPreferences.NoodleStyle = _style;
            SaintsGraphPreferences.NoodleThickness = _thickness;
            SaintsGraphPreferences.GridSnap = _snap;
        }

        [Test]
        public void Changing_A_Preference_Notifies_Once()
        {
            SaintsGraphPreferences.NoodleStyle = NoodleStyle.Curvy;

            int notifications = 0;
            void Handler() => notifications++;

            SaintsGraphPreferences.Changed += Handler;
            try
            {
                SaintsGraphPreferences.NoodleStyle = NoodleStyle.Angled;
                Assert.AreEqual(1, notifications, "open windows are told about the change");

                SaintsGraphPreferences.NoodleStyle = NoodleStyle.Angled;
                Assert.AreEqual(1, notifications, "setting the same value again is not a change");
            }
            finally
            {
                SaintsGraphPreferences.Changed -= Handler;
            }

            Assert.AreEqual(NoodleStyle.Angled, SaintsGraphPreferences.NoodleStyle, "the value persists");
        }

        [Test]
        public void Thickness_Is_Clamped_To_A_Usable_Range()
        {
            SaintsGraphPreferences.NoodleThickness = 100f;
            Assert.AreEqual(10f, SaintsGraphPreferences.NoodleThickness);

            SaintsGraphPreferences.NoodleThickness = -5f;
            Assert.AreEqual(1f, SaintsGraphPreferences.NoodleThickness);
        }

        [Test]
        public void Snap_Rounds_Positions_And_Is_Off_By_Default_Value_Zero()
        {
            SaintsGraphPreferences.GridSnap = 0f;
            Assert.AreEqual(new Vector2(13f, 27f), SaintsGraphPreferences.Snap(new Vector2(13f, 27f)),
                "zero means no snapping at all");

            SaintsGraphPreferences.GridSnap = 20f;
            Assert.AreEqual(new Vector2(20f, 40f), SaintsGraphPreferences.Snap(new Vector2(23f, 34f)));
            Assert.AreEqual(new Vector2(0f, -20f), SaintsGraphPreferences.Snap(new Vector2(-7f, -14f)),
                "snapping works either side of the origin");
        }
    }
}
