﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Base
{
    public class BotRunner<T> where T : SwitchBotConfig
    {
        public readonly List<BotSource<T>> Bots = new List<BotSource<T>>();

        public bool IsRunning => Bots.Any(z => z.IsRunning);
        public bool RunOnce { get; private set; }

        public virtual void Add(SwitchRoutineExecutor<T> bot)
        {
            if (Bots.Any(z => z.Bot.Connection.IP == bot.Connection.IP && z.Bot.Config.ConnectionType != PokeConnectionType.USB))
                throw new ArgumentException($"{(bot.Config.ConnectionType == PokeConnectionType.WiFi ? nameof(bot.Connection.IP) : nameof(bot.Config.DeviceAddress))} has already been added.");
            Bots.Add(new BotSource<T>(bot));
        }

        public virtual bool Remove(string ip, string deviceAddress, bool callStop)
        {
            var match = deviceAddress == string.Empty ? Bots.Find(z => z.Bot.Connection.IP == ip) : Bots.Find(z => z.Bot.Config.DeviceAddress == deviceAddress);
            if (match == null)
                return false;

            if (callStop)
                match.Stop();
            return Bots.Remove(match);
        }

        public virtual void InitializeStart()
        {
            RunOnce = true;
        }

        public virtual void StartAll()
        {
            foreach (var b in Bots)
                b.Start();
        }

        public virtual void StopAll()
        {
            foreach (var b in Bots)
                b.Stop();
        }

        public virtual void PauseAll()
        {
            // Tell all the bots to go to Idle after finishing.
            foreach (var b in Bots)
                b.Pause();
        }

        public virtual void ResumeAll()
        {
            // Tell all the bots to go to Idle after finishing.
            foreach (var b in Bots)
                b.Resume();
        }

        public BotSource<T>? GetBot(T config) => Bots.Find(z => z.Bot.Config == config);
        public BotSource<T>? GetBot(string ip) => Bots.Find(z => z.Bot.Config.ConnectionType == PokeConnectionType.WiFi ? z.Bot.Config.IP == ip : z.Bot.Config.DeviceAddress == ip);
    }
}