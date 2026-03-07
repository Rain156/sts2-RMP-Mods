using RemoveMultiplayerPlayerLimit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RemoveMultiplayerPlayetLimit.src
{
    public class Option
    {
        [JsonPropertyName("player_limit")]
        public int PlayerLimit { get; set; } = ModEntry.DefaultPlayerLimit;

        [JsonPropertyName("min_player")]
        public int MinPlayerLimit { get; set; } = ModEntry.MinSupportedPlayerLimit;

        [JsonPropertyName("max_player")]
        public int MaxPlayerLimit { get; set; } = ModEntry.MaxSupportedPlayerLimit;
    }
}
