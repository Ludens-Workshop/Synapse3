﻿using System.Collections.Generic;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using Mirror;
using Neuron.Core.Logging;
using Synapse3.SynapseModule.Map.Schematic;
using Synapse3.SynapseModule.Player;
using UnityEngine;

namespace Synapse3.SynapseModule.Map.Objects;

//TODO: Add DoorType
public class SynapseDoor : NetworkSynapseObject, IJoinUpdate
{
    public static Dictionary<SpawnableDoorType, BreakableDoor> Prefab { get; } = new();

    public DoorVariant Variant { get; }
    
    public override GameObject GameObject => Variant.gameObject;
    
    public override NetworkIdentity NetworkIdentity => Variant.netIdentity;
    
    public override ObjectType Type => ObjectType.Door;

    public override void OnDestroy()
    {
        Map._synapseDoors.Remove(this);
        base.OnDestroy();
        
        if (Parent is SynapseSchematic schematic) schematic._doors.Remove(this);
    }

    private string _name;

    public string Name
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_name)) return GameObject.name;
            return _name;
        }
    }

    public bool Open
    {
        get => Variant.IsConsideredOpen();
        set => Variant.NetworkTargetState = value;
    }

    public bool Locked
    {
        get => Variant.ActiveLocks > 0;
        set
        {
            if (value)
                Variant.ServerChangeLock(DoorLockReason.SpecialDoorFeature, true);
            else
                Variant.NetworkActiveLocks = 0;
        }
    }

    public DoorPermissions DoorPermissions
    {
        get => Variant.RequiredPermissions;
        set => Variant.RequiredPermissions = value;
    }

    public float Health
    {
        get => Variant is BreakableDoor breakableDoor ? breakableDoor._remainingHealth : -1f;
        set
        {
            if (Variant is BreakableDoor breakableDoor)
            {
                breakableDoor._remainingHealth = value;
                breakableDoor._maxHealth = value;
                NeuronLogger.For<Synapse>().Warn(breakableDoor._remainingHealth);
            }
        }
    }

    public bool IsDestroyed
    {
        get => Variant is IDamageableDoor { IsDestroyed: true };
        set
        {
            if (Variant is IDamageableDoor damageableDoor)
                damageableDoor.IsDestroyed = value;
        }
    }

    public bool UnDestroyable { get; set; }


    public bool IsBreakable => Variant is BreakableDoor;

    public bool IsPryable => Variant is PryableDoor;
    
    public SpawnableDoorType SpawnableType { get; private set; }

    public bool TryBreakDoor()
    {
        if (Variant is BreakableDoor breakableDoor)
        {
            breakableDoor.IsDestroyed = true;
            return true;
        }

        return false;
    }

    public bool TryPry()
    {
        if (Variant is PryableDoor door)
            return door.TryPryGate();

        return false;
    }

    public void LockWithReason(DoorLockReason reason) => Variant.ServerChangeLock(reason, true);

    public void Damage(float damageAmount, DoorDamageType damageType = DoorDamageType.None)
    {
        if (Variant is IDamageableDoor damageableDoor)
            damageableDoor.ServerDamage(damageAmount, damageType);
    }

    public override string ToString() => Name;


    public SynapseDoor(SpawnableDoorType type, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Variant = CreateDoor(type, position, rotation, scale);
        NeedsJoinUpdate = true;
        SetUp(type);
    }
    
    internal SynapseDoor(DoorVariant variant)
    {
        Variant = variant;
        SetUp(SpawnableDoorType.None);
    }

    internal SynapseDoor(SchematicConfiguration.DoorConfiguration configuration,
        SynapseSchematic schematic) :
        this(configuration.DoorType, configuration.Position, configuration.Rotation, configuration.Scale)
    {
        Parent = schematic;
        schematic._doors.Add(this);

        Locked = configuration.Locked;
        Open = configuration.Open;
        UnDestroyable = configuration.UnDestroyable;
        if (configuration.Health > 0f)
            Health = configuration.Health;
        OriginalScale = configuration.Scale;
        CustomAttributes = configuration.CustomAttributes;
        UpdateEveryFrame = configuration.UpdateEveryFrame;
    }
    private void SetUp(SpawnableDoorType type)
    {
        SpawnableType = type;
        
        Map._synapseDoors.Add(this);
        var comp = GameObject.AddComponent<SynapseObjectScript>();
        comp.Object = this;
        if (Variant.TryGetComponent<DoorNametagExtension>(out var nametag))
            _name = nametag.GetName;
    }
    
    private DoorVariant CreateDoor(SpawnableDoorType type, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        return CreateNetworkObject(Prefab[type], position, rotation, scale);
    }
    
    public enum SpawnableDoorType
    {
        None,
        Lcz,
        Hcz,
        Ez
    }

    public bool NeedsJoinUpdate { get; }
    public void Refresh(SynapsePlayer player)
    {
        player.SendNetworkMessage(NetworkIdentity.GetSpawnMessage());
    }
}