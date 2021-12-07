﻿using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;

namespace BetterFriendship
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        private ModConfig Config { get; set; }
        private BubbleDrawer BubbleDrawer { get; set; }

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            BubbleDrawer = new BubbleDrawer(Config);

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        /// <summary>
        ///     Raised after the game is launched, right before the first update tick. This happens once per game session
        ///     (unrelated to loading saves). All mods are loaded and initialised at this point, so this is a good time to set up
        ///     mod integrations.
        /// </summary>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null) return;

            SetupConfigMenu(configMenu);
        }

        /*********
        ** Private methods
        *********/
        /// <summary>Raised after the player presses a button on the keyboard, controller, or mouse.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private static void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // ignore if player hasn't loaded a save yet
            if (!Context.IsWorldReady)
                return;
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.eventUp) return;

            var currentLocation = Game1.currentLocation;

            if (!Config.DisplayBubbles || Config.GiftPreference == "none" && !Config.DisplayTalkPrompts) return;

            foreach (var npc in currentLocation.characters.Where(npc =>
                         npc.IsTownsfolk() || npc is Child child && child.daysOld.Value > 14))
            {
                if (!Game1.player.friendshipData.TryGetValue(npc.Name, out var friendship)) continue;

                if (Config.IgnoreMaxedFriendships && !FriendshipCanDecay(npc, friendship)) continue;

                if (Config.GiftPreference == "none" || friendship.GiftsToday != 0 || friendship.GiftsThisWeek >= 2)
                {
                    if (!Config.DisplayTalkPrompts || friendship.TalkedToToday) continue;

                    BubbleDrawer.DrawBubble(Game1.spriteBatch, npc, null, false, true);
                    continue;
                }

                var bestItems = Game1.player.Items.Where(x => x is Object)
                    .Select(x => (item: x as Object, taste: npc.getGiftTasteForThisItem(x)))
                    .Where(x => Config.GiftPreference switch
                    {
                        "love" => x.taste is 0,
                        "like" => x.taste is 0 or 2,
                        "neutral" => x.taste is not 4 or 6,
                        _ => false
                    })
                    .TakeTopPrioritized(Config)
                    .ToList();

                BubbleDrawer.DrawBubble(Game1.spriteBatch, npc, bestItems,
                    true,
                    Config.DisplayTalkPrompts && !friendship.TalkedToToday
                );
            }
        }

        private static bool FriendshipCanDecay(NPC npc, Friendship friendship)
        {
            if (Game1.player.spouse == npc.Name) return true;
            if (friendship.IsDating() && friendship.Points < 2500) return true;

            var isPreBouquet = npc.datable.Value && !friendship.IsDating() && !npc.isMarried();
            return !isPreBouquet && friendship.Points < 2500 || isPreBouquet && friendship.Points < 2000;
        }

        private void SetupConfigMenu(IGenericModConfigMenuApi configMenu)
        {
            configMenu.Register(
                ModManifest,
                () => Config = new ModConfig(),
                () => Helper.WriteConfig(Config)
            );

            configMenu.AddBoolOption(
                ModManifest,
                name: () => "Prompt to Speak w/ Villagers",
                tooltip: () => "Displays an indicator if a villager has not been talked to today.",
                getValue: () => Config.DisplayTalkPrompts,
                setValue: value => Config.DisplayTalkPrompts = value
            );

            configMenu.AddTextOption(
                ModManifest,
                name: () => "Gift Suggestion Preference",
                tooltip: () =>
                    "The lowest level of matching you want for gift suggestions. Gift suggestions come from items currently in your inventory ordered by receiver's gift preference, quality of item, and cheapest price.",
                getValue: () => Config.GiftPreference,
                setValue: value => Config.GiftPreference = value,
                allowedValues: new[] { "love", "like", "neutral", "none" },
                formatAllowedValue: value => value switch
                {
                    "love" => "Show only loved gifts",
                    "like" => "Show liked gifts & above",
                    "neutral" => "Show neutral gifts & above",
                    "none" => "Hide all suggestions",
                    _ => "UNKNOWN"
                }
            );

            configMenu.AddNumberOption(
                ModManifest,
                name: () => "Max Gifts to Show",
                tooltip: () => "The maximum number of gift suggestions to cycle through.",
                interval: 1,
                min: 1,
                max: 10,
                getValue: () => Config.GiftCycleCount,
                setValue: value => Config.GiftCycleCount = value
            );

            configMenu.AddNumberOption(
                ModManifest,
                name: () => "Gift Display Time (ms)",
                tooltip: () => "The time to display each suggested gift in milliseconds.",
                interval: 500,
                min: 500,
                max: 5000,
                getValue: () => Config.GiftCycleDelay,
                setValue: value => Config.GiftCycleDelay = value
            );

            configMenu.AddBoolOption(
                ModManifest,
                name: () => "Ignore Maxed Friendships",
                tooltip: () =>
                    "Hides suggestions and prompts for relationships that won't decay.",
                getValue: () => Config.IgnoreMaxedFriendships,
                setValue: value => Config.IgnoreMaxedFriendships = value
            );

            configMenu.AddBoolOption(
                ModManifest,
                name: () => "Only Show Highest Quality",
                tooltip: () =>
                    "Display only the highest quality version of items available. E.g. if you have both a gold and silver quality Hot Pepper, only the gold quality Hot Pepper will be suggested.",
                getValue: () => Config.OnlyHighestQuality,
                setValue: value => Config.OnlyHighestQuality = value
            );

            configMenu.AddBoolOption(
                ModManifest,
                name: () => "[!] Enable Suggestion Bubbles",
                tooltip: () =>
                    "Allows floating bubbles to be displayed over villagers. Warning: Turning this off will hide ALL floating bubbles enabled by this mod (talk prompts, gift suggestions, etc.)",
                getValue: () => Config.DisplayBubbles,
                setValue: value => Config.DisplayBubbles = value
            );
        }
    }
}