// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Aggregation;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : Skill
    {
        private readonly List<double> sliderStrains = new List<double>();
        private readonly StaminaStrain staminaStrain = new StaminaStrain();

        private double harmonicWeightSum;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        protected override double ProcessInternal(DifficultyHitObject current)
        {
            const double skill_multiplier = 10.7;

            if (Mods.Any(m => m is OsuModRelax))
                return 0;

            double currentDifficulty = calculateAdjustedDifficulty(current);
            double currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);

            staminaStrain.AddDifficulty(currentDifficulty);
            double currentStamina = staminaStrain.CalculateStaminaStrain();

            double totalStrain = currentRhythm * currentStamina * skill_multiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalStrain);

            return totalStrain;
        }

        private double calculateAdjustedDifficulty(DifficultyHitObject current)
        {
            double difficulty = SpeedEvaluator.EvaluateDifficultyOf(current);

            if (Mods.Any(m => m is OsuModAutopilot))
                difficulty *= 0.5;

            return difficulty;
        }

        public override double DifficultyValue()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            (double difficulty, harmonicWeightSum) = HarmonicSeries.Aggregate(ObjectDifficulties, harmonicScale: 20);

            return difficulty;
        }

        public virtual double CountTopWeightedObjectDifficulties(double difficultyValue)
        {
            if (ObjectDifficulties.Count == 0)
                return 0.0;

            if (harmonicWeightSum == 0)
                return 0.0;

            double consistentTopObject = difficultyValue / harmonicWeightSum; // What would the top difficulty be if all object difficulties were identical

            if (consistentTopObject == 0)
                return 0;

            return ObjectDifficulties.Sum(d => DiffUtils.Logistic(d / consistentTopObject, 0.88, 10, 1.1));
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            if (harmonicWeightSum == 0)
                return 0.0;

            double consistentTopObject = difficultyValue / harmonicWeightSum; // What would the top note be if all note values were identical

            if (consistentTopObject == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DiffUtils.Logistic(s / consistentTopObject, 0.88, 10, 1.1));
        }

        private class StaminaStrain
        {
            private readonly record struct StaminaObject
            {
                public readonly double DifficultyReciprocal;
                public readonly double Count;

                public StaminaObject(double difficultyReciprocal, double count = 1)
                {
                    DifficultyReciprocal = difficultyReciprocal;
                    Count = count;
                }
            }

            private const int count_scalar = 1; // Can be used to horizontally stretch/squish the final result

            private readonly List<StaminaObject> staminaMemory = new List<StaminaObject>();

            public void AddDifficulty(double difficulty)
            {
                StaminaObject curr = new StaminaObject(1.0 / difficulty);

                staminaMemory.Add(curr);
            }

            // Useful links for stamina strain calc
            // https://www.wolframalpha.com/input?i2d=true&i=Sum%5B%5C%2840%29Divide%5B1%2C%5C%2840%29k*t%2Ba%5C%2841%29%5D%5C%2841%29%2C%7Bk%2C1%2Cx%7D%5D
            public double CalculateStaminaStrain()
            {
                if (staminaMemory.Count == 0) return 0;

                double currentStamina = 0;
                double tSum = 0;

                for (int i = staminaMemory.Count - 1; i >= 0; i--)
                {
                    // Always combine objects where possible
                    while (i > 0 && canMerge(staminaMemory[i], staminaMemory[i - 1], tSum))
                    {
                        StaminaObject s = staminaMemory[i];
                        staminaMemory.RemoveAt(i);
                        i--;
                        staminaMemory[i] = averageStaminaObjects(s, staminaMemory[i]);
                    }

                    StaminaObject obj = staminaMemory[i];

                    double t = obj.DifficultyReciprocal;
                    double x = obj.Count * count_scalar;

                    currentStamina += calculateObject(tSum, t, x);
                    tSum += t * x;
                }

                return currentStamina;
            }

            private static bool canMerge(StaminaObject s1, StaminaObject s2, double tSumBase)
            {
                double individualSum = 0;
                double avgSum = 0;

                // Calculate strain objects separately
                double tSum = tSumBase;

                double t = s1.DifficultyReciprocal;
                double x = s1.Count * count_scalar;
                individualSum += calculateObject(tSum, t, x);
                tSum += t * x;

                t = s2.DifficultyReciprocal;
                x = s2.Count * count_scalar;
                individualSum += calculateObject(tSum, t, x);

                // Calculate averaged stamina objects
                StaminaObject avg = averageStaminaObjects(s1, s2);
                tSum = tSumBase;

                t = avg.DifficultyReciprocal;
                x = avg.Count * count_scalar;
                avgSum += calculateObject(tSum, t, x);

                return Math.Max(individualSum, avgSum) <= 1.01 * Math.Min(individualSum, avgSum);
            }

            private static StaminaObject averageStaminaObjects(StaminaObject s1, StaminaObject s2) => new StaminaObject(
                (s1.DifficultyReciprocal * s1.Count + s2.DifficultyReciprocal * s2.Count) / (s1.Count + s2.Count),
                s1.Count + s2.Count);

            private static double calculateObject(double tSum, double t, double x)
            {
                // Simpler calculation for small values
                if (x < 12)
                {
                    double currentStamina = 0;

                    for (int j = 0; j < x; j++)
                    {
                        tSum += t;
                        currentStamina += 1 / tSum;
                    }

                    return currentStamina;
                }

                // Faster when calculating in bulk
                return (
                    DiffUtils.DiGamma(tSum / t + x + 1.0) -
                    DiffUtils.DiGamma(tSum / t + 1.0)
                ) / t;
            }
        }
    }
}
