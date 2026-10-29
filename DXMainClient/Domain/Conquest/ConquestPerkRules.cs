#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Rampastring.Tools;

namespace DTAClient.Domain.Conquest
{
    /// <summary>Applies declared game-pack rewards from the battle's committed inventory.</summary>
    public static class ConquestPerkRules
    {
        public static void Apply(IniFile destination, ConquestSettings settings, ConquestBattle battle,
            string gamePath, bool spawn, int playerStart = -1, int aiStart = -1, Func<string, string>? playerType = null,
            int playerSide = -1, int aiSide = -1)
        {
            foreach (var perk in settings.Perks)
            {
                int playerLevel = battle.PlayerPerks.GetValueOrDefault(perk.Id);
                int aiLevel = battle.AIPerks.GetValueOrDefault(perk.Id);
                string file = spawn ? perk.SpawnCodeFile : perk.MapCodeFile;
                if (file.Length == 0 || (playerLevel == 0 && aiLevel == 0))
                    continue;

                var code = ConquestBattleConfiguration.LoadGameIni(gamePath, file);
                foreach (string section in code.GetSections())
                {
                    var scoped = Regex.Match(section, @"^(Player|AI)(?:@(\d+))?:(.+)$");
                    bool player = scoped.Success && scoped.Groups[1].Value == "Player";
                    if (scoped.Success && ((player ? playerLevel : aiLevel) == 0 ||
                        (scoped.Groups[2].Success && ParseNumber(scoped.Groups[2].Value) != (player ? playerSide : aiSide))))
                    {
                        continue;
                    }

                    string outputSection = scoped.Success ? scoped.Groups[3].Value : section;
                    foreach (var entry in code.GetSection(section).Keys)
                    {
                        if (spawn && Regex.IsMatch(entry.Value, @"\{(?:PlayerUnit:|(?:Player|AI)(?:Start|Spawn))"))
                            throw new InvalidOperationException("Unit aliases and starting positions are available only during Conquest map preparation.");

                        string value = Regex.Replace(entry.Value, @"\{PlayerUnit:([^{}:]+)\}",
                            match => playerType?.Invoke(match.Groups[1].Value) ?? match.Groups[1].Value);
                        value = value.Replace("{PlayerLevel}", playerLevel.ToString(CultureInfo.InvariantCulture))
                            .Replace("{AILevel}", aiLevel.ToString(CultureInfo.InvariantCulture));
                        if (!spawn)
                        {
                            value = ReplaceStart(value, "Player", playerStart, destination);
                            value = ReplaceStart(value, "AI", aiStart, destination);
                        }

                        destination.SetStringValue(outputSection, entry.Key, value);
                    }
                }
            }
        }

        private static string ReplaceStart(string value, string faction, int start, IniFile map)
        {
            return Regex.Replace(value, @"\{" + faction + @"(StartWaypoint|Spawn|StartX:[+-]?\d+|StartY:[+-]?\d+|StartCell:[+-]?\d+:[+-]?\d+)\}", match =>
            {
                if (start < 0)
                    throw new InvalidOperationException("Conquest starting grants require client-assigned starting positions.");

                string token = match.Groups[1].Value;
                if (token == "StartWaypoint")
                    return start.ToString(CultureInfo.InvariantCulture);

                if (token == "Spawn")
                    return "Spawn" + (start + 1).ToString(CultureInfo.InvariantCulture);

                int cell = map.GetIntValue("Waypoints", start.ToString(CultureInfo.InvariantCulture), -1);
                if (cell < 0)
                    throw new InvalidOperationException("Conquest starting grants require an existing map waypoint.");

                string[] parts = token.Split(':');
                int offset = ParseNumber(parts[1]);
                int x = cell % 1000;
                int y = cell / 1000;
                if (parts[0] == "StartY")
                    y += offset;
                else
                    x += offset;

                if (parts[0] == "StartCell")
                    y += ParseNumber(parts[2]);

                if (x < 0 || x >= 1000 || y < 0 || y >= 1000)
                    throw new InvalidOperationException("Conquest starting grant coordinates are out of range.");

                return (parts[0] == "StartX" ? x : parts[0] == "StartY" ? y : y * 1000 + x).ToString(CultureInfo.InvariantCulture);
            });
        }

        private static int ParseNumber(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < -999 || number > 999)
                throw new InvalidDataException("Conquest adapter numbers must be integers between -999 and 999.");

            return number;
        }
    }
}