// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;

namespace osu.Game.Tests.Visual.UserInterface
{
    public partial class TestSceneFormControls : ThemeComparisonTestScene
    {
        public TestSceneFormControls()
            : base(false)
        {
        }

        protected override Drawable CreateContent() => new OsuContextMenuContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(5),
                    Padding = new MarginPadding(10),
                    Children = new Drawable[]
                    {
                        new FormTextBox
                        {
                            Caption = "Artist",
                            HintText = "Poot artist here!",
                            PlaceholderText = "Here is an artist",
                            TabbableContentContainer = this,
                        },
                        new FormTextBox
                        {
                            Caption = "Artist",
                            HintText = "Poot artist here!",
                            PlaceholderText = "Here is an artist",
                            Current = { Disabled = true },
                            TabbableContentContainer = this,
                        },
                        new FormNumberBox
                        {
                            Caption = "Number",
                            HintText = "Insert your favourite number",
                            PlaceholderText = "Mine is 42!",
                            TabbableContentContainer = this,
                        },
                    },
                },
            },
        };
    }
}
