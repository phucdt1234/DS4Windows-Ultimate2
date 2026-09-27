using DS4Windows.StickProcessing;

namespace DS4WindowsTests
{
    [TestClass]
    public partial class StickProcessingPipelineTests
    {
        private const double EPS = 1e-6;

        private static (double x, double y) Run(double x, double y, StickProcessingSettings s,
            StickProcessingState state = null, double dt = 0.001, double trigger = 0.0)
        {
            state ??= new StickProcessingState();
            StickProcessingPipeline.Process(ref x, ref y, s, state, dt, trigger);
            return (x, y);
        }

        private static StickProcessingSettings OnlyDeadzone(double inner, double outer = 0)
        {
            return new StickProcessingSettings
            {
                Enabled = true,
                Deadzone = new RadialDeadzoneSettings { Enabled = true, InnerRadius = inner, OuterRadius = outer },
            };
        }

        [TestMethod]
        public void DisabledPipeline_PassesThrough()
        {
            var (x, y) = Run(0.5, -0.25, StickProcessingSettings.Disabled());
            Assert.AreEqual(0.5, x, EPS);
            Assert.AreEqual(-0.25, y, EPS);
        }

        [TestMethod]
        public void RadialDeadzone_ZeroesInsideInnerRadius()
        {
            var (x, y) = Run(0.05, 0.05, OnlyDeadzone(0.2));
            Assert.AreEqual(0.0, x, EPS);
            Assert.AreEqual(0.0, y, EPS);
        }

        [TestMethod]
        public void RadialDeadzone_PreservesDirectionAboveInnerRadius()
        {
            // 45 degree diagonal, magnitude 0.7071: remapped to (0.7071-0.1)/0.9,
            // direction preserved.
            var (x, y) = Run(0.5, 0.5, OnlyDeadzone(0.1));
            double expectedDiagonal = (Math.Sqrt(0.5) - 0.1) / 0.9 / Math.Sqrt(2.0);
            Assert.AreEqual(expectedDiagonal, x, 1e-3);
            Assert.AreEqual(expectedDiagonal, y, 1e-3);

            // Axis-aligned sample keeps its axis (no Y bleed from a bad X/Y independent deadzone)
            var (x2, y2) = Run(0.5, 0.0, OnlyDeadzone(0.1));
            Assert.AreEqual(0.0, y2, EPS);
            Assert.AreEqual((0.5 - 0.1) / 0.9, x2, 1e-3);
        }

        [TestMethod]
        public void RadialDeadzone_SaturatesAtOuterRadius()
        {
            var (x, y) = Run(1.0, 0.0, OnlyDeadzone(0.1, 0.5));
            Assert.AreEqual(1.0, x, EPS);
            Assert.AreEqual(0.0, y, EPS);

            var (x2, y2) = Run(0.5, 0.0, OnlyDeadzone(0.1, 0.5));
            Assert.AreEqual(1.0, x2, EPS);
        }

        [TestMethod]
        public void PowerCurve_ReducesCenterSensitivity()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Curve = new ResponseCurveSettings { Enabled = true, CurveType = ResponseCurveType.Power, PowerExponent = 2.0 },
            };

            var (x, y) = Run(0.5, 0.0, s);
            Assert.AreEqual(0.25, x, 1e-4);
            Assert.AreEqual(0.0, y, EPS);

