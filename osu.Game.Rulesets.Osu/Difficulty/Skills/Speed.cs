// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Aggregation;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys with regards to keeping up with the speed at which objects need to be hit.
    /// </summary>
    public class Speed : Skill
    {
        private readonly List<double> sliderStrains = new List<double>();

        private double currentBurstStrain;
        private double currentStaminaStrain;
        private double currentRhythm;

        private double harmonicWeightSum;

        public Speed(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecayBurst(double ms) => DiffUtils.Pow(0.3, ms / 1000);
        private double strainDecayStamina(double ms) => DiffUtils.Pow(0.1, DiffUtils.Pow(ms / 1000, 2.5) * 2.75);

        protected override double ProcessInternal(DifficultyHitObject current)
        {
            const double total_multiplier = 1.0;
            const double burst_multiplier = 0.98;
            const double stamina_multiplier = 0.045;

            if (Mods.Any(m => m is OsuModRelax))
                return 0;

            double burstDifficulty = adjustDifficulty(SpeedEvaluator.EvaluateDifficultyOf(current));
            double staminaDifficulty = adjustDifficulty(StaminaEvaluator.EvaluateDifficultyOf(current));
            double rhythmDifficulty = RhythmEvaluator.EvaluateDifficultyOf(current);

            currentRhythm = rhythmDifficulty;

            currentBurstStrain *= strainDecayBurst(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            currentBurstStrain += burstDifficulty * burst_multiplier;

            currentStaminaStrain *= strainDecayStamina(((OsuDifficultyHitObject)current).AdjustedDeltaTime);
            currentStaminaStrain += staminaDifficulty * stamina_multiplier;

            double totalValue = (currentBurstStrain * currentRhythm +
                                 currentStaminaStrain) * total_multiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(totalValue);

            return totalValue;
        }

        private double adjustDifficulty(double difficulty)
        {
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

        public double RelevantObjectCount()
        {
            if (ObjectDifficulties.Count == 0)
                return 0;

            double maxStrain = ObjectDifficulties.Max();

            if (maxStrain == 0)
                return 0;

            return ObjectDifficulties.Sum(strain => DiffUtils.Logistic(strain / maxStrain, 0.5, 12.0));
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
    }
}
