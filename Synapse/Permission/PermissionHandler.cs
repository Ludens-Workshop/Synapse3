﻿using System;
using Synapse.Config;
using System.Collections.Generic;
using Synapse.Api;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using GameCore;

namespace Synapse.Permission
{
    public class PermissionHandler
    {
        internal PermissionHandler() { }

        private SYML _permissionSYML;

        internal readonly Dictionary<string, SynapseGroup> Groups = new Dictionary<string, SynapseGroup>();
        internal ServerSection ServerSection;

        internal void Init()
        {
            _permissionSYML = new SYML(Server.Get.Files.PermissionFile);
            Reload();
        }

        public void Reload()
        {
            _permissionSYML.Load();
            ServerSection = new ServerSection();
            ServerSection = _permissionSYML.GetOrSetDefault("Server", ServerSection);
            Groups.Clear();

            foreach (var pair in _permissionSYML.Sections)
                if (pair.Key.ToLower() != "server")
                {
                    try
                    {
                        var group = pair.Value.LoadAs<SynapseGroup>();
                        Groups.Add(pair.Key, group);
                    }
                    catch (Exception e)
                    {
                        Logger.Get.Error($"Synapse-Permission: Section {pair.Key} in permission.syml is no SynapseGroup or ServerGroup\n{e}");
                    }
                }

            if (Groups.Count == 0)
            {
                var group = new SynapseGroup()
                {
                    Badge = "Owner",
                    Color = "red",
                    Cover = true,
                    Hidden = true,
                    KickPower = 254,
                    Members = new List<string> { "0000000@steam" },
                    Permissions = new List<string> { "*" },
                    RemoteAdmin = true,
                    RequiredKickPower = 255
                };

                AddServerGroup(group, "Owner");

                AddServerGroup(GetDefaultGroup(),"User");
            }

            foreach (var player in Server.Get.Players)
                player.RefreshPermission(player.HideRank);
        }

        public void AddServerGroup(SynapseGroup group,string groupname)
        {
            group = _permissionSYML.GetOrSetDefault(groupname, group);
            Groups.Add(groupname,group);
        }

        public SynapseGroup GetServerGroup(string groupname) => Groups.FirstOrDefault(x => x.Key.ToLower() == groupname.ToLower()).Value;

        public SynapseGroup GetPlayerGroup(Player player)
        {
            var group = Groups.Values.FirstOrDefault(x => x.Members?.Contains(player.UserId) ?? false);

            if (group != null)
                return group;

            var nwgroup = GetNorthwoodGroup();

            if (player.GlobalBadge != Api.Enum.GlobalBadge.None && player.GlobalBadge != Api.Enum.GlobalBadge.Patreon && nwgroup != null)
                return nwgroup;

            return GetDefaultGroup();
        }

        public SynapseGroup GetPlayerGroup(string UserID)
        {
            var group = Groups.Values.FirstOrDefault(x => x.Members == null ? false : x.Members.Contains(UserID));

            if (group != null)
                return group;

            var nwgroup = GetNorthwoodGroup();

            if (UserID.ToLower().Contains("@northwood") && nwgroup != null)
                return nwgroup;

            return GetDefaultGroup();
        }

        public SynapseGroup GetDefaultGroup()
        {
            var group = Groups.Values.FirstOrDefault(x => x.Default);

            if (group != null)
                return group;

            return new SynapseGroup
            {
                Default = true,
                Permissions = new List<string> { "synapse.command.help", "synapse.command.plugins" },
                Members = null,
            };
        }

        public SynapseGroup GetNorthwoodGroup() => Groups.Values.FirstOrDefault(x => x.Northwood);

        public void AddPlayerToGroup(string groupname, Player player)
        {
            var group = GetServerGroup(groupname);

            if (group == null)
            {
                Logger.Get.Warn($"Group {groupname} does not exist!");
                return;
            }
            
            group.Members.Add(player.UserId);
            
            _permissionSYML.Sections[groupname].Import(group);
            _permissionSYML.Store();
            
            Reload();
        }
    }
}
