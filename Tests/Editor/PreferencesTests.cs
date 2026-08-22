using NUnit.Framework;
using SaintsGraph.Editor;
using UnityEngine;

namespace SaintsGraph.Tests
{
    public class PreferencesTests
    {
        private NoodleStyle _style;
        private int _snapCells;

        [SetUp]
        public void SetUp()
        {
            _style = SaintsGraphPreferences.NoodleStyle;
            _snapCells = SaintsGraphPreferences.SnapCells;
        }

        [TearDown]
        public void TearDown()
        {
            SaintsGraphPreferences.NoodleStyle = _style;
            SaintsGraphPreferences.SnapCells = _snapCells;
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
        public void Snap_Is_Measured_In_Cells_Of_The_Drawn_Grid()
        {
            SaintsGraphPreferences.SnapCells = 0;
            Assert.AreEqual(new Vector2(13f, 27f), SaintsGraphPreferences.Snap(new Vector2(13f, 27f)),
                "zero means no snapping at all");

            SaintsGraphPreferences.SnapCells = 1;
            float cell = SaintsGraphPreferences.GridCell;
            Assert.AreEqual(new Vector2(cell, 2f * cell),
                SaintsGraphPreferences.Snap(new Vector2(cell * 1.15f, cell * 1.7f)));
            Assert.AreEqual(new Vector2(0f, -cell),
                SaintsGraphPreferences.Snap(new Vector2(-cell * 0.35f, -cell * 0.7f)),
                "snapping works either side of the origin");

            SaintsGraphPreferences.SnapCells = 2;
            Assert.AreEqual(new Vector2(2f * cell, 0f), SaintsGraphPreferences.Snap(new Vector2(cell * 1.6f, cell * 0.4f)),
                "more cells means a coarser step");
        }
    }
}
