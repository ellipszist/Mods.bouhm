﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NPCMapLocations.Framework.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;

namespace NPCMapLocations.Framework
{
    // Handles custom maps (recolors of the mod map), custom NPCs, custom sprites, custom names, etc.
    public class ModCustomizations
    {
        /*********
        ** Accessors
        *********/
        public Dictionary<string, MapVector[]> MapVectors { get; set; } = new();

        /// <summary>Maps NPCs' internal names to their translated or customized display names.</summary>
        public Dictionary<string, string> Names { get; set; } = new();
        public HashSet<string> LocationExclusions { get; set; } = new();
        public Dictionary<string, MapTooltip> Tooltips { get; set; } = new();

        public string MapsRootPath { get; } = "maps";

        public string MapsPath { get; } = Path.Combine("maps", "_default");


        /*********
        ** Public methods
        *********/
        public void LoadCustomData(Dictionary<string, JObject> customNpcJson, Dictionary<string, JObject> customLocationJson)
        {
            this.LoadCustomLocations(customLocationJson);
            this.LoadCustomNpcs(customNpcJson);
        }


        /*********
        ** Private methods
        *********/
        private void LoadCustomLocations(Dictionary<string, JObject> customLocationJson)
        {
            foreach (var locationData in customLocationJson)
            {
                JObject location = locationData.Value;

                if (location.ContainsKey("MapVectors"))
                    this.AddCustomMapLocation(locationData.Key, (JArray)location.GetValue("MapVectors"));

                if (location.ContainsKey("MapTooltip"))
                    this.AddTooltip(locationData.Key, (JObject)location.GetValue("MapTooltip"));

                if (location.ContainsKey("Exclude") && (bool)location.GetValue("Exclude"))
                    this.LocationExclusions.Add(locationData.Key);
            }
        }

        // Handles customizations for NPCs
        // Custom NPCs and custom names or sprites for existing NPCs
        private void LoadCustomNpcs(Dictionary<string, JObject> customNpcJson)
        {
            // load custom NPC marker offsets and exclusions
            {
                // get defaults
                var markerOffsets = this.Merge(ModConstants.NpcMarkerOffsets, ModEntry.Globals.NpcMarkerOffsets);
                var exclusions = this.Merge(ModEntry.Globals.NpcExclusions);

                // get custom data
                foreach (var npcData in customNpcJson)
                {
                    var npc = npcData.Value;

                    if (npc.ContainsKey("Exclude") && (bool)npc.GetValue("Exclude"))
                    {
                        exclusions.Add(npcData.Key);
                        continue;
                    }

                    if (npc.ContainsKey("MarkerCropOffset"))
                        markerOffsets[npcData.Key] = (int)npc.GetValue("MarkerCropOffset");
                    else
                    {
                        NPC gameNpc = Game1.getCharacterFromName(npcData.Key);
                        if (gameNpc != null)
                        {
                            // If custom crop offset is not specified, default to 0
                            if (!markerOffsets.ContainsKey(gameNpc.Name))
                                markerOffsets[gameNpc.Name] = 0;

                            // Children sprites are short so give them a booster seat
                            if (gameNpc is Child)
                                markerOffsets[gameNpc.Name] += 7;
                        }
                    }
                }

                // Merge customizations into globals config
                ModEntry.Globals.NpcMarkerOffsets = markerOffsets;
                ModEntry.Globals.NpcExclusions = exclusions;
            }

            foreach (var character in Utility.getAllCharacters())
            {
                // Handle any modified NPC names 
                // Specifically mods that change names in dialogue files (displayName)
                this.Names[character.Name] = character.displayName ?? character.Name;
            }

            // Handle duplicate displayName -- custom NPCs that replaces villagers
            Dictionary<string, string> dupes = this.Names
              .Where(n1 => this.Names.Any(n2 => n2.Key != n1.Key && n2.Value == n1.Value))
              .ToDictionary(n => n.Key, n => n.Value);

            // Properly replace the villager with custom NPC
            foreach (var dupe in dupes)
            {
                if (dupe.Key != dupe.Value)
                {
                    this.Names[dupe.Key] = dupe.Value;
                }
                else
                {
                    this.Names.Remove(dupe.Key);
                }
            }

            ModEntry.StaticHelper.Data.WriteJsonFile($"config/{Constants.SaveFolderName}.json", ModEntry.Config);
            ModEntry.StaticHelper.Data.WriteJsonFile("config/globals.json", ModEntry.Globals);
        }

        private void AddTooltip(string locationName, JObject tooltip)
        {
            this.Tooltips[locationName] = new MapTooltip(
                (int)tooltip.GetValue("X"),
                (int)tooltip.GetValue("Y"),
                (int)tooltip.GetValue("Width"),
                (int)tooltip.GetValue("Height"),
                (string)tooltip.GetValue("PrimaryText"),
                (string)tooltip.GetValue("SecondaryText")
            );

            if (tooltip.ContainsKey("SecondaryText"))
                this.Tooltips[locationName].SecondaryText = (string)tooltip.GetValue("SecondaryText");
        }

        // Any custom locations with given location on the map
        private void AddCustomMapLocation(string locationName, JArray mapLocations)
        {
            var rawVectors = mapLocations.ToObject<JObject[]>();
            var parsedVectors = new MapVector[rawVectors.Length];
            for (int i = 0; i < rawVectors.Length; i++)
            {
                JObject rawVector = rawVectors[i];

                // Marker doesn't need to specify corresponding Tile position
                if (rawVector.GetValue("TileX") == null || rawVector.GetValue("TileY") == null)
                {
                    parsedVectors[i] = new MapVector(
                        (int)rawVector.GetValue("MapX"),
                        (int)rawVector.GetValue("MapY")
                    );
                }
                // Region must specify corresponding Tile positions for
                // Calculations on movement within location
                else
                {
                    parsedVectors[i] = new MapVector(
                        (int)rawVector.GetValue("MapX"),
                        (int)rawVector.GetValue("MapY"),
                        (int)rawVector.GetValue("TileX"),
                        (int)rawVector.GetValue("TileY")
                    );
                }
            }

            this.MapVectors[locationName] = parsedVectors;
        }

        /// <summary>Merge any number of dictionaries into a new dictionary.</summary>
        /// <typeparam name="TValue">The dictionary value type.</typeparam>
        /// <param name="dictionaries">The dictionaries to merge. Later dictionaries have precedence for conflicting keys.</param>
        /// <returns>Returns a new dictionary instance.</returns>
        private Dictionary<string, TValue> Merge<TValue>(params Dictionary<string, TValue>[] dictionaries)
        {
            Dictionary<string, TValue> merged = new();

            foreach (var dictionary in dictionaries)
            {
                foreach (var pair in dictionary)
                    merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        /// <summary>Merge any number of sets into a new set.</summary>
        /// <typeparam name="TValue">The set value type.</typeparam>
        /// <param name="sets">The sets to merge. Later sets have precedence for conflicting keys.</param>
        /// <returns>Returns a new set instance.</returns>
        private HashSet<TValue> Merge<TValue>(params HashSet<TValue>[] sets)
        {
            HashSet<TValue> merged = new();

            foreach (var set in sets)
            {
                foreach (TValue value in set)
                {
                    merged.Remove(value);
                    merged.Add(value);
                }
            }

            return merged;
        }
    }
}
