// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to press keys in time with complex rhythmic patterning.
    /// </summary>
    public class RhythmComplexity : HarmonicSkill
    {
        private double skillMultiplier => 3;
        private readonly List<double> sliderStrains = new List<double>();

        private double currentDifficulty;
        private double strainDecayBase => 0.75;
        protected override double HarmonicScale => 50;
        protected override double DecayExponent => 1;

        public RhythmComplexity(Mod[] mods)
            : base(mods)
        {
        }

        private double strainDecay(double ms) => Math.Pow(strainDecayBase, ms / 1000);

        protected override double ObjectDifficultyOf(DifficultyHitObject current)
        {
            currentDifficulty *= strainDecay(((OsuDifficultyHitObject)current).AdjustedDeltaTime);

            currentDifficulty += RhythmComplexityEvaluator.EvaluateDifficultyOf(current) * skillMultiplier;

            if (current.BaseObject is Slider)
                sliderStrains.Add(currentDifficulty);

            return currentDifficulty;
        }

        public double CountTopWeightedSliders(double difficultyValue)
        {
            if (sliderStrains.Count == 0)
                return 0;

            if (NoteWeightSum == 0)
                return 0.0;

            double consistentTopNote = difficultyValue / NoteWeightSum; // What would the top note be if all note values were identical

            if (consistentTopNote == 0)
                return 0;

            // Use a weighted sum of all notes. Constants are arbitrary and give nice values
            return sliderStrains.Sum(s => DifficultyCalculationUtils.Logistic(s / consistentTopNote, 0.88, 10, 1.1));
        }
    }
}
