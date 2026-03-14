// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
// using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class RhythmEvaluator
    {
        private const double jerk_balancing_factor = 0.040; // Increase this value to make things more based
        private const double jerk_time_constant = 400.0; // 400ms
        private const double resonance_factor = 1.0; // Keep neutral, but worth tuning perhaps?
        private const double compression_exponent = 0.86; // Reflects the nonlinear perception of effort, more relevant at higher BPMs

        // Unused for now since I can't seem to find use for these, maybe some other time?
        // private const double min_speed_bonus = 200; // Should be identical to the value in SpeedEvaluator for now
        // private const double tc_exponent = 0.5;

        /// <summary>
        /// Calculates a rhythm multiplier for the difficulty of the tap associated with historic rhythmic interval data of the current <see cref="OsuDifficultyHitObject"/>.
        /// Additionally, uses jerk (derivative of acceleration/force) to model muscle confusion, which is defined here as the derivative of Speed values for the object.
        /// Note that this specifically does NOT model cognitive difficulty and is a very "1st-order" measurement of physical finger control.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = (OsuDifficultyHitObject?)current.Previous(0);

            // double strainTime = osuCurrObj.AdjustedDeltaTime;
            double nextDoubletapness = osuCurrObj.GetDoubletapness((OsuDifficultyHitObject?)osuCurrObj.Next(0));
            double prevDoubletapness = osuPrevObj?.GetDoubletapness(osuCurrObj) ?? 0;
            double doubletapness = 1.0 - Math.Max(nextDoubletapness, prevDoubletapness); // This is done because for doubletaps, extreme jerk appears BEFORE the note with high doubletapness
            double epsilon = current.HitWindow(HitResult.Great) * 0.4;

            // Copied strain time code from SpeedEvaluator
            // strainTime /= Math.Clamp((strainTime / osuCurrObj.HitWindow(HitResult.Great)) / 0.93, 0.92, 1);

            // Dynamically vary the time constant / decay speed based on BPM, we don't want to excessively buff finger control at high end where raw speed typically dominates
            // This partially counteracts the "double-counting of jerk / muscle confusion"
            // tc_exponent makes this anti double-counting harsher or softer
            // double scaledTimeConstant = jerk_time_constant * Math.Pow(strainTime / DifficultyCalculationUtils.BPMToMilliseconds(min_speed_bonus), tc_exponent);
            osuCurrObj.History.Decay(osuCurrObj.AdjustedDeltaTime, jerk_time_constant);

            // Do not calculate a jerk if there is no previous note
            if (osuPrevObj == null) return 0;

            // Calculate the jerk of the required motion based off of current and previous speed difficulty, scaled by DeltaTime

            double rawJerk = calculateJerk(osuCurrObj.History.BasePower, osuPrevObj.History.BasePower, osuCurrObj.AdjustedDeltaTime, epsilon, osuPrevObj.BaseObject is Slider);
            // Multiply by a resonance factor
            // Catch-all nerf for repetitive patterns which players can optimize, minimizing the jerk of the required motion
            double currentRatio = osuCurrObj.AdjustedDeltaTime / osuPrevObj.AdjustedDeltaTime;
            double resonanceMultiplier = resonanceSieve(osuCurrObj, currentRatio);
            double jerkDifficulty = rawJerk * resonanceMultiplier;

            // Add the jerk strain to the rhythm history
            osuCurrObj.History.JerkStrain += jerkDifficulty * doubletapness;

            // Calculate the current ratio and save both the ratio and the speed difficulty (power) to the rhythm history
            osuCurrObj.History.Push(currentRatio, osuCurrObj.History.BasePower);

            return (osuCurrObj.History.JerkStrain * jerk_balancing_factor) * doubletapness;
        }

        private static double calculateJerk(double currentPower, double prevPower, double dt, double epsilon, bool fromSlider)
        {
            // If the pattern is unambiguously doubletappable, assume its jerk to be 0
            if (dt < epsilon) return 0;

            double deltaPower = currentPower - prevPower;
            double compressedDelta = Math.Sign(deltaPower) * Math.Pow(Math.Abs(deltaPower), compression_exponent);
            double jerk = compressedDelta / ((dt + epsilon) / 1000.0); // Smooth with epsilon

            if (jerk < 0)
            {
                // Slowdowns are mildly harder to execute than speedups (subject to debate, I think)
                return Math.Abs(jerk) * 1.25;
            }

            if (fromSlider)
            {
                // If the last object was a slider, this is typically a much easier transition
                return Math.Abs(jerk) * 0.6;
            }

            return Math.Abs(jerk);
        }

        // Array containing the smallest prime factors of integers 0 through 17
        // Patterns can be thought of as having specific periods
        // ex. triples are period-4 (XXX-XXX-XXX-...), swing is period-3 (XX-XX-XX-XX-...)
        // This is used to nerf resonances that represent "less prime" note counts
        private static readonly int[] smallest_prime_factors =
        {
            0, // 0: N/A
            1, // 1: N/A
            2, // 2: Stream (p=1)
            3, // 3: Swing   (p=2)
            2, // 4: Triple  (p=3)
            5, // 5: Quad    (p=4)
            2, // 6: 5-Burst (p=5) -> SPF of 6 is 2
            7, // 7: 6-Burst (p=6)
            2, // 8: 7-Burst (p=7) -> SPF of 8 is 2
            3, // 9: etc.
            2, // 10
            11, // 11
            2, // 12
            13, // 13
            2, // 14
            3, // 15
            2, // 16
            17 // 17
        };

        // Function that calculates how resonant a pattern is by penalizing same rhythmic intervals occurring at smaller prime note intervals
        // e.g. since triples are period-4 (smallest prime factor is 2), they will likely be nerfed more than weirder periods (e.g. 1/7)
        private static double resonanceSieve(OsuDifficultyHitObject current, double currentRatio)
        {
            const double ratio_tolerance = 0.09; // How close should two rhythmic intervals be in order to be considered "the same"
            const double gallop_midpoint = 37.0; // 37ms - arbitrarily chosen midpoint representing the time between two notes in a gallop

            double totalResonance = 0;

            // Smooth gallop factor
            double gallopFactor = 1.0 / (1.0 + Math.Exp(0.25 * (current.AdjustedDeltaTime - gallop_midpoint)));

            // For each integer from 1 to 16
            for (int p = 1; p <= 16; p++)
            {
                double historicalRatio = current.History.GetRatio(p); // Look at the rhythmic interval from p notes ago
                int period = p + 1; // Analyze the period corresponding to p+1, meaning that if we're looking 3 notes ago / a triple, this is period-(3+1)

                // Nerf "smaller prime" resonances more
                double resonanceDepth = 1.0 / Math.Pow(smallest_prime_factors[Math.Min(period, 17)], resonance_factor); // I'm not sure what resonance_factor even does

                // Standard rhythmic match
                bool isRhythmicMatch = Math.Abs(currentRatio - historicalRatio) < currentRatio * ratio_tolerance;

                // Gallop match - nerf even harder
                bool isGallopMatch = gallopFactor > 0.1 && p < 4;
                if (!isRhythmicMatch && !isGallopMatch) continue;

                // Use the gallop factor from before to blend the nerf: gallops above a certain note 1 to note 2 duration remain difficult
                double multiplier = isGallopMatch ? (Math.Pow(gallop_midpoint / current.AdjustedDeltaTime, 2) * gallopFactor * 4) : 1.0;
                totalResonance += resonanceDepth * multiplier;
            }

            // Map resonance density to a multiplier - higher resonance implies lower jerk since players can play resonant patterns more efficiently
            return 1.0 / (1.0 + totalResonance);
        }
    }
}
