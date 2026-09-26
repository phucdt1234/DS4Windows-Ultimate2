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

namespace DS4WinWPF.DS4Control
{
    /// <summary>
    /// Per-profile "Special Aim / Assist" configuration for one analog stick.
    /// Mirrors the stages of DS4Windows.StickProcessing.StickProcessingPipeline;
    /// every stage is independently bypassable through its enable flag.
    /// </summary>
    public class SpecialAimStickSettings
    {
        public const bool DEFAULT_ENABLED = false;
        public const double DEFAULT_DEADZONE_INNER = 0.0;
        public const double DEFAULT_DEADZONE_OUTER = 0.0;
        public const bool DEFAULT_CURVE_ENABLED = false;
        public const int DEFAULT_CURVE_TYPE = 0; // Linear
        public const double DEFAULT_CURVE_POWER = 1.0;
        public const double DEFAULT_CURVE_P1X = 0.33, DEFAULT_CURVE_P1Y = 0.33;
        public const double DEFAULT_CURVE_P2X = 0.67, DEFAULT_CURVE_P2Y = 0.67;
        public const bool DEFAULT_FILTER_ENABLED = false;
        public const double DEFAULT_FILTER_ALPHA = 0.5;
        public const bool DEFAULT_JITTER_ENABLED = false;
        public const double DEFAULT_JITTER_RADIUS = 0.01;
        public const double DEFAULT_JITTER_FREQUENCY = 20.0;
        public const double DEFAULT_JITTER_THRESHOLD = 1.0;

        public bool enabled = DEFAULT_ENABLED;
        public double deadzoneInner = DEFAULT_DEADZONE_INNER;
        public double deadzoneOuter = DEFAULT_DEADZONE_OUTER;
        public bool curveEnabled = DEFAULT_CURVE_ENABLED;
        /// <summary>0 = Linear, 1 = Power, 2 = Bezier, 3 = S-Curve</summary>
        public int curveType = DEFAULT_CURVE_TYPE;
        public double curvePower = DEFAULT_CURVE_POWER;
        public double curveP1X = DEFAULT_CURVE_P1X;
        public double curveP1Y = DEFAULT_CURVE_P1Y;
        public double curveP2X = DEFAULT_CURVE_P2X;
        public double curveP2Y = DEFAULT_CURVE_P2Y;
        public bool filterEnabled = DEFAULT_FILTER_ENABLED;
        public double filterAlpha = DEFAULT_FILTER_ALPHA;
        public bool jitterEnabled = DEFAULT_JITTER_ENABLED;
        public double jitterRadius = DEFAULT_JITTER_RADIUS;
        public double jitterFrequency = DEFAULT_JITTER_FREQUENCY;
        public double jitterThreshold = DEFAULT_JITTER_THRESHOLD;

        public void Reset()
        {
            enabled = DEFAULT_ENABLED;
            deadzoneInner = DEFAULT_DEADZONE_INNER;
            deadzoneOuter = DEFAULT_DEADZONE_OUTER;
            curveEnabled = DEFAULT_CURVE_ENABLED;
            curveType = DEFAULT_CURVE_TYPE;
            curvePower = DEFAULT_CURVE_POWER;
            curveP1X = DEFAULT_CURVE_P1X; curveP1Y = DEFAULT_CURVE_P1Y;
            curveP2X = DEFAULT_CURVE_P2X; curveP2Y = DEFAULT_CURVE_P2Y;
            filterEnabled = DEFAULT_FILTER_ENABLED;
            filterAlpha = DEFAULT_FILTER_ALPHA;
            jitterEnabled = DEFAULT_JITTER_ENABLED;
            jitterRadius = DEFAULT_JITTER_RADIUS;
            jitterFrequency = DEFAULT_JITTER_FREQUENCY;
            jitterThreshold = DEFAULT_JITTER_THRESHOLD;
        }
    }

    /// <summary>Per-profile recoil compensation configuration (applied to the right stick).</summary>
    public class SpecialAimRecoilSettings
    {
        public const bool DEFAULT_ENABLED = false;
        public const double DEFAULT_COMPENSATION_X = 0.0;
        public const double DEFAULT_COMPENSATION_Y = 0.0;
        public const double DEFAULT_PULL_STRENGTH = 1.0;
        public const double DEFAULT_PULL_RATE = 2.0;
        public const double DEFAULT_TRIGGER_THRESHOLD = 0.3;
        public const int DEFAULT_TRIGGER_SOURCE = 1; // RightTrigger

        public bool enabled = DEFAULT_ENABLED;
        /// <summary>Horizontal compensation target (-1..1, negative pulls left).</summary>
        public double compensationX = DEFAULT_COMPENSATION_X;
        /// <summary>Vertical compensation target (-1..1, negative pulls down).</summary>
        public double compensationY = DEFAULT_COMPENSATION_Y;
        /// <summary>Overall strength multiplier (0..1).</summary>
        public double pullStrength = DEFAULT_PULL_STRENGTH;
        /// <summary>Ramp rate in normalized units per second.</summary>
        public double pullRate = DEFAULT_PULL_RATE;
        /// <summary>Trigger value (0..1) above which compensation activates.</summary>
        public double triggerThreshold = DEFAULT_TRIGGER_THRESHOLD;
        /// <summary>0 = LeftTrigger, 1 = RightTrigger, 2 = AnyTrigger</summary>
        public int triggerSource = DEFAULT_TRIGGER_SOURCE;

        public void Reset()
        {
            enabled = DEFAULT_ENABLED;
            compensationX = DEFAULT_COMPENSATION_X;
            compensationY = DEFAULT_COMPENSATION_Y;
            pullStrength = DEFAULT_PULL_STRENGTH;
            pullRate = DEFAULT_PULL_RATE;
            triggerThreshold = DEFAULT_TRIGGER_THRESHOLD;
            triggerSource = DEFAULT_TRIGGER_SOURCE;
        }
    }

    /// <summary>
    /// Master "Special Aim / Assist" group persisted per profile slot.
    /// </summary>
    public class SpecialAimSettings
    {
        public const bool DEFAULT_ENABLED = false;

        public bool enabled = DEFAULT_ENABLED;
        public SpecialAimStickSettings leftStick = new SpecialAimStickSettings();
        public SpecialAimStickSettings rightStick = new SpecialAimStickSettings();
        public SpecialAimRecoilSettings recoil = new SpecialAimRecoilSettings();

        public void Reset()
        {
            enabled = DEFAULT_ENABLED;
            leftStick.Reset();
            rightStick.Reset();
            recoil.Reset();
        }
    }
}
