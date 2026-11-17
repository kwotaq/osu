// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public static class RhythmIslandsPreprocessor
    {
        public static void PopulateIslands(List<DifficultyHitObject> difficultyHitObjects)
        {
            var islands = new List<Island>();

            Island? currIsland = null;
            Island? prevIsland = null;

            foreach (var currObj in difficultyHitObjects)
            {
                var osuCurrObj = (OsuDifficultyHitObject)currObj;
                var osuPrevObj = (OsuDifficultyHitObject?)currObj.Previous();

                if (osuPrevObj == null)
                    continue;

                int currDelta = (int)osuCurrObj.DeltaTime;
                int prevDelta = (int)osuPrevObj.DeltaTime;

                double deltaDifference = Math.Abs(prevDelta - currDelta);
                double deltaDifferenceEpsilon = osuCurrObj.HitWindowGreat;

                if (currIsland == null)
                {
                    currIsland = new Island(currDelta);
                    islands.Add(currIsland);
                }

                if (deltaDifference <= deltaDifferenceEpsilon)
                {
                    // island is still progressing
                    currIsland.AddDelta(currDelta);
                }

                if (deltaDifference > deltaDifferenceEpsilon)
                {
                    currIsland = new Island(currDelta)
                    {
                        PreviousIsland = prevIsland
                    };

                    prevIsland = currIsland;

                    islands.Add(currIsland);
                }

                osuCurrObj.Island = currIsland;
            }
        }
    }
}
