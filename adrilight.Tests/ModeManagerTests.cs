using System;
using System.Collections.Generic;
using adrilight.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace adrilight.Tests
{
    [TestClass]
    public class ModeManagerTests
    {
        private (ModeManager manager, Mock<IUserSettings> settings) Build(
            bool transferActive = true,
            IEnumerable<ILightingMode> lightingModes = null)
        {
            var mock = new Mock<IUserSettings>();
            mock.SetupProperty(s => s.TransferActive, transferActive);
            var manager = new ModeManager(mock.Object, lightingModes ?? Array.Empty<ILightingMode>());
            return (manager, mock);
        }

        // ── Inhibitor: single source ──────────────────────────────────────────────

        [TestMethod]
        public void AddInhibitor_FirstInhibitor_ForcesTransferActiveFalse()
        {
            var (manager, settings) = Build(transferActive: true);

            manager.AddInhibitor("sleep");

            Assert.IsFalse(settings.Object.TransferActive, "First inhibitor should force TransferActive off");
            Assert.IsTrue(manager.IsInhibited);
        }

        [TestMethod]
        public void RemoveInhibitor_LastInhibitor_RestoresTransferActiveTrue()
        {
            var (manager, settings) = Build(transferActive: true);

            manager.AddInhibitor("sleep");
            manager.RemoveInhibitor("sleep");

            Assert.IsTrue(settings.Object.TransferActive, "Removing last inhibitor should restore TransferActive");
            Assert.IsFalse(manager.IsInhibited);
        }

        [TestMethod]
        public void RemoveInhibitor_WhenUserWantsOff_DoesNotTurnOn()
        {
            // If TransferActive was already false before sleep, waking should not turn LEDs on.
            var (manager, settings) = Build(transferActive: false);

            manager.AddInhibitor("sleep");
            manager.RemoveInhibitor("sleep");

            Assert.IsFalse(settings.Object.TransferActive,
                "TransferActive should remain false if it was off before the inhibitor was added");
        }

        // ── Inhibitor: multiple sources ───────────────────────────────────────────

        [TestMethod]
        public void AddInhibitor_SecondInhibitor_DoesNotChangeTransferActiveAgain()
        {
            var (manager, settings) = Build(transferActive: true);

            manager.AddInhibitor("lock");
            manager.AddInhibitor("screensaver");

            Assert.IsFalse(settings.Object.TransferActive);
            Assert.IsTrue(manager.IsInhibited);
        }

        [TestMethod]
        public void RemoveInhibitor_WithRemainingInhibitors_KeepsTransferActiveFalse()
        {
            var (manager, settings) = Build(transferActive: true);

            manager.AddInhibitor("lock");
            manager.AddInhibitor("screensaver");
            manager.RemoveInhibitor("screensaver"); // "lock" still active

            Assert.IsFalse(settings.Object.TransferActive,
                "TransferActive must stay off while any inhibitor remains");
            Assert.IsTrue(manager.IsInhibited);
        }

        // ── THE BUG TEST: screen saver activating while session is locked ─────────
        //
        // v3.4.2 bug: both SessionLock and ScreenSaver wrote to the same
        // _transferActiveBeforeLock field. If the screen saver fired while the session
        // was locked (which Windows does on the lock screen after a timeout), the screen
        // saver handler would overwrite the saved "true" with "false". When both events
        // cleared, LEDs would never turn back on.
        //
        // v3.5.0 fix: each inhibitor source is tracked independently. The saved user
        // intent is captured once (when the first inhibitor fires) and not overwritten
        // by subsequent inhibitors.

        [TestMethod]
        public void ScreenSaverWhileLocked_TransferActiveRestoredAfterBoth()
        {
            var (manager, settings) = Build(transferActive: true);

            // Step 1 — session locks (LEDs were on)
            manager.AddInhibitor("lock");
            Assert.IsFalse(settings.Object.TransferActive, "Should be off after lock");

            // Step 2 — screen saver activates while session is still locked
            manager.AddInhibitor("screensaver");
            Assert.IsFalse(settings.Object.TransferActive, "Should stay off with both inhibitors");

            // Step 3 — screen saver ends (session still locked)
            manager.RemoveInhibitor("screensaver");
            Assert.IsFalse(settings.Object.TransferActive, "Should stay off: session still locked");

            // Step 4 — session unlocks — this is where the old code would leave LEDs off
            manager.RemoveInhibitor("lock");
            Assert.IsTrue(settings.Object.TransferActive,
                "Should be restored to true after all inhibitors clear — this was the v3.4.2 bug");
        }

        // ── IsOutputActive ────────────────────────────────────────────────────────

        [TestMethod]
        public void IsOutputActive_WhenNotInhibited_ReflectsUserIntent()
        {
            var (manager, _) = Build(transferActive: true);
            Assert.IsTrue(manager.IsOutputActive);

            var (managerOff, _) = Build(transferActive: false);
            Assert.IsFalse(managerOff.IsOutputActive);
        }

        [TestMethod]
        public void IsOutputActive_WhenInhibited_ReturnsFalse()
        {
            var (manager, _) = Build(transferActive: true);
            manager.AddInhibitor("sleep");
            Assert.IsFalse(manager.IsOutputActive);
        }

        // ── Mode switching ────────────────────────────────────────────────────────

        [TestMethod]
        public void SetMode_ToNewMode_UpdatesActiveMode()
        {
            var (manager, _) = Build();
            Assert.AreEqual(LightingMode.ScreenCapture, manager.ActiveMode);

            manager.SetMode(LightingMode.SoundToLight);

            Assert.AreEqual(LightingMode.SoundToLight, manager.ActiveMode);
        }

        [TestMethod]
        public void SetMode_SameMode_IsNoOp()
        {
            var (manager, settings) = Build(transferActive: true);

            // Calling SetMode with the current mode should not change anything
            manager.SetMode(LightingMode.ScreenCapture);

            Assert.AreEqual(LightingMode.ScreenCapture, manager.ActiveMode);
            Assert.IsTrue(settings.Object.TransferActive, "TransferActive must not change for a no-op SetMode");
        }

        // ── Pipeline Start/Stop wiring ────────────────────────────────────────────

        [TestMethod]
        public void SetMode_ToNewMode_StartsIncomingPipeline()
        {
            var pipeline = new Mock<ILightingMode>();
            pipeline.Setup(p => p.ModeId).Returns(LightingMode.SoundToLight);
            pipeline.Setup(p => p.IsRunning).Returns(false);

            var (manager, _) = Build(lightingModes: new[] { pipeline.Object });

            manager.SetMode(LightingMode.SoundToLight);

            pipeline.Verify(p => p.Start(), Times.Once,
                "SetMode should call Start() on the incoming pipeline");
        }

        [TestMethod]
        public void SetMode_FromRunningMode_StopsOutgoingPipeline()
        {
            var outgoing = new Mock<ILightingMode>();
            outgoing.Setup(p => p.ModeId).Returns(LightingMode.SoundToLight);
            outgoing.Setup(p => p.IsRunning).Returns(true);

            var incoming = new Mock<ILightingMode>();
            incoming.Setup(p => p.ModeId).Returns(LightingMode.GamerMode);
            incoming.Setup(p => p.IsRunning).Returns(false);

            var (manager, _) = Build(lightingModes: new[] { outgoing.Object, incoming.Object });

            // First put manager into SoundToLight, then switch to GamerMode
            manager.SetMode(LightingMode.SoundToLight);
            outgoing.Setup(p => p.IsRunning).Returns(true); // now it's "running"
            manager.SetMode(LightingMode.GamerMode);

            outgoing.Verify(p => p.Stop(), Times.Once,
                "SetMode should call Stop() on the outgoing running pipeline");
            incoming.Verify(p => p.Start(), Times.Once,
                "SetMode should call Start() on the incoming pipeline");
        }
    }
}
