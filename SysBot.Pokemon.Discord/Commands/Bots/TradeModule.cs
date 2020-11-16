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
            if (Info.Hub.Config.Trade.Memes)
            {
                if (await TrollAsync(content, template).ConfigureAwait(false))
                    return;
            }

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
            if (invalid)
            {
                var imsg = $"Oops! I wasn't able to create something from that. Here's my best attempt for that {spec}!";
                await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
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
            if (!pk8.CanBeTraded() || !IsItemMule(pk8))
            {
                if (Info.Hub.Config.Trade.ItemMuleCustomMessage == string.Empty || IsItemMule(pk8))
                    Info.Hub.Config.Trade.ItemMuleCustomMessage = "Provided Pokémon content is blocked from trading!";
                await ReplyAsync($"{Info.Hub.Config.Trade.ItemMuleCustomMessage}").ConfigureAwait(false);
                return;
            }

            var la = new LegalityAnalysis(pk8);
            if (!la.Valid && SysCordInstance.Self.Hub.Config.Legality.VerifyLegality)
            {
                await ReplyAsync("PK8 attachment is not legal, and cannot be traded!").ConfigureAwait(false);
                return;
            }

            await Context.AddToQueueAsync(code, trainerName, sig, pk8, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);
        }

        private bool IsItemMule(PK8 pk8)
        {
            if (Info.Hub.Config.Trade.ItemMuleSpecies == Species.None)
                return true;
            return !(pk8.Species != SpeciesName.GetSpeciesID(Info.Hub.Config.Trade.ItemMuleSpecies.ToString()) || pk8.IsShiny);
        }

        private async Task<bool> TrollAsync(string content, IBattleTemplate set)
        {
            var path = Info.Hub.Config.Trade.MemeFileNames.Split(',');
            bool web = false;
            if (Info.Hub.Config.Trade.MemeFileNames.Contains(".com"))
                web = true;

            if (set.HeldItem == 16)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[0]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[0]).ConfigureAwait(false);
                return true;
            }
            else if (set.HeldItem == 500)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[1]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[1]).ConfigureAwait(false);
                return true;
            }
            else if (content.Contains($"★"))
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[2]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[2]).ConfigureAwait(false);
                return true;
            }
            else if (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && set.Shiny)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[3]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[3]).ConfigureAwait(false);
                return true;
            }
            else if (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && set.Species != SpeciesName.GetSpeciesID(Info.Hub.Config.Trade.ItemMuleSpecies.ToString()))
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[4]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[4]).ConfigureAwait(false);
                return true;
            }
            return false;
        }
    }
}
