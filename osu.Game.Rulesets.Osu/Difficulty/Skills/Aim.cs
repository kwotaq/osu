// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : TimeSkill
    {
        public readonly bool IncludeSliders;

        public Aim(Mod[] mods, bool includeSliders)
            : base(mods)
        {
            IncludeSliders = includeSliders;
        }

        private double currentStrain;
        protected override double TimeThresholdMinutes => 50;

        private readonly List<double> sliderStrains = new List<double>();

        protected override double HitProbability(double skill, double difficulty)
        {
            if (difficulty <= 0) return 1;
            if (skill <= 0) return 0;

            double baseDeviation = difficulty / skill;
            // at what point does the player lose the ability to aim normally
            // increasing this will like high misscount scores more than ringtone maps, and vice versa
            const double limit_of_proportion = 1.2;
            // how quickly does the player lose the ability to aim normally at the limit of proportion
            // increasing this has a similar effect as increasing the limit of proportion, but it changes how significant the effect is across maps
            const double breakdown_rate = 5;
            double adjustedDeviation = baseDeviation + Math.Exp(breakdown_rate * (baseDeviation - limit_of_proportion));

            const double contamination_rate = 5e-3;

            const double contamination_scale = 2.0;

            double cleanProbability = DiffUtils.Erf(1 / (Math.Sqrt(2) * adjustedDeviation));
            double contaminatedProbability = DiffUtils.Erf(1 / (Math.Sqrt(2) * contamination_scale * adjustedDeviation));

            return (1 - contamination_rate) * cleanProbability + contamination_rate * contaminatedProbability;
        }

        private double strainDecay(double ms) => Math.Pow(0.15, ms / 1000);

        protected override double StrainValueAt(DifficultyHitObject current)
        {
            double decay = strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            currentStrain *= decay;
            currentStrain += calculateAdjustedDifficulty(current) * (1 - decay);

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentStrain);

            return currentStrain;
        }

        private double calculateAdjustedDifficulty(DifficultyHitObject current)
        {
            const double skill_multiplier_snap = 125;
            const double skill_multiplier_agility = 11500.0 * 1_000_000;
            const double skill_multiplier_flow = 255.0;

            double snapDifficulty = SnapAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skill_multiplier_snap;
            double agilityDifficulty = AgilityEvaluator.EvaluateDifficultyOf(current) * skill_multiplier_agility;
            double flowDifficulty = FlowAimEvaluator.EvaluateDifficultyOf(current, IncludeSliders) * skill_multiplier_flow;

            double totalDifficulty = calculateTotalValue(snapDifficulty, agilityDifficulty, flowDifficulty);

            if (Mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = Mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                totalDifficulty *= 1.0 - magnetisedStrength;
            }

            totalDifficulty *= 0.985 + DiffUtils.Pow(Math.Max(0, ((OsuDifficultyHitObject)current).OverallDifficulty), 2) / 4000;

            return totalDifficulty;
        }

        private double calculateTotalValue(double snapDifficulty, double agilityDifficulty, double flowDifficulty)
        {
            const double skill_multiplier_total = 5.35;
            const double combined_snap_norm_exponent = 1.2;

            // We compare flow to combined snap and agility because snap by itself doesn't have enough difficulty to be above flow on streams
            // Agility on the other hand is supposed to measure the rate of cursor velocity changes while snapping
            // So snapping every circle on a stream requires an enormous amount of agility at which point it's easier to flow
            double combinedSnapDifficulty = DiffUtils.Norm(combined_snap_norm_exponent, snapDifficulty, agilityDifficulty);

            double pSnap = calculateSnapFlowProbability(flowDifficulty / combinedSnapDifficulty);
            double pFlow = 1 - pSnap;

            if (Mods.Any(m => m is OsuModTouchDevice))
            {
                // we don't adjust agility here since agility represents TD difficulty in a decent enough way
                snapDifficulty = DiffUtils.Pow(snapDifficulty, 0.89);
                combinedSnapDifficulty = DiffUtils.Norm(combined_snap_norm_exponent, snapDifficulty, agilityDifficulty);
            }

            if (Mods.Any(m => m is OsuModRelax))
            {
                combinedSnapDifficulty *= 0.75;
                flowDifficulty *= 0.6;
            }

            double totalDifficulty = combinedSnapDifficulty * pSnap + flowDifficulty * pFlow;

            double totalStrain = totalDifficulty * skill_multiplier_total;

            return totalStrain;
        }

        // A function that turns the ratio of snap : flow into the probability of snapping/flowing
        // It has the constraints:
        // P(snap) + P(flow) = 1 (the object is always either snapped or flowed)
        // P(snap) = f(snap/flow), P(flow) = f(flow/snap) (ie snap and flow are symmetric and reversible)
        // Therefore: f(x) + f(1/x) = 1
        // 0 <= f(x) <= 1 (cannot have negative or greater than 100% probability of snapping or flowing)
        // This logistic function is a solution, which fits nicely with the general idea of interpolation and provides a tuneable constant
        private static double calculateSnapFlowProbability(double ratio)
        {
            const double k = 7.27;

            if (ratio == 0)
                return 0;

            if (double.IsNaN(ratio))
                return 1;

            return DiffUtils.Logistic(-k * Math.Log(ratio));
        }

        public double GetDifficultSliders()
        {
            if (sliderStrains.Count == 0)
                return 0;

            double maxSliderStrain = sliderStrains.Max();

            if (maxSliderStrain == 0)
                return 0;

            return sliderStrains.Sum(strain => DiffUtils.Logistic(strain / maxSliderStrain, 0.5, 12.0));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            double consistentTopStrain = difficultyValue * (1 - 0.9); // What would the top strain be if all strain values were identical

            if (consistentTopStrain == 0)
                return 0;

            // Use a weighted sum of all strains. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DiffUtils.Logistic(s / consistentTopStrain, 0.88, 10, 1.1));
        }
    }
}
