﻿using System;
using System.Linq;
using Neuron.Modules.Commands;
using Neuron.Modules.Commands.Command;

namespace Synapse3.SynapseModule.Command.SynapseCommands;

[SynapseRaCommand(
    CommandName = "Plugin",
    Aliases = new [] { "pl, plugins" },
    Parameters = new [] { "(pluginName)" },
    Description = "A command which provides information about the currently installed Plugins",
    Permission = "synapse.command.plugins",
    Platforms = new [] { CommandPlatform.PlayerConsole, CommandPlatform.RemoteAdmin, CommandPlatform.ServerConsole }
)]
public class PluginCommand : SynapseCommand
{
    private readonly ServerService _server;

    public PluginCommand(ServerService server)
    {
        _server = server;
    }
    
    public override void Execute(SynapseContext context, ref CommandResult result)
    {
        if(context.Arguments.Length > 0)
        {
            var pl = _server.Plugins.FirstOrDefault(x =>
                string.Equals(x.Attribute.Name, context.Arguments[0], StringComparison.OrdinalIgnoreCase));
            
             if(pl == null)
             {
                 result.Response = "No Plugin was found";
                 result.StatusCode = CommandStatusCode.Error;
                 return;
             }

             result.Response = $"\n{pl.Attribute.Name}" +
                               $"\n    - Description: {pl.Attribute.Description}" +
                               $"\n    - Author: {pl.Attribute.Author}" +
                               $"\n    - Version: {pl.Attribute.Version}" +
                               $"\n    - Repository: {pl.Attribute.Repository}" +
                               $"\n    - Website: {pl.Attribute.Website}";

             result.StatusCode = CommandStatusCode.Ok;
             return;
        }

        result.Response = "All Plugins:";
        
        foreach (var pl in _server.Plugins)
            result.Response += $"\n{pl.Attribute.Name} Version: {pl.Attribute.Version} by {pl.Attribute.Author}";

        result.StatusCode = CommandStatusCode.Ok;
    }
}