﻿using Discord;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    internal class HelpInfoCommands : CommandsCommon
    {
        private readonly Embed _helpEmbed;
        private readonly Embed _helpConfigEmbed;

        public HelpInfoCommands(BirthdayBot inst, Configuration db) : base(inst, db)
        {
            var embeds = BuildHelpEmbeds();
            _helpEmbed = embeds.Item1;
            _helpConfigEmbed = embeds.Item2;
        }

        public override IEnumerable<(string, CommandHandler)> Commands =>
            new List<(string, CommandHandler)>() {
                ("help", CmdHelp),
                ("help-config", CmdHelpConfig),
                ("help-tzdata", CmdHelpTzdata),
                ("help-message", CmdHelpMessage),
                ("info", CmdInfo)
            };

        private (Embed, Embed) BuildHelpEmbeds()
        {
            var cpfx = $"●`{CommandPrefix}";

            // Normal section
            var cmdField = new EmbedFieldBuilder()
            {
                Name = "Commands",
                Value = $"{cpfx}help`, `{CommandPrefix}info`, `{CommandPrefix}help-tzdata`\n"
                    + $" » Help and informational messages.\n"
                    + $"{cpfx}recent` and `{CommandPrefix}upcoming`\n"
                    + $" » Lists recent and upcoming birthdays.\n"
                    + $"{cpfx}set (date) [zone]`\n"
                    + $" » Registers your birth date. Time zone is optional.\n"
                    + $" »» Examples: `{CommandPrefix}set jan-31`, `{CommandPrefix}set 15-aug America/Los_Angeles`.\n"
                    + $"{cpfx}zone (zone)`\n"
                    + $" » Sets your local time zone. See `{CommandPrefix}help-tzdata`.\n"
                    + $"{cpfx}remove`\n"
                    + $" » Removes your birthday information from this bot.\n"
                    + $"{cpfx}when (user)`\n"
                    + $" » Displays birthday information of the given user."
            };
            var cmdModField = new EmbedFieldBuilder()
            {
                Name = "Commands",
                Value = $"{cpfx}config`\n"
                    + $" » Edit bot configuration. See `{CommandPrefix}help-config`.\n"
                    + $"{cpfx}list`\n"
                    + $" » Exports all birthdays to file. Accepts `csv` as a parameter.\n"
                    + $"{cpfx}override (user ping or ID) (command w/ parameters)`\n"
                    + " » Perform certain commands on behalf of another user."
            };
            var helpRegular = new EmbedBuilder().AddField(cmdField).AddField(cmdModField);

            // Manager section
            var mpfx = cpfx + "config ";
            var configField1 = new EmbedFieldBuilder()
            {
                Name = "Basic settings",
                Value = $"{mpfx}role (role name or ID)`\n"
                    + " » Sets the role to apply to users having birthdays.\n"
                    + $"{mpfx}channel (channel name or ID)`\n"
                    + " » Sets the announcement channel. Leave blank to disable.\n"
                    + $"{mpfx}message (message)`, `{CommandPrefix}config messagepl (message)`\n"
                    + $" » Sets a custom announcement message. See `{CommandPrefix}help-message`.\n"
                    + $"{mpfx}ping (off|on)`\n"
                    + $" » Sets whether to ping the respective users in the announcement message.\n"
                    + $"{mpfx}zone (time zone name)`\n"
                    + $" » Sets the default server time zone. See `{CommandPrefix}help-tzdata`."
            };
            var configField2 = new EmbedFieldBuilder()
            {
                Name = "Access management",
                Value = $"{mpfx}modrole (role name, role ping, or ID)`\n"
                    + " » Establishes a role for bot moderators. Grants access to `bb.config` and `bb.override`.\n"
                    + $"{mpfx}block/unblock (user ping or ID)`\n"
                    + " » Prevents or allows usage of bot commands to the given user.\n"
                    + $"{mpfx}moderated on/off`\n"
                    + " » Prevents or allows using commands for all members excluding moderators."
            };

            var helpConfig = new EmbedBuilder()
            {
                Author = new EmbedAuthorBuilder() { Name = $"{CommandPrefix} config subcommands" },
                Description = "All the following subcommands are only usable by moderators and server managers."
            }.AddField(configField1).AddField(configField2);

            return (helpRegular.Build(), helpConfig.Build());
        }

        private async Task CmdHelp(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
            => await reqChannel.SendMessageAsync(embed: _helpEmbed);

        private async Task CmdHelpConfig(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
            => await reqChannel.SendMessageAsync(embed: _helpConfigEmbed);

        private async Task CmdHelpTzdata(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            const string tzhelp = "You may specify a time zone in order to have your birthday recognized with respect to your local time. "
                + "This bot only accepts zone names from the IANA Time Zone Database (a.k.a. Olson Database).\n\n"
                + "These names can be found at the following link, under the 'TZ database name' column: " +
            "https://en.wikipedia.org/wiki/List_of_tz_database_time_zones";
            var embed = new EmbedBuilder();
            embed.AddField(new EmbedFieldBuilder()
            {
                Name = "Time Zone Support",
                Value = tzhelp
            });
            await reqChannel.SendMessageAsync(embed: embed.Build());
        }

        private async Task CmdHelpMessage(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            const string msghelp = "The `message` and `messagepl` subcommands allow for editing the message sent into the announcement "
                + "channel (defined with `{0}config channel`). This feature is separated across two commands:\n"
                + "●`{0}config message`\n"
                + "●`{0}config messagepl`\n"
                + "The first command sets the message to be displayed when *one* user is having a birthday. The second command sets the "
                + "message for when *two or more* users are having birthdays ('pl' means plural). If only one of the two custom messages "
                + "are defined, it will be used for both cases.\n\n"
                + "To further allow customization, you may place the token `%n` in your message to specify where the name(s) should appear.\n"
                + "Leave the parameter blank to clear or reset the message to its default value.";
            const string msghelp2 = "As examples, these are the default announcement messages used by this bot:\n"
                + "`message`: {0}\n" + "`messagepl`: {1}";
            var embed = new EmbedBuilder().AddField(new EmbedFieldBuilder()
            {
                Name = "Custom announcement message",
                Value = string.Format(msghelp, CommandPrefix)
            }).AddField(new EmbedFieldBuilder()
            {
                Name = "Examples",
                Value = string.Format(msghelp2,
                    BackgroundServices.BirthdayRoleUpdate.DefaultAnnounce, BackgroundServices.BirthdayRoleUpdate.DefaultAnnouncePl)
            });
            await reqChannel.SendMessageAsync(embed: embed.Build());
        }

        private async Task CmdInfo(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            var strStats = new StringBuilder();
            var asmnm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            strStats.AppendLine("BirthdayBot v" + asmnm.Version.ToString(3));
            strStats.AppendLine("Server count: " + Discord.Guilds.Count.ToString());
            strStats.AppendLine("Shard #" + Discord.GetShardIdFor(reqChannel.Guild).ToString());
            strStats.AppendLine("Uptime: " + Common.BotUptime);

            // TODO fun stats
            // current birthdays, total names registered, unique time zones

            var embed = new EmbedBuilder()
            {
                Author = new EmbedAuthorBuilder()
                {
                    Name = "Thank you for using Birthday Bot!",
                    IconUrl = Discord.CurrentUser.GetAvatarUrl()
                },
                // TODO this message needs an overhaul
                Description = "Suggestions and feedback are always welcome. Please refer to the listing on Discord Bots "
                    + "(discord.bots.gg) for information on reaching my personal server. I may not be available often, but I am happy to "
                    + "respond to feedback in due time." // TODO update this string
            }.AddField(new EmbedFieldBuilder()
            {
                Name = "Statistics",
                Value = strStats.ToString()
            });
            await reqChannel.SendMessageAsync(embed: embed.Build());
        }
    }
}