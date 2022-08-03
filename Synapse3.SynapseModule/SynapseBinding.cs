﻿using System.Collections.Generic;
using System.Linq;
using Neuron.Core.Meta;
using Neuron.Core.Modules;
using Neuron.Core.Plugins;
using Synapse3.SynapseModule.Command;
using Synapse3.SynapseModule.Item;
using Synapse3.SynapseModule.Map.Rooms;
using Synapse3.SynapseModule.Map.Schematic.CustomAttributes;
using Synapse3.SynapseModule.Map.Scp914;
using Synapse3.SynapseModule.Permissions.RemoteAdmin;
using Synapse3.SynapseModule.Role;
using Synapse3.SynapseModule.Teams;

namespace Synapse3.SynapseModule;

public partial class Synapse
{
    private void MetaGenerateBindings(MetaGenerateBindingsEvent args)
    {
        OnGenerateRoleBinding(args);
        OnGenerateCommandBinding(args);
        OnGenerateTeamBinding(args);
        OnGenerateAttributeBinding(args);
        OnGenerate914ProcessorBinding(args);
        OnGenerateItemBinding(args);
        OnGenerateRoomBindings(args);
        OnGenerateRemoteAdminBindings(args);
    }

    private void OnGenerateRemoteAdminBindings(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<RaCategoryAttribute>(out var info)) return;
        if (!args.MetaType.Is<RemoteAdminCategory>()) return;

        info.CategoryType = args.MetaType.Type;
        args.Outputs.Add(new SynapseRaCategoryBinding()
        {
            Info = info
        });
    }

    private void OnGenerateRoomBindings(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<CustomRoomAttribute>(out var roomInfo)) return;
        if (!args.MetaType.Is<SynapseCustomRoom>()) return;

        roomInfo.RoomType = args.MetaType.Type;
        args.Outputs.Add(new SynapseRoomBinding()
        {
            Info = roomInfo
        });
    }

    private void OnGenerateRoleBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<RoleAttribute>(out var roleInformation)) return;
        if (!args.MetaType.Is<ISynapseRole>()) return;

        roleInformation.RoleScript = args.MetaType.Type;
        args.Outputs.Add(new SynapseRoleBinding()
        {
            Info = roleInformation
        });
    }
    
    private void OnGenerateCommandBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<SynapseCommandAttribute>(out var _)) return;
        if (!args.MetaType.Is<SynapseCommand>()) return;
        
        args.Outputs.Add(new SynapseCommandBinding()
        {
            Type = args.MetaType.Type,
        });
    }

    private void OnGenerateTeamBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<TeamAttribute>(out var info)) return;
        if (!args.MetaType.Is<ISynapseTeam>()) return;
        
        args.Outputs.Add(new SynapseTeamBinding()
        {
            Info = info,
            Type = args.MetaType.Type
        });
    }

    private void OnGenerateAttributeBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out var _)) return;
        if(!args.MetaType.Is<AttributeHandler>()) return;

        args.Outputs.Add(new SynapseCustomObjectAttributeBinding()
        {
            Type = args.MetaType.Type
        });
    }
    
    private void OnGenerate914ProcessorBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<Scp914ProcessorAttribute>(out var info)) return;
        if (!args.MetaType.Is<ISynapse914Processor>()) return;

        args.Outputs.Add(new SynapseScp914ProcessorBinding()
        {
            Processor = args.MetaType.Type,
            ReplaceHandlers = info.ReplaceHandlers
        });
    }

    private void OnGenerateItemBinding(MetaGenerateBindingsEvent args)
    {
        if (!args.MetaType.TryGetAttribute<AutomaticAttribute>(out _)) return;
        if (!args.MetaType.TryGetAttribute<ItemAttribute>(out var info)) return;
        if (!args.MetaType.Is<CustomItemHandler>()) return;

        args.Outputs.Add(new SynapseItemBinding()
        {
            Info = info,
            HandlerType = args.MetaType.Type
        });
    }

    private void OnPluginLoadLate(PluginLoadEvent args)
    {
        args.Context.MetaBindings
            .OfType<SynapseCommandBinding>()
            .ToList().ForEach(x=> SynapseCommandService.LoadBinding(x));

        args.Context.MetaBindings
            .OfType<SynapseRoleBinding>()
            .ToList().ForEach(x => RoleService.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseTeamBinding>()
            .ToList().ForEach(x => TeamService.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseCustomObjectAttributeBinding>()
            .ToList().ForEach(x => CustomAttributeService.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseScp914ProcessorBinding>()
            .ToList().ForEach(x => Scp914Service.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseItemBinding>()
            .ToList().ForEach(x => ItemService.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseRoomBinding>()
            .ToList().ForEach(x => RoomService.LoadBinding(x));
        
        args.Context.MetaBindings
            .OfType<SynapseRaCategoryBinding>()
            .ToList().ForEach(x => RemoteAdminCategoryService.LoadBinding(x));
    }
    
    internal readonly Queue<SynapseCommandBinding> ModuleCommandBindingQueue = new();
    internal readonly Queue<SynapseRoleBinding> ModuleRoleBindingQueue = new();
    internal readonly Queue<SynapseTeamBinding> ModuleTeamBindingQueue = new();
    internal readonly Queue<SynapseCustomObjectAttributeBinding> ModuleObjectAttributeBindingQueue = new();
    internal readonly Queue<SynapseScp914ProcessorBinding> ModuleScp914BindingQueue = new();
    internal readonly Queue<SynapseItemBinding> ModuleItemBindingQueue = new();
    internal readonly Queue<SynapseRoomBinding> ModuleRoomBindingQueue = new();
    internal readonly Queue<SynapseRaCategoryBinding> ModuleRaCategoryBindingQueue = new();

    private void LoadModuleLate(ModuleLoadEvent args)
    {
        args.Context.MetaBindings
            .OfType<SynapseCommandBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleCommandBindingQueue.Enqueue(binding);
            });
        
        args.Context.MetaBindings
            .OfType<SynapseRoleBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleRoleBindingQueue.Enqueue(binding);
            });
        
        args.Context.MetaBindings
            .OfType<SynapseTeamBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleTeamBindingQueue.Enqueue(binding);
            });
        
        args.Context.MetaBindings
            .OfType<SynapseCustomObjectAttributeBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleObjectAttributeBindingQueue.Enqueue(binding);
            });
        
        args.Context.MetaBindings
            .OfType<SynapseScp914ProcessorBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleScp914BindingQueue.Enqueue(binding);
            });

        
        args.Context.MetaBindings
            .OfType<SynapseItemBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleItemBindingQueue.Enqueue(binding);
            });

        
        args.Context.MetaBindings
            .OfType<SynapseRoomBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleRoomBindingQueue.Enqueue(binding);
            });
        
        args.Context.MetaBindings
            .OfType<SynapseRaCategoryBinding>()
            .ToList().ForEach(binding =>
            {
                ModuleRaCategoryBindingQueue.Enqueue(binding);
            });
    }
}