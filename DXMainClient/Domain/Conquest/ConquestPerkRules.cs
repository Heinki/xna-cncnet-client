#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using Rampastring.Tools;

namespace DTAClient.Domain.Conquest
{
    /// <summary>Applies declared game-pack rewards from the battle's committed inventory.</summary>
    public static class ConquestPerkRules
    {
        public static void Apply(IniFile destination, ConquestSettings settings, ConquestBattle battle,
            string gamePath, bool spawn, int playerStart = -1, int aiStart = -1, Func<string, string>? playerType = null)
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
                    if ((section.StartsWith("Player:", StringComparison.Ordinal) && playerLevel == 0) ||
                        (section.StartsWith("AI:", StringComparison.Ordinal) && aiLevel == 0))
                        continue;

                    string outputSection = section.StartsWith("Player:", StringComparison.Ordinal) ? section.Substring(7) :
                        section.StartsWith("AI:", StringComparison.Ordinal) ? section.Substring(3) : section;
                    foreach (var entry in code.GetSection(section).Keys)
                    {
                        if (spawn && entry.Value.Contains("{PlayerUnit:"))
                            throw new InvalidOperationException("Player unit aliases are available only during Conquest map preparation.");

                        string value = Regex.Replace(entry.Value, @"\{PlayerUnit:([^{}:]+)\}",
                            match => playerType?.Invoke(match.Groups[1].Value) ?? match.Groups[1].Value);
                        value = value.Replace("{PlayerLevel}", playerLevel.ToString(CultureInfo.InvariantCulture))
                            .Replace("{AILevel}", aiLevel.ToString(CultureInfo.InvariantCulture));
                        if (!spawn)
                        {
                            if ((value.Contains("{PlayerStartWaypoint}") && playerStart < 0) ||
                                (value.Contains("{AIStartWaypoint}") && aiStart < 0))
                                throw new InvalidOperationException("Conquest starting grants require client-assigned starting positions.");

                            value = value.Replace("{PlayerStartWaypoint}", playerStart.ToString(CultureInfo.InvariantCulture))
                                .Replace("{AIStartWaypoint}", aiStart.ToString(CultureInfo.InvariantCulture));
                        }
                        destination.SetStringValue(outputSection, entry.Key, value);
                    }
                }
            }
        }
    }
}
