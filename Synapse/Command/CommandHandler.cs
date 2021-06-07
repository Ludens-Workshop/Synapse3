﻿using System.Collections.Generic;
using System.Linq;

namespace Synapse.Command
{
    public class CommandHandler : ICommandHandler
    {
        private readonly Dictionary<string, string> commandAliases = new();

        private readonly Dictionary<string, ICommand> commands = new();

        
        public List<ICommand> Commands => commands.Values.ToList();

        public bool TryGetCommand(string name, out ICommand cmd)
        {
            if (commandAliases.TryGetValue(name.ToLower(), out var alias))
                name = alias.ToLower();

            return commands.TryGetValue(name.ToLower(), out cmd);
        }

        public bool RegisterCommand(ICommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
                return false;

            commands.Add(command.Name.ToLower(), command);

            if (command.Aliases != null)
                foreach (var alias in command.Aliases)
                    if (!string.IsNullOrWhiteSpace(alias))
                        commandAliases.Add(alias.ToLower(), command.Name.ToLower());

            return true;
        }
    }
}
