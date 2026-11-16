// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class StaminaEvaluator
    {
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuNextObj = (OsuDifficultyHitObject?)osuCurrObj.Next();

            double deltaTime = osuCurrObj.AdjustedDeltaTime;

            if (osuNextObj != null)
            {
                const double next_delta_influence_on_doubletaps = 1;

                // If the current and next object are a doubletappable double then we want to partially combine their delta times to treat it as a single press
                double doubleTapFeasibility = osuCurrObj.CalculateDoubleTapFeasibility(osuNextObj);

                deltaTime += osuNextObj.AdjustedDeltaTime *
                             doubleTapFeasibility *
                             next_delta_influence_on_doubletaps;
            }

            return 1000 / deltaTime;
        }
    }
}