            // Full deflection stays full
            var (x2, _) = Run(1.0, 0.0, s);
            Assert.AreEqual(1.0, x2, EPS);
        }

        [TestMethod]
        public void SCurve_FixedEndpointsAndCenterBoost()
        {
            Assert.AreEqual(0.0, StickProcessingPipeline.EvalSCurve(0.0), EPS);
            Assert.AreEqual(1.0, StickProcessingPipeline.EvalSCurve(1.0), EPS);
            Assert.AreEqual(0.5, StickProcessingPipeline.EvalSCurve(0.5), EPS);
            // Smoothstep is steeper than linear at the midpoint
            Assert.AreEqual(0.53, StickProcessingPipeline.EvalSCurve(0.52), 0.02);
        }

        [TestMethod]
        public void BezierCurve_PassesThroughCornersAndMatchesLinearDiagonal()
        {
            double y0 = StickProcessingPipeline.EvalBezierCurve(0.0, 0.33, 0.33, 0.67, 0.67);
            double y1 = StickProcessingPipeline.EvalBezierCurve(1.0, 0.33, 0.33, 0.67, 0.67);
            Assert.AreEqual(0.0, y0, EPS);
            Assert.AreEqual(1.0, y1, EPS);

            // Control points on the diagonal reproduce a linear response
            double yLin = StickProcessingPipeline.EvalBezierCurve(0.37, 0.3333333, 0.3333333, 0.6666667, 0.6666667);
            Assert.AreEqual(0.37, yLin, 1e-3);

            // Aggressive curve: y stays below x for a power-like response
            double yAggr = StickProcessingPipeline.EvalBezierCurve(0.5, 0.6, 0.1, 0.9, 0.4);
            Assert.IsTrue(yAggr < 0.5);
        }

        [TestMethod]
        public void RcFilter_ConvergesTowardInput()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Filter = new RcFilterSettings { Enabled = true, Alpha = 0.5 },
            };
            var state = new StickProcessingState();

            double x = 1.0, y = 0.0;
            for (int i = 0; i < 100; i++)
            {
                (x, y) = Run(x, y, s, state);
            }

            Assert.AreEqual(1.0, x, 0.01);
        }

        [TestMethod]
        public void RcFilter_SmallerAlphaIsSmootherAndSlower()
        {
            StickProcessingState fast = new StickProcessingState();
            StickProcessingState slow = new StickProcessingState();
            var fastSettings = new StickProcessingSettings { Enabled = true, Filter = new RcFilterSettings { Enabled = true, Alpha = 0.9 } };
            var slowSettings = new StickProcessingSettings { Enabled = true, Filter = new RcFilterSettings { Enabled = true, Alpha = 0.1 } };

            // First call initializes the filter memory, so feed a changing
            // input: init with center, then a full deflection sample.
            double x = 0.0, y = 0.0;
            (x, y) = Run(x, y, fastSettings, fast);
            (x, y) = Run(1.0, 0.0, fastSettings, fast);
            double fastVal = x;

            x = 0.0; y = 0.0;
            (x, y) = Run(x, y, slowSettings, slow);
            (x, y) = Run(1.0, 0.0, slowSettings, slow);
            double slowVal = x;

            Assert.AreEqual(0.9, fastVal, 0.01);
            Assert.AreEqual(0.1, slowVal, 0.01);
            Assert.IsTrue(slowVal < fastVal);
        }

        [TestMethod]
        public void RotationalJitter_InjectsCircleOnlyNearNeutral()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Jitter = new RotationalJitterSettings { Enabled = true, Radius = 0.05, FrequencyHz = 100.0, ActivationThreshold = 0.1 },
            };
            var state = new StickProcessingState();

            var (x, y) = Run(0.0, 0.0, s, state, dt: 0.0025); // quarter period at 100 Hz
            double mag = Math.Sqrt(x * x + y * y);
            Assert.AreEqual(0.05, mag, 1e-3);

            // Above the activation threshold: no injection
            var state2 = new StickProcessingState();
            var (x2, y2) = Run(0.5, 0.0, s, state2, dt: 0.0025);
            Assert.AreEqual(0.5, x2, EPS);
            Assert.AreEqual(0.0, y2, EPS);
        }

        [TestMethod]
        public void RecoilCompensation_RampsWhileTriggerHeldAndDecaysAfterRelease()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Recoil = new RecoilCompensationSettings
                {
                    Enabled = true,
                    CompensationY = -1.0, // pulls down (against recoil rise)
                    PullStrength = 1.0,
                    PullRate = 1.0,       // 1.0 per second
                    TriggerThreshold = 0.3,
                    Trigger = TriggerSource.RightTrigger,
                },
            };
            var state = new StickProcessingState();

            // Hold trigger for 0.5 s in 10 ms steps: offset should reach 0.5.
            // Each iteration feeds fresh neutral stick input; only the pipeline
            // state persists between samples.
            double x = 0.0, y = 0.0;
            for (int i = 0; i < 50; i++)
            {
                (x, y) = Run(0.0, 0.0, s, state, dt: 0.01, trigger: 1.0);
            }
            Assert.AreEqual(-0.5, y, 0.01);

            // Release: offset decays back to zero
            for (int i = 0; i < 60; i++)
            {
                (x, y) = Run(0.0, 0.0, s, state, dt: 0.01, trigger: 0.0);
            }
            Assert.AreEqual(0.0, y, 0.01);
        }

        [TestMethod]
        public void RecoilCompensation_IgnoresTriggerBelowThreshold()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Recoil = new RecoilCompensationSettings
                {
                    Enabled = true,
                    CompensationY = -1.0,
                    PullRate = 10.0,
                    TriggerThreshold = 0.3,
                },
            };
            var state = new StickProcessingState();

            double x = 0.0, y = 0.0;
            for (int i = 0; i < 20; i++)
            {
                (x, y) = Run(x, y, s, state, dt: 0.01, trigger: 0.2);
            }
            Assert.AreEqual(0.0, y, EPS);
        }

        [TestMethod]
        public void OutputIsClampedToUnitRange()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Jitter = new RotationalJitterSettings { Enabled = true, Radius = 0.5, ActivationThreshold = 1.0 },
            };

            var (x, y) = Run(0.9, 0.9, s, new StickProcessingState(), dt: 0.001);
            Assert.IsTrue(x <= 1.0 && y <= 1.0);

            Assert.AreEqual(255, StickProcessingPipeline.DenormalizeAxis(2.0));
            Assert.AreEqual(0, StickProcessingPipeline.DenormalizeAxis(-2.0));
            Assert.AreEqual(128, StickProcessingPipeline.DenormalizeAxis(0.0));
        }

        [TestMethod]
        public void NormalizeAxis_MapsByteRange()
        {
            Assert.AreEqual(0.0, StickProcessingPipeline.NormalizeAxis(128), 0.01);
            Assert.AreEqual(1.0, StickProcessingPipeline.NormalizeAxis(255), 0.01);
            Assert.AreEqual(-1.0, StickProcessingPipeline.NormalizeAxis(0), 0.01);
        }
    }
}

namespace DS4WindowsTests
{
    public partial class StickProcessingPipelineTests
    {
        [TestMethod]
        public void RotationalJitter_RemainsVisibleAtFullDeflection()
        {
            var s = new StickProcessingSettings
            {
                Enabled = true,
                Jitter = new RotationalJitterSettings { Enabled = true, Radius = 0.05, FrequencyHz = 50.0, ActivationThreshold = 1.0 },
            };
            var state = new StickProcessingState();

            double minX = 2.0, maxY = 0.0;
            double x = 1.0, y = 0.0;
            for (int i = 0; i < 40; i++)
            {
                (x, y) = Run(1.0, 0.0, s, state, dt: 0.005); // quarter period per step
                double mag = Math.Sqrt(x * x + y * y);
                Assert.IsTrue(mag <= 1.0 + 1e-9, $"output must stay inside the unit circle, got {mag}");
                minX = Math.Min(minX, x);
                maxY = Math.Max(maxY, y);
            }

            // The tip must slide along the rim: x dips below 1 and y rises above 0
            Assert.IsTrue(minX < 0.99, $"x should wobble below 1, min {minX}");
            Assert.IsTrue(maxY > 0.02, $"y should wobble above 0, max {maxY}");
        }
    }
}
