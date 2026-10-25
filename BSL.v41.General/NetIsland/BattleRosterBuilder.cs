using BSL.v41.General.NetIsland.LaserBattle;
using BSL.v41.General.Networking;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Player;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Supercell.Titan.CommonUtils.Utils;
using BSL.v41.Titan.Graphic;
using BSL.v41.Titan.Mathematical.Data;
using BSL.v41.Titan.Utilities;
using BSL.v41.Tools.LaserCsv;
using BSL.v41.Tools.LaserCsv.Manufacturer.Laser;

namespace BSL.v41.General.NetIsland;

public static class BattleRosterBuilder
{
    private static readonly int[] BotBrawlers = [0, 1, 12, 13, 16, 17];

    public static void AddBots(LogicBattleModeServer battle, List<LogicPlayer> roster,
        AccountModel account, ServerConnection connection, int gameModeVariation)
    {
        var targetCount = LogicGameModeUtil.HasTwoTeams(gameModeVariation) ? 6 : 10;
        var botNumber = 0;

        while (roster.Count < targetCount)
        {
            var playerIndex = roster.Count;
            var display = new PlayerDisplayData();
            display.SetAvatarName($"Bot_{++botNumber}");
            display.SetPlayerThumbnail(0);
            display.SetNameColor(0);

            var bot = new LogicPlayer();
            bot.SetBot(true);
            bot.SetPlayerIndex(playerIndex);
            bot.SetTeamIndex(playerIndex % 2);
            bot.SetBounty(gameModeVariation == 3);
            bot.SetAccountModel(account);
            bot.SetPlayerDisplayData(display);

            // Bots never send UDP input; -1 also prevents duplicate session-map entries.
            bot.SessionId = -1;
            bot.HeroInfo.Add(BotBrawlers[Random.Shared.Next(BotBrawlers.Length)]);
            bot.HeroInfo.Add(1);
            bot.HeroInfo.Add(0);
            bot.HeroInfo.Add(0);
            // Keep bot loadout data minimal. Optional upgrade/accessory
            // references are not needed to spawn a bot and an invalid or
            // brawler-mismatched card reference can make the V41 client
            // reject the whole StartLoading roster.

            roster.Add(bot);
            battle.AddPlayer(bot, connection);
        }

        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Cmd,
            $"Battle roster ready: {roster.Count} players ({roster.Count(player => player.IsBot())} bots), " +
            $"mode variation: {gameModeVariation}.");
    }
}
