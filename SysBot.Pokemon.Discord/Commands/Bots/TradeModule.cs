﻿using Discord;
using Discord.Commands;
using PKHeX.Core;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    [Summary("Queues new Link Code trades")]
    public class TradeModule : ModuleBase<SocketCommandContext>
    {
        private static TradeQueueInfo<PK8> Info => SysCordInstance.Self.Hub.Queues.Info;

        [Command("tradeList")]
        [Alias("tl")]
        [Summary("Prints the users in the trade queues.")]
        [RequireSudo]
        public async Task GetTradeListAsync()
        {
            string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
            var embed = new EmbedBuilder();
            embed.AddField(x =>
            {
                x.Name = "Pending Trades";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you the provided Pokémon file.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsyncAttach([Summary("Trade Code")] int code)
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
            if (!att.Success || !(att.Data is PK8 pk8))
            {
                await ReplyAsync("No PK8 attachment provided!").ConfigureAwait(false);
                return;
            }

            var sig = Context.User.GetFavor();
            await AddTradeToQueueAsync(code, Context.User.Username, pk8, sig).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
        {
            const int gen = 8;
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);

            if (set.InvalidLines.Count != 0)
            {
                var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }

            var sav = AutoLegalityWrapper.GetTrainerInfo(gen);
            var pkm = sav.GetLegal(template, out _);

            var la = new LegalityAnalysis(pkm);
            var spec = GameInfo.Strings.Species[template.Species];
            var invalid = !(pkm is PK8) || (!la.Valid && SysCordInstance.Self.Hub.Config.Legality.VerifyLegality);
            if (invalid && !Info.Hub.Config.Trade.Memes)
            {
                var imsg = $"Oops! I wasn't able to create something from that. Here's my best attempt for that {spec}!";
                await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
                return;
            }
            else if (Info.Hub.Config.Trade.Memes)
            {
                if (await TrollAsync(invalid, template).ConfigureAwait(false))
                    return;
            }

            pkm.ResetPartyStats();
            var sig = Context.User.GetFavor();
            await AddTradeToQueueAsync(code, Context.User.Username, (PK8)pkm, sig).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsync([Summary("Showdown Set")][Remainder] string content)
        {
            var code = Info.GetRandomTradeCode();
            await TradeAsync(code, content).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you the attached file.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsyncAttach()
        {
            var code = Info.GetRandomTradeCode();
            await TradeAsyncAttach(code).ConfigureAwait(false);
        }

        private async Task AddTradeToQueueAsync(int code, string trainerName, PK8 pk8, RequestSignificance sig)
        {
            if (!pk8.CanBeTraded() || !new TradeExtensions(Info.Hub).IsItemMule(pk8))
            {
                var msg = "Provided Pokémon content is blocked from trading!";
                await ReplyAsync($"{(!Info.Hub.Config.Trade.ItemMuleCustomMessage.Equals(string.Empty) && !Info.Hub.Config.Trade.ItemMuleSpecies.Equals(Species.None) ? Info.Hub.Config.Trade.ItemMuleCustomMessage : msg)}").ConfigureAwait(false);
                return;
            }

            if (Info.Hub.Config.Trade.DittoTrade && pk8.Species == 132)
                TradeExtensions.DittoTrade(pk8);

            if (Info.Hub.Config.Trade.EggTrade && pk8.Nickname == "Egg")
                TradeExtensions.EggTrade(pk8);

            var la = new LegalityAnalysis(pk8);
            if (!la.Valid && SysCordInstance.Self.Hub.Config.Legality.VerifyLegality)
            {
                await ReplyAsync("PK8 attachment is not legal, and cannot be traded!").ConfigureAwait(false);
                return;
            }

            await Context.AddToQueueAsync(code, trainerName, sig, pk8, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);
        }

        private async Task<bool> TrollAsync(bool invalid, IBattleTemplate set)
        {
            var rng = new System.Random();
            var path = Info.Hub.Config.Trade.MemeFileNames.Split(',');
            var msg = $"Oops! I wasn't able to create that {GameInfo.Strings.Species[set.Species]}. Here's a meme instead!\n";

            if (path.Length == 0)
                path = new string[] { "https://i.imgur.com/qaCwr09.png" }; //If memes enabled but none provided, use a default one.

            if (invalid || !ItemRestrictions.IsHeldItemAllowed(set.HeldItem, 8) || (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && set.Shiny) || Info.Hub.Config.Trade.EggTrade && set.Nickname == "Egg" && set.Species >= 888
                || (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && GameInfo.Strings.Species[set.Species] != Info.Hub.Config.Trade.ItemMuleSpecies.ToString() && !(Info.Hub.Config.Trade.DittoTrade && set.Species == 132 || Info.Hub.Config.Trade.EggTrade && set.Nickname == "Egg" && set.Species < 888)))
            {
                if (Info.Hub.Config.Trade.MemeFileNames.Contains(".com") || path.Length == 0)
                    _ = invalid == true ? await Context.Channel.SendMessageAsync($"{msg}{path[rng.Next(path.Length)]}").ConfigureAwait(false) : await Context.Channel.SendMessageAsync($"{path[rng.Next(path.Length)]}").ConfigureAwait(false);
                else _ = invalid == true ? await Context.Channel.SendMessageAsync($"{msg}{path[rng.Next(path.Length)]}").ConfigureAwait(false) : await Context.Channel.SendMessageAsync($"{path[rng.Next(path.Length)]}").ConfigureAwait(false);
                return true;
            }
            return false;
        }
    }
}
